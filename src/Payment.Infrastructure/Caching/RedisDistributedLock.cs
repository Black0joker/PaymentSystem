using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Caching;
using Payment.Infrastructure.Redis;
using StackExchange.Redis;

namespace Payment.Infrastructure.Caching;

/// <summary>
/// Phase 11 — Redis-based distributed lock (single-node variant).
///
/// Acquire: SET key token NX PX expiry  — atomic set-if-not-exists with TTL.
/// Release: Lua compare-and-delete so a lock is only ever released by the
///          instance that acquired it (token check).
///
/// The TTL guarantees a crashed holder cannot block the resource forever.
///
/// Remember: this lock is an OPTIMIZATION to avoid redundant work (e.g. two
/// concurrent refund requests for the same payment). Correctness does not
/// depend on it — domain guards and DB constraints are authoritative.
/// </summary>
public class RedisDistributedLock : IDistributedLock
{
    private const string ReleaseScript = @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
else
    return 0
end";

    private readonly RedisConnectionFactory _connectionFactory;
    private readonly ILogger<RedisDistributedLock> _logger;

    public RedisDistributedLock(RedisConnectionFactory connectionFactory, ILogger<RedisDistributedLock> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionFactory.GetConnectionAsync(cancellationToken);
            if (connection is null)
                return null;

            var fullKey = _connectionFactory.PrefixedKey("lock:" + key);
            var token = Guid.NewGuid().ToString();

            var acquired = await connection.GetDatabase()
                .StringSetAsync(fullKey, token, expiry, When.NotExists);

            if (!acquired)
                return null;

            return new LockHandle(connection, _connectionFactory, fullKey, token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lock acquisition failed for {Key}; proceeding without lock.", key);
            return null;
        }
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly RedisConnectionFactory _factory;
        private readonly string _key;
        private readonly string _token;
        private int _disposed;

        public LockHandle(IConnectionMultiplexer connection, RedisConnectionFactory factory, string key, string token)
        {
            _connection = connection;
            _factory = factory;
            _key = key;
            _token = token;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try
            {
                await _connection.GetDatabase().ScriptEvaluateAsync(
                    ReleaseScript,
                    new RedisKey[] { _key },
                    new RedisValue[] { _token });
            }
            catch
            {
                // Best effort: the TTL will release the lock on its own.
            }
        }
    }
}

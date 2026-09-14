using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Payment.Infrastructure.Redis;

/// <summary>
/// Owns the single shared ConnectionMultiplexer for the application.
/// StackExchange.Redis multiplexes all commands over one connection,
/// so a singleton multiplexer is the correct usage pattern.
///
/// The connection is created lazily and failures never escape to callers:
/// every Redis feature degrades gracefully (fail-open) when Redis is
/// unavailable, because SQL Server remains the source of truth.
/// </summary>
public sealed class RedisConnectionFactory : IDisposable
{
    private readonly RedisOptions _options;
    private readonly ILogger<RedisConnectionFactory> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _connection;
    private bool _permanentlyFailed;

    public RedisConnectionFactory(IOptions<RedisOptions> options, ILogger<RedisConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the shared connection, or null when Redis is not configured
    /// or cannot be reached.
    /// </summary>
    public async Task<IConnectionMultiplexer?> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured || _permanentlyFailed)
            return null;

        if (_connection is { IsConnected: true })
            return _connection;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsConnected: true })
                return _connection;

            _connection?.Dispose();
            _connection = null;

            var configOptions = ConfigurationOptions.Parse(_options.ConnectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectTimeout = 2000;
            configOptions.SyncTimeout = 2000;

            _connection = await ConnectionMultiplexer.ConnectAsync(configOptions);

            if (!_connection.IsConnected)
            {
                _logger.LogWarning("Redis connection could not be established. Redis features disabled (SQL remains authoritative).");
                _permanentlyFailed = true;
                _connection.Dispose();
                _connection = null;
                return null;
            }

            _logger.LogInformation("Connected to Redis.");
            return _connection;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable. Redis features disabled (SQL remains authoritative).");
            _permanentlyFailed = true;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string PrefixedKey(string key) => _options.KeyPrefix + key;

    public void Dispose()
    {
        _connection?.Dispose();
        _gate.Dispose();
    }
}

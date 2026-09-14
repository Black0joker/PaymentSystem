using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.Caching;
using Payment.Application.Abstractions.Notifications;
using Payment.Application.Abstractions.Outbox;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.RateLimiting;
using Payment.Application.Abstractions.Persistence;
using MassTransit;
using Payment.Infrastructure.Caching;
using Payment.Infrastructure.Messaging;
using Payment.Infrastructure.Messaging.Consumers;
using Payment.Infrastructure.Notifications;
using Payment.Infrastructure.Outbox;
using Payment.Infrastructure.Payments;
using Payment.Infrastructure.Payments.Fake;
using Payment.Infrastructure.Payments.Stripe;
using Payment.Infrastructure.Persistence;
using Payment.Infrastructure.RateLimiting;
using Payment.Infrastructure.Reconciliation;
using Payment.Infrastructure.Redis;

namespace Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        // Stripe configuration
        services.Configure<StripeSettings>(configuration.GetSection(StripeSettings.SectionName));

        // Payment provider registration
        // Use "Stripe" or "Fake" in configuration to switch providers.
        // Every provider is wrapped in a resilience decorator (Phase 7) that
        // retries transient failures (timeouts, 5xx, rate limits).
        var providerType = configuration.GetValue<string>("PaymentProvider") ?? "Stripe";
        var useFake = providerType.Equals("Fake", StringComparison.OrdinalIgnoreCase);

        if (!useFake)
        {
            // Set the global Stripe API key
            var stripeSettings = configuration.GetSection(StripeSettings.SectionName).Get<StripeSettings>();
            if (stripeSettings != null && !string.IsNullOrEmpty(stripeSettings.SecretKey))
            {
                Stripe.StripeConfiguration.ApiKey = stripeSettings.SecretKey;
            }
        }

        services.AddSingleton<IPaymentProvider>(sp =>
        {
            IPaymentProvider inner = useFake
                ? new FakePaymentProvider(sp.GetRequiredService<ILogger<FakePaymentProvider>>())
                : new StripePaymentProvider(
                    sp.GetRequiredService<IOptions<StripeSettings>>(),
                    sp.GetRequiredService<ILogger<StripePaymentProvider>>());

            return new ResilientPaymentProviderDecorator(
                inner,
                sp.GetRequiredService<ILogger<ResilientPaymentProviderDecorator>>());
        });

        // Outbox (Phase 8): reliable event publishing
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
        services.AddHostedService<OutboxProcessorWorker>();

        // Reconciliation (Phase 10): repair payments stuck in Pending/Processing
        services.Configure<ReconciliationOptions>(configuration.GetSection(ReconciliationOptions.SectionName));
        services.AddHostedService<PaymentReconciliationWorker>();

        // =====================================================================
        // Phase 11: Redis — caching, distributed locking, rate limiting.
        //
        // Redis is an OPTIMIZATION layer only; SQL Server remains the source
        // of truth. When no Redis connection is configured (local dev, tests)
        // in-memory implementations provide identical single-process behavior.
        // =====================================================================

        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddSingleton<RedisConnectionFactory>();

        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

        if (redisOptions.IsConfigured)
        {
            services.AddSingleton<ICacheService, RedisCacheService>();
            services.AddSingleton<IDistributedLock, RedisDistributedLock>();
            services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        }
        else
        {
            services.AddSingleton<ICacheService, InMemoryCacheService>();
            services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
            services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
        }

        // =====================================================================
        // Phase 12: Messaging — MassTransit + RabbitMQ (or in-memory bus).
        //
        // The transactional outbox remains the reliability boundary: events
        // are committed to SQL first, then MassTransitOutboxPublisher pushes
        // them to the bus. Consumers react independently:
        //   OrderPaidEvent -> EnrollmentConsumer (course access)
        //   all events     -> NotificationConsumer (customer messaging)
        //   all events     -> AnalyticsConsumer (event history)
        //
        // Provider "None" keeps the Phase 8 logging publisher (no bus).
        // =====================================================================

        services.AddSingleton<INotificationService, LoggingNotificationService>();

        var messagingOptions = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));

        if (string.Equals(messagingOptions.Provider, MessagingOptions.ProviderNone, StringComparison.OrdinalIgnoreCase))
        {
            // No bus: keep logging-only publishing (Phase 8 behavior).
            // (LoggingOutboxPublisher already registered above with the outbox.)
            services.Remove(services.Single(d => d.ServiceType == typeof(IOutboxPublisher)));
            services.AddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
        }
        else
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<EnrollmentConsumer>();
                x.AddConsumer<NotificationConsumer>();
                x.AddConsumer<AnalyticsConsumer>();

                if (string.Equals(messagingOptions.Provider, MessagingOptions.ProviderRabbitMq, StringComparison.OrdinalIgnoreCase))
                {
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var rmq = messagingOptions.RabbitMq;
                        cfg.Host(rmq.Host, rmq.VirtualHost, h =>
                        {
                            h.Username(rmq.Username);
                            h.Password(rmq.Password);
                        });
                        cfg.ConfigureEndpoints(context);
                    });
                }
                else
                {
                    // In-memory transport: local development and tests.
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });
                }
            });

            // Replace the logging publisher with the MassTransit-backed one.
            services.Remove(services.Single(d => d.ServiceType == typeof(IOutboxPublisher)));
            services.AddSingleton<IOutboxPublisher, MassTransitOutboxPublisher>();
        }

        return services;
    }
}

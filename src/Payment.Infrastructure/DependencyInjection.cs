using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.Outbox;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Infrastructure.Outbox;
using Payment.Infrastructure.Payments;
using Payment.Infrastructure.Payments.Fake;
using Payment.Infrastructure.Payments.Stripe;
using Payment.Infrastructure.Persistence;

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

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
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
        // Use "Stripe" or "Fake" in configuration to switch providers
        var providerType = configuration.GetValue<string>("PaymentProvider") ?? "Stripe";

        if (providerType.Equals("Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        }
        else
        {
            // Set the global Stripe API key
            var stripeSettings = configuration.GetSection(StripeSettings.SectionName).Get<StripeSettings>();
            if (stripeSettings != null && !string.IsNullOrEmpty(stripeSettings.SecretKey))
            {
                Stripe.StripeConfiguration.ApiKey = stripeSettings.SecretKey;
            }

            services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
        }

        return services;
    }
}

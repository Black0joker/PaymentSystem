namespace Payment.Infrastructure.Payments.Stripe;

/// <summary>
/// Configuration settings for Stripe integration.
/// </summary>
public class StripeSettings
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = null!;
    public string PublishableKey { get; set; } = null!;
    public string WebhookSecret { get; set; } = null!;
}

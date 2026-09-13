namespace Payment.Application.Abstractions.Payments;

/// <summary>
/// Thrown when webhook signature verification fails.
/// </summary>
public class WebhookVerificationException : Exception
{
    public WebhookVerificationException(string message) : base(message) { }
    public WebhookVerificationException(string message, Exception inner) : base(message, inner) { }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.Payments;
using Stripe;
using Stripe.Checkout;

namespace Payment.Infrastructure.Payments.Stripe;

/// <summary>
/// Stripe implementation of the payment provider abstraction.
/// All Stripe-specific code lives here — it never leaks into Application or Domain.
/// </summary>
public class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeSettings _settings;
    private readonly ILogger<StripePaymentProvider> _logger;

    public StripePaymentProvider(
        IOptions<StripeSettings> settings,
        ILogger<StripePaymentProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = request.LineItems.Select(item => new SessionLineItemOptions
            {
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = request.Currency.ToLowerInvariant(),
                    UnitAmountDecimal = item.UnitPrice * 100, // Stripe uses cents
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = item.Name
                    }
                },
                Quantity = item.Quantity
            }).ToList(),
            Mode = "payment",
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ClientReferenceId = request.OrderId,
            Metadata = new Dictionary<string, string>
            {
                ["order_number"] = request.OrderNumber
            },
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        if (!string.IsNullOrEmpty(request.CustomerEmail))
        {
            options.CustomerEmail = request.CustomerEmail;
        }

        try
        {
            var service = new SessionService();
            var session = await service.CreateAsync(options, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Stripe checkout session created. SessionId={SessionId}, OrderId={OrderId}",
                session.Id, request.OrderId);

            return new CheckoutSessionResult
            {
                SessionId = session.Id,
                CheckoutUrl = session.Url,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "Failed to create Stripe checkout session. OrderId={OrderId}, Error={Error}",
                request.OrderId, ex.StripeError?.Message);
            throw;
        }
    }

    public PaymentWebhookEvent VerifyWebhook(string payload, string signature)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                payload,
                signature,
                _settings.WebhookSecret);

            _logger.LogInformation(
                "Stripe webhook verified. EventId={EventId}, Type={Type}",
                stripeEvent.Id, stripeEvent.Type);

            return MapStripeEvent(stripeEvent, payload);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            throw new WebhookVerificationException("Webhook signature verification failed.", ex);
        }
    }

    public async Task<ProviderPaymentDetails> GetPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        var service = new PaymentIntentService();
        var paymentIntent = await service.GetAsync(providerPaymentId, cancellationToken: cancellationToken);

        return new ProviderPaymentDetails
        {
            ProviderPaymentId = paymentIntent.Id,
            Status = paymentIntent.Status,
            Amount = paymentIntent.Amount / 100m,
            Currency = paymentIntent.Currency.ToUpperInvariant(),
            CompletedAt = paymentIntent.Created
        };
    }

    private static PaymentWebhookEvent MapStripeEvent(Event stripeEvent, string rawPayload)
    {
        var webhookEvent = new PaymentWebhookEvent
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            Provider = "Stripe",
            RawPayload = rawPayload
        };

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                var session = stripeEvent.Data.Object as Session;
                if (session != null)
                {
                    webhookEvent.SessionId = session.Id;
                    webhookEvent.PaymentIntentId = session.PaymentIntentId;
                    webhookEvent.Amount = session.AmountTotal / 100m;
                    webhookEvent.Currency = session.Currency?.ToUpperInvariant();
                }
                break;

            case "payment_intent.succeeded":
            case "payment_intent.payment_failed":
                var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                if (paymentIntent != null)
                {
                    webhookEvent.PaymentIntentId = paymentIntent.Id;
                    webhookEvent.Status = paymentIntent.Status;
                    webhookEvent.Amount = paymentIntent.Amount / 100m;
                    webhookEvent.Currency = paymentIntent.Currency?.ToUpperInvariant();
                    webhookEvent.FailureReason = paymentIntent.LastPaymentError?.Message;
                }
                break;

            case "charge.refunded":
                var charge = stripeEvent.Data.Object as Charge;
                if (charge != null)
                {
                    webhookEvent.PaymentIntentId = charge.PaymentIntentId;
                    webhookEvent.Amount = charge.Amount / 100m;
                    webhookEvent.Currency = charge.Currency?.ToUpperInvariant();
                }
                break;
        }

        return webhookEvent;
    }
}

using Payment.Domain.Entities;
using Payment.Domain.Enums;

namespace Payment.Tests.Domain;

public class WebhookEventTests
{
    [Fact]
    public void NewWebhookEvent_ShouldHaveReceivedStatus()
    {
        var webhookEvent = new WebhookEvent(
            "Stripe",
            "evt_test_123",
            "payment_intent.succeeded",
            "{\"test\": true}");

        Assert.Equal(WebhookEventStatus.Received, webhookEvent.Status);
        Assert.Null(webhookEvent.ProcessedAt);
    }

    [Fact]
    public void MarkProcessed_ShouldSetProcessedAtAndStatus()
    {
        var webhookEvent = new WebhookEvent(
            "Stripe",
            "evt_test_123",
            "payment_intent.succeeded",
            "{\"test\": true}");

        webhookEvent.MarkProcessed();

        Assert.Equal(WebhookEventStatus.Processed, webhookEvent.Status);
        Assert.NotNull(webhookEvent.ProcessedAt);
    }

    [Fact]
    public void MarkFailed_ShouldSetErrorAndStatus()
    {
        var webhookEvent = new WebhookEvent(
            "Stripe",
            "evt_test_123",
            "payment_intent.succeeded",
            "{\"test\": true}");
        var error = "Payment not found";

        webhookEvent.MarkFailed(error);

        Assert.Equal(WebhookEventStatus.Failed, webhookEvent.Status);
        Assert.Equal(error, webhookEvent.Error);
        Assert.NotNull(webhookEvent.ProcessedAt);
    }

    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var webhookEvent = new WebhookEvent(
            "Stripe",
            "evt_test_456",
            "checkout.session.completed",
            "{\"amount\": 100}");

        Assert.Equal("Stripe", webhookEvent.Provider);
        Assert.Equal("evt_test_456", webhookEvent.ProviderEventId);
        Assert.Equal("checkout.session.completed", webhookEvent.EventType);
        Assert.Equal("{\"amount\": 100}", webhookEvent.Payload);
    }
}

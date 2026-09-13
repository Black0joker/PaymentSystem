namespace Payment.Application.Abstractions.Payments;

/// <summary>
/// Request to create a checkout session.
/// </summary>
public class CheckoutRequest
{
    public string OrderId { get; set; } = null!;
    public string OrderNumber { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string CustomerEmail { get; set; } = null!;
    public List<CheckoutLineItem> LineItems { get; set; } = new();
    public string SuccessUrl { get; set; } = null!;
    public string CancelUrl { get; set; } = null!;
}

public class CheckoutLineItem
{
    public string Name { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Result of creating a checkout session.
/// </summary>
public class CheckoutSessionResult
{
    public string SessionId { get; set; } = null!;
    public string CheckoutUrl { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}

namespace Payment.Domain.Enums;

public enum OrderStatus
{
    Pending = 0,
    PaymentProcessing = 1,
    Paid = 2,
    Failed = 3,
    Cancelled = 4,
    Expired = 5
}

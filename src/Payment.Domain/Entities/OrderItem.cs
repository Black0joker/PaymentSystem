using Payment.Domain.Common;

namespace Payment.Domain.Entities;

public class OrderItem : BaseEntity
{
    public Guid OrderId { get; private set; }
    public Guid CourseId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }

    // Navigation properties
    public Order Order { get; private set; } = null!;
    public Course Course { get; private set; } = null!;

    private OrderItem() { } // EF Core constructor

    public OrderItem(Guid orderId, Guid courseId, decimal unitPrice, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));

        OrderId = orderId;
        CourseId = courseId;
        UnitPrice = unitPrice;
        Quantity = quantity;
        TotalPrice = unitPrice * quantity;
    }
}

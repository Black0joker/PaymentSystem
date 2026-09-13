using Payment.Domain.Common;

namespace Payment.Domain.Entities;

public class Course : BaseEntity
{
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "USD";
    public bool IsActive { get; private set; } = true;

    // Navigation properties
    public ICollection<OrderItem> OrderItems { get; private set; } = new List<OrderItem>();

    private Course() { } // EF Core constructor

    public Course(string title, string description, decimal price, string currency = "USD")
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        Title = title;
        Description = description;
        Price = price;
        Currency = currency;
    }

    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(newPrice));

        Price = newPrice;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }
}

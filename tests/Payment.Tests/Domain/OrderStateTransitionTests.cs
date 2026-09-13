using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Domain.Exceptions;

namespace Payment.Tests.Domain;

public class OrderStateTransitionTests
{
    private static Order CreateOrder()
    {
        var userId = Guid.NewGuid();
        return new Order(userId);
    }

    private static Course CreateCourse(decimal price = 50m)
    {
        return new Course("Test Course", "Description", price, "USD");
    }

    [Fact]
    public void NewOrder_ShouldHavePendingStatus()
    {
        var order = CreateOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void MarkPaymentProcessing_FromPending_ShouldSucceed()
    {
        var order = CreateOrder();

        order.MarkPaymentProcessing();

        Assert.Equal(OrderStatus.PaymentProcessing, order.Status);
    }

    [Fact]
    public void MarkPaid_FromPaymentProcessing_ShouldSucceed()
    {
        var order = CreateOrder();
        order.MarkPaymentProcessing();

        order.MarkPaid();

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void MarkFailed_FromPaymentProcessing_ShouldSucceed()
    {
        var order = CreateOrder();
        order.MarkPaymentProcessing();

        order.MarkFailed();

        Assert.Equal(OrderStatus.Failed, order.Status);
    }

    [Fact]
    public void Cancel_FromPending_ShouldSucceed()
    {
        var order = CreateOrder();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void MarkExpired_FromPending_ShouldSucceed()
    {
        var order = CreateOrder();

        order.MarkExpired();

        Assert.Equal(OrderStatus.Expired, order.Status);
    }

    [Fact]
    public void MarkPaid_FromPending_ShouldThrow()
    {
        var order = CreateOrder();

        Assert.Throws<InvalidStateTransitionException>(() => order.MarkPaid());
    }

    [Fact]
    public void Cancel_FromPaid_ShouldThrow()
    {
        var order = CreateOrder();
        order.MarkPaymentProcessing();
        order.MarkPaid();

        Assert.Throws<InvalidStateTransitionException>(() => order.Cancel());
    }

    [Fact]
    public void MarkPaymentProcessing_FromPaid_ShouldThrow()
    {
        var order = CreateOrder();
        order.MarkPaymentProcessing();
        order.MarkPaid();

        Assert.Throws<InvalidStateTransitionException>(() => order.MarkPaymentProcessing());
    }

    [Fact]
    public void MarkExpired_FromPaid_ShouldThrow()
    {
        var order = CreateOrder();
        order.MarkPaymentProcessing();
        order.MarkPaid();

        Assert.Throws<InvalidStateTransitionException>(() => order.MarkExpired());
    }

    [Fact]
    public void MarkFailed_FromPending_ShouldThrow()
    {
        var order = CreateOrder();

        Assert.Throws<InvalidStateTransitionException>(() => order.MarkFailed());
    }

    [Fact]
    public void AddItem_ShouldCalculateTotals()
    {
        var order = CreateOrder();
        var course = CreateCourse(price: 50m);

        order.AddItem(course, quantity: 2);

        Assert.Single(order.Items);
        Assert.Equal(100m, order.Subtotal);
        Assert.Equal(100m, order.Total);
    }

    [Fact]
    public void AddMultipleItems_ShouldCalculateTotals()
    {
        var order = CreateOrder();
        var course1 = CreateCourse(price: 30m);
        var course2 = CreateCourse(price: 20m);

        order.AddItem(course1, quantity: 1);
        order.AddItem(course2, quantity: 2);

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(70m, order.Subtotal);
        Assert.Equal(70m, order.Total);
    }
}

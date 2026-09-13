using Payment.Domain.Common;

namespace Payment.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;

    // Navigation properties
    public ICollection<Order> Orders { get; private set; } = new List<Order>();

    private User() { } // EF Core constructor

    public User(string email, string firstName, string lastName)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }
}

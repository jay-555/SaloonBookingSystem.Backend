using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class CustomerTests
{
    [Fact]
    public void Customer_normalizes_indian_phone_and_optional_email()
    {
        var customer = new Customer(Guid.NewGuid(), Guid.NewGuid(), " Priya ", "+91 98765 43210", " priya@example.com ");
        Assert.Equal("Priya", customer.Name);
        Assert.Equal("9876543210", customer.Phone);
        Assert.Equal("priya@example.com", customer.Email);
        Assert.Null(new Customer(Guid.NewGuid(), Guid.NewGuid(), "A", "9876543210", null).Email);
        Assert.Throws<ArgumentException>(() => Customer.NormalizePhone("12345"));
        Assert.Throws<ArgumentException>(() => Customer.NormalizePhone("0876543210"));
        Assert.Throws<ArgumentException>(() => Customer.NormalizeEmail("not-an-email"));
    }
}

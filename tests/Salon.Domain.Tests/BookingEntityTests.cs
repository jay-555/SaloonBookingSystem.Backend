using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class BookingEntityTests
{
    [Fact]
    public void Booking_and_leave_reject_inverted_ranges_and_empty_ids()
    {
        var start = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
        var end = start.AddHours(1);
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end);
        Assert.Equal(start, booking.StartsAtUtc);
        Assert.Null(booking.CustomerId);
        var withCustomer = new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, Guid.NewGuid());
        Assert.NotNull(withCustomer.CustomerId);
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), end, start));
        Assert.Throws<ArgumentException>(() => new Booking(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end));
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, Guid.Empty));
        Assert.Throws<ArgumentException>(() => new EmployeeLeave(Guid.NewGuid(), Guid.NewGuid(), end, start));
    }
}

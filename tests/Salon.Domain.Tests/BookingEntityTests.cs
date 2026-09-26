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
        Assert.Equal(BookingStatuses.Confirmed, booking.Status);
        Assert.Null(booking.CustomerId);
        var withCustomer = new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, Guid.NewGuid());
        Assert.NotNull(withCustomer.CustomerId);
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), end, start));
        Assert.Throws<ArgumentException>(() => new Booking(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end));
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, Guid.Empty));
        Assert.Throws<ArgumentException>(() => new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, null, BookingStatuses.Cancelled));
        Assert.Throws<ArgumentException>(() => new EmployeeLeave(Guid.NewGuid(), Guid.NewGuid(), end, start));

        var active = new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end);
        active.Reschedule(Guid.NewGuid(), Guid.NewGuid(), start.AddHours(2), end.AddHours(2));
        Assert.Equal(start.AddHours(2), active.StartsAtUtc);
        active.Cancel(DateTimeOffset.Parse("2026-09-28T12:00:00Z"));
        Assert.False(active.IsActive);
        Assert.Equal(BookingStatuses.Cancelled, active.Status);
        Assert.Throws<InvalidOperationException>(() => active.Cancel(DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => active.Reschedule(Guid.NewGuid(), Guid.NewGuid(), start, end));
    }

    [Fact]
    public void Booking_status_transitions_follow_allow_list()
    {
        var start = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
        var end = start.AddHours(1);
        var booking = new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end);
        Assert.Equal(new[] { BookingStatuses.CheckedIn, BookingStatuses.Cancelled, BookingStatuses.NoShow }, BookingStatusTransitions.Next(booking.Status));

        booking.TransitionTo(BookingStatuses.CheckedIn, DateTimeOffset.UtcNow);
        Assert.Equal(BookingStatuses.CheckedIn, booking.Status);
        Assert.Throws<InvalidOperationException>(() => booking.TransitionTo(BookingStatuses.Completed, DateTimeOffset.UtcNow));

        booking.TransitionTo(BookingStatuses.InService, DateTimeOffset.UtcNow);
        booking.TransitionTo(BookingStatuses.Completed, DateTimeOffset.UtcNow);
        Assert.Equal(BookingStatuses.Completed, booking.Status);
        Assert.True(booking.IsActive);
        Assert.Throws<InvalidOperationException>(() => booking.TransitionTo(BookingStatuses.Cancelled, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => booking.Reschedule(Guid.NewGuid(), Guid.NewGuid(), start, end));

        var noShow = new Booking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end);
        noShow.TransitionTo(BookingStatuses.NoShow, DateTimeOffset.UtcNow);
        Assert.Equal(BookingStatuses.NoShow, noShow.Status);
        Assert.True(noShow.IsActive);
        Assert.Empty(BookingStatusTransitions.Next(BookingStatuses.NoShow));
    }
}

using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class EmployeeScheduleCalendarTests
{
    [Fact]
    public void Week_starts_on_Monday_and_day_window_is_single_day()
    {
        // 2026-09-27 is Sunday; week Monday is 2026-09-21.
        Assert.Equal(new DateOnly(2026, 9, 21), EmployeeScheduleCalendar.WeekStartMonday(new DateOnly(2026, 9, 27)));
        Assert.Equal(new DateOnly(2026, 9, 28), EmployeeScheduleCalendar.WeekStartMonday(new DateOnly(2026, 9, 28)));
        var day = EmployeeScheduleCalendar.Window(EmployeeScheduleCalendar.ViewDay, new DateOnly(2026, 9, 28));
        Assert.Equal(new DateOnly(2026, 9, 28), day.From);
        Assert.Equal(new DateOnly(2026, 9, 29), day.ToExclusive);
        var week = EmployeeScheduleCalendar.Window(EmployeeScheduleCalendar.ViewWeek, new DateOnly(2026, 9, 30));
        Assert.Equal(new DateOnly(2026, 9, 28), week.From);
        Assert.Equal(new DateOnly(2026, 10, 5), week.ToExclusive);
        Assert.Throws<ArgumentException>(() => EmployeeScheduleCalendar.Window("month", new DateOnly(2026, 9, 28)));
    }

    [Fact]
    public void Assemble_includes_booking_break_and_multi_day_leave_slices()
    {
        var monday = new DateOnly(2026, 9, 28);
        var (from, to) = EmployeeScheduleCalendar.Window(EmployeeScheduleCalendar.ViewWeek, monday);
        // Haircut 10:00–11:00 IST = 04:30–05:30Z
        var bookingStart = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
        var leaveStart = DateTimeOffset.Parse("2026-09-29T18:30:00Z"); // Tue 00:00 IST
        var leaveEnd = DateTimeOffset.Parse("2026-10-01T18:30:00Z");   // Thu 00:00 IST (covers Wed)

        var items = EmployeeScheduleCalendar.Assemble(
            "Asia/Kolkata", from, to,
            [new EmployeeScheduleCalendar.BookingSlice(Guid.NewGuid(), bookingStart, bookingStart.AddHours(1), "Haircut", "Chair 1")],
            [new EmployeeScheduleCalendar.LeaveSlice(Guid.NewGuid(), leaveStart, leaveEnd)],
            [new EmployeeScheduleCalendar.BreakSlice((int)DayOfWeek.Monday, 12 * 60, 13 * 60)]);

        Assert.Contains(items, x => x is { Kind: "booking", Date: var d, StartsAtLocal: "10:00", EndsAtLocal: "11:00", Label: "Haircut" } && d == monday);
        Assert.Contains(items, x => x is { Kind: "break", Date: var d, StartsAtLocal: "12:00", EndsAtLocal: "13:00" } && d == monday);
        Assert.Contains(items, x => x.Kind == "leave" && x.Date == new DateOnly(2026, 9, 30));
        Assert.DoesNotContain(items, x => x.Kind == "leave" && x.Date == monday);
    }
}

using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class AvailabilityEngineTests
{
    private static readonly Guid Emp = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Seat = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateOnly Day = new(2026, 9, 28); // Monday

    [Fact]
    public void Overlap_helpers_detect_half_open_conflicts()
    {
        var a = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
        var b = DateTimeOffset.Parse("2026-09-28T05:30:00Z");
        Assert.True(AvailabilityEngine.Overlaps(a, b, DateTimeOffset.Parse("2026-09-28T05:00:00Z"), DateTimeOffset.Parse("2026-09-28T06:00:00Z")));
        Assert.False(AvailabilityEngine.Overlaps(a, b, b, DateTimeOffset.Parse("2026-09-28T06:30:00Z")));
        Assert.True(AvailabilityEngine.OverlapsMinutes(600, 660, 630, 690));
        Assert.False(AvailabilityEngine.OverlapsMinutes(600, 660, 660, 720));
    }

    [Fact]
    public void Section12_success_returns_open_start_when_employee_and_seat_free()
    {
        // Asia/Kolkata Monday 09:00–18:00; Haircut 60m + 0 buffer; no bookings.
        var slots = AvailabilityEngine.FindSlots(BaseRequest());
        Assert.Contains(slots, s => s.StartsAtLocal == "09:00" && s.EmployeeId == Emp && s.SeatId == Seat);
        Assert.Contains(slots, s => s.StartsAtLocal == "10:00");
    }

    [Fact]
    public void Section12_employee_conflict_omits_overlapping_start()
    {
        // Existing booking 10:00–11:00 IST (04:30–05:30Z) blocks 10:00 start for 60m service.
        var bookingStart = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
        var request = BaseRequest() with
        {
            Employees =
            [
                new AvailabilityEngine.EmployeeDay(Emp,
                    new AvailabilityEngine.DayInterval(9 * 60, 18 * 60),
                    [],
                    [],
                    [new AvailabilityEngine.BusyInterval(bookingStart, bookingStart.AddHours(1))])
            ]
        };
        var slots = AvailabilityEngine.FindSlots(request);
        Assert.DoesNotContain(slots, s => s.StartsAtLocal == "10:00");
        Assert.Contains(slots, s => s.StartsAtLocal == "09:00");
        Assert.Contains(slots, s => s.StartsAtLocal == "11:00");
    }

    [Fact]
    public void Section12_seat_conflict_omits_start_when_only_seat_busy()
    {
        var bookingStart = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
        var request = BaseRequest() with
        {
            Seats =
            [
                new AvailabilityEngine.SeatDay(Seat,
                    [new AvailabilityEngine.BusyInterval(bookingStart, bookingStart.AddHours(1))])
            ]
        };
        var slots = AvailabilityEngine.FindSlots(request);
        Assert.DoesNotContain(slots, s => s.StartsAtLocal == "10:00");
        Assert.Contains(slots, s => s.StartsAtLocal == "09:00");
    }

    [Fact]
    public void Break_and_leave_exclude_overlapping_starts()
    {
        var leaveStart = DateTimeOffset.Parse("2026-09-28T03:30:00Z"); // 09:00 IST
        var withBreak = BaseRequest() with
        {
            Employees =
            [
                new AvailabilityEngine.EmployeeDay(Emp,
                    new AvailabilityEngine.DayInterval(9 * 60, 18 * 60),
                    [new AvailabilityEngine.BreakInterval(12 * 60, 13 * 60)],
                    [new AvailabilityEngine.BusyInterval(leaveStart, leaveStart.AddMinutes(60))],
                    [])
            ]
        };
        var slots = AvailabilityEngine.FindSlots(withBreak);
        Assert.DoesNotContain(slots, s => s.StartsAtLocal == "09:00");
        Assert.DoesNotContain(slots, s => s.StartsAtLocal == "12:00");
        Assert.Contains(slots, s => s.StartsAtLocal == "11:00");
    }

    private static AvailabilityEngine.Request BaseRequest() => new(
        Day,
        "Asia/Kolkata",
        DurationMinutes: 60,
        BufferMinutes: 0,
        Employees:
        [
            new AvailabilityEngine.EmployeeDay(Emp,
                new AvailabilityEngine.DayInterval(9 * 60, 18 * 60),
                [],
                [],
                [])
        ],
        Seats: [new AvailabilityEngine.SeatDay(Seat, [])]);
}

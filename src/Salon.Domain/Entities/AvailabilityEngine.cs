namespace Salon.Domain.Entities;

/// <summary>Pure single-service availability: employee + seat + duration/buffer must all be free.</summary>
public static class AvailabilityEngine
{
    public const int SlotStepMinutes = 15;

    public sealed record BusyInterval(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
    public sealed record DayInterval(int OpensAt, int ClosesAt);
    public sealed record BreakInterval(int StartsAt, int EndsAt);
    public sealed record EmployeeDay(
        Guid EmployeeId,
        DayInterval? Hours,
        IReadOnlyList<BreakInterval> Breaks,
        IReadOnlyList<BusyInterval> Leave,
        IReadOnlyList<BusyInterval> Bookings);
    public sealed record SeatDay(Guid SeatId, IReadOnlyList<BusyInterval> Bookings);
    public sealed record Request(
        DateOnly LocalDate,
        string TimeZoneId,
        int DurationMinutes,
        int BufferMinutes,
        IReadOnlyList<EmployeeDay> Employees,
        IReadOnlyList<SeatDay> Seats);

    public sealed record Slot(string StartsAtLocal, DateTimeOffset StartsAtUtc, Guid EmployeeId, Guid SeatId);

    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && bStart < aEnd;

    public static bool OverlapsMinutes(int aStart, int aEnd, int bStart, int bEnd) =>
        aStart < bEnd && bStart < aEnd;

    public static IReadOnlyList<Slot> FindSlots(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DurationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Duration must be positive.");
        if (request.BufferMinutes < 0) throw new ArgumentOutOfRangeException(nameof(request), "Buffer must be non-negative.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
        var reservedMinutes = request.DurationMinutes + request.BufferMinutes;
        var slots = new List<Slot>();

        foreach (var employee in request.Employees)
        {
            if (employee.Hours is null) continue;
            var open = employee.Hours;
            for (var start = open.OpensAt; start + reservedMinutes <= open.ClosesAt; start += SlotStepMinutes)
            {
                if (employee.Breaks.Any(b => OverlapsMinutes(start, start + reservedMinutes, b.StartsAt, b.EndsAt)))
                    continue;

                var localStart = new DateTime(request.LocalDate.Year, request.LocalDate.Month, request.LocalDate.Day,
                    start / 60, start % 60, 0, DateTimeKind.Unspecified);
                var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone));
                var endUtc = startUtc.AddMinutes(reservedMinutes);

                if (employee.Leave.Any(x => Overlaps(startUtc, endUtc, x.StartsAtUtc, x.EndsAtUtc)))
                    continue;
                if (employee.Bookings.Any(x => Overlaps(startUtc, endUtc, x.StartsAtUtc, x.EndsAtUtc)))
                    continue;

                var seat = request.Seats.FirstOrDefault(s =>
                    !s.Bookings.Any(x => Overlaps(startUtc, endUtc, x.StartsAtUtc, x.EndsAtUtc)));
                if (seat is null) continue;

                slots.Add(new Slot($"{start / 60:D2}:{start % 60:D2}", startUtc, employee.EmployeeId, seat.SeatId));
            }
        }

        return slots
            .GroupBy(x => x.StartsAtLocal)
            .Select(g => g.First())
            .OrderBy(x => x.StartsAtLocal)
            .ToArray();
    }
}

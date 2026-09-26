namespace Salon.Domain.Entities;

public static class EmployeeScheduleCalendar
{
    public const string ViewDay = "day";
    public const string ViewWeek = "week";

    /// <summary>Monday-start week used for India MVP calendars.</summary>
    public static DateOnly WeekStartMonday(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    public static (DateOnly From, DateOnly ToExclusive) Window(string view, DateOnly anchor)
    {
        if (view is not (ViewDay or ViewWeek))
            throw new ArgumentException("View must be day or week.", nameof(view));
        if (view == ViewDay) return (anchor, anchor.AddDays(1));
        var start = WeekStartMonday(anchor);
        return (start, start.AddDays(7));
    }

    public sealed record BookingSlice(Guid Id, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string ServiceName, string? SeatName, string? Status = null);
    public sealed record LeaveSlice(Guid Id, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
    public sealed record BreakSlice(int DayOfWeek, int StartsAtMinutes, int EndsAtMinutes);
    public sealed record TimelineItem(
        string Kind,
        DateOnly Date,
        string StartsAtLocal,
        string EndsAtLocal,
        string Label,
        Guid? SourceId,
        string? SeatName,
        string? Status = null);

    public static IReadOnlyList<TimelineItem> Assemble(
        string timeZoneId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        IReadOnlyList<BookingSlice> bookings,
        IReadOnlyList<LeaveSlice> leaves,
        IReadOnlyList<BreakSlice> breaks)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var items = new List<TimelineItem>();
        for (var day = fromInclusive; day < toExclusive; day = day.AddDays(1))
        {
            var dayStartLocal = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var dayEndLocal = dayStartLocal.AddDays(1);
            var dayStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, zone));
            var dayEndUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, zone));

            foreach (var booking in bookings.Where(x => x.StartsAtUtc < dayEndUtc && x.EndsAtUtc > dayStartUtc))
            {
                var (start, end) = ClipLocal(booking.StartsAtUtc, booking.EndsAtUtc, dayStartUtc, dayEndUtc, zone);
                items.Add(new TimelineItem("booking", day, Format(start), Format(end), booking.ServiceName, booking.Id, booking.SeatName, booking.Status));
            }

            foreach (var leave in leaves.Where(x => x.StartsAtUtc < dayEndUtc && x.EndsAtUtc > dayStartUtc))
            {
                var (start, end) = ClipLocal(leave.StartsAtUtc, leave.EndsAtUtc, dayStartUtc, dayEndUtc, zone);
                items.Add(new TimelineItem("leave", day, Format(start), Format(end), "Leave", leave.Id, null));
            }

            var dow = (int)day.DayOfWeek;
            foreach (var br in breaks.Where(x => x.DayOfWeek == dow))
                items.Add(new TimelineItem("break", day, FormatMinutes(br.StartsAtMinutes), FormatMinutes(br.EndsAtMinutes), "Break", null, null));
        }

        return items
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartsAtLocal)
            .ThenBy(x => x.Kind)
            .ToArray();
    }

    private static (TimeOnly Start, TimeOnly End) ClipLocal(
        DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc,
        DateTimeOffset dayStartUtc, DateTimeOffset dayEndUtc, TimeZoneInfo zone)
    {
        var clippedStart = startsAtUtc > dayStartUtc ? startsAtUtc : dayStartUtc;
        var clippedEnd = endsAtUtc < dayEndUtc ? endsAtUtc : dayEndUtc;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(clippedStart.UtcDateTime, zone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(clippedEnd.UtcDateTime, zone);
        return (TimeOnly.FromDateTime(localStart), TimeOnly.FromDateTime(localEnd));
    }

    private static string Format(TimeOnly time) => $"{time.Hour:D2}:{time.Minute:D2}";
    private static string FormatMinutes(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";
}

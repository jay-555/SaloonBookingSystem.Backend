namespace Salon.Application;

public sealed record SeatScheduleResult(
    Guid SeatId,
    string SeatName,
    string SeatType,
    string TimeZoneId,
    string View,
    DateOnly AnchorDate,
    DateOnly RangeStart,
    DateOnly RangeEndExclusive,
    IReadOnlyList<ScheduleItem> Items);

public interface ISeatSchedule
{
    Task<SeatScheduleResult> Query(Guid userId, Guid seatId, DateOnly date, string view, CancellationToken cancellationToken);
}

namespace Salon.Application;

public sealed record ScheduleItem(
    string Kind,
    DateOnly Date,
    string StartsAtLocal,
    string EndsAtLocal,
    string Label,
    Guid? SourceId,
    string? SeatName);

public sealed record EmployeeScheduleResult(
    Guid EmployeeId,
    string EmployeeName,
    string TimeZoneId,
    string View,
    DateOnly AnchorDate,
    DateOnly RangeStart,
    DateOnly RangeEndExclusive,
    IReadOnlyList<ScheduleItem> Items);

public interface IEmployeeSchedule
{
    Task<EmployeeScheduleResult> Query(Guid userId, Guid employeeId, DateOnly date, string view, CancellationToken cancellationToken);
}

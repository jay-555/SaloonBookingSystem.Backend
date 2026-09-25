using Salon.Domain.Entities;

namespace Salon.Application;

public sealed record AvailabilityQuery(Guid ServiceId, DateOnly Date, Guid? EmployeeId);
public sealed record AvailableSlot(string StartsAtLocal, DateTimeOffset StartsAtUtc, Guid EmployeeId, Guid SeatId);
public sealed record AvailabilityResult(Guid ServiceId, DateOnly Date, string TimeZoneId, IReadOnlyList<AvailableSlot> Slots);

public interface IAvailabilityService
{
    Task<AvailabilityResult> Query(Guid userId, AvailabilityQuery query, CancellationToken cancellationToken);
}

namespace Salon.Application;

public sealed class SlotUnavailableException() : Exception("That time is no longer available. Please choose another slot.");

public sealed record PublicSalonInfo(Guid Id, string Name, string TimeZoneId);
public sealed record PublicServiceSummary(Guid Id, string Name, string Category, decimal Price, int DurationMinutes);
public sealed record PublicEmployeeSummary(Guid Id, string Name);
public sealed record CreatePublicBookingInput(
    Guid ServiceId,
    DateOnly Date,
    string StartsAtLocal,
    Guid? EmployeeId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail);
public sealed record PublicBookingConfirmation(
    Guid BookingId,
    string SalonName,
    string ServiceName,
    string StartsAtLocal,
    DateOnly Date,
    string CustomerName);

public interface IPublicBooking
{
    Task<PublicSalonInfo> Salon(CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicServiceSummary>> Services(CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicEmployeeSummary>> Employees(Guid serviceId, CancellationToken cancellationToken);
    Task<AvailabilityResult> Availability(AvailabilityQuery query, CancellationToken cancellationToken);
    Task<PublicBookingConfirmation> Create(CreatePublicBookingInput input, CancellationToken cancellationToken);
}

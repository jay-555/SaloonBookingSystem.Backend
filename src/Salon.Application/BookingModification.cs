namespace Salon.Application;

public sealed record RescheduleBookingInput(DateOnly Date, string StartsAtLocal, Guid? EmployeeId);

public sealed record StaffBookingDetail(
    Guid BookingId,
    Guid ServiceId,
    string ServiceName,
    Guid EmployeeId,
    string EmployeeName,
    Guid SeatId,
    string SeatName,
    string StartsAtLocal,
    string EndsAtLocal,
    DateOnly Date,
    string TimeZoneId,
    string? CustomerName,
    bool Cancelled);

public interface IBookingModification
{
    Task<StaffBookingDetail> Get(Guid userId, Guid bookingId, CancellationToken cancellationToken);
    Task<StaffBookingDetail> Reschedule(Guid userId, Guid bookingId, RescheduleBookingInput input, CancellationToken cancellationToken);
    Task Cancel(Guid userId, Guid bookingId, CancellationToken cancellationToken);
}

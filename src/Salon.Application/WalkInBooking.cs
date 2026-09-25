namespace Salon.Application;

// Reuse Phase 8 contact/create shapes for staff walk-ins (same engine, same confirmation).
public interface IWalkInBooking
{
    Task<IReadOnlyList<PublicEmployeeSummary>> Employees(Guid userId, Guid serviceId, CancellationToken cancellationToken);
    Task<PublicBookingConfirmation> Create(Guid userId, CreatePublicBookingInput input, CancellationToken cancellationToken);
}

namespace Salon.Domain.Entities;

public sealed class Booking
{
    private Booking() { }

    public Booking(Guid id, Guid salonId, Guid serviceId, Guid employeeId, Guid seatId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, Guid? customerId = null, string status = BookingStatuses.Confirmed)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        ServiceId = DomainGuard.Id(serviceId, nameof(serviceId));
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
        if (customerId is Guid customer) CustomerId = DomainGuard.Id(customer, nameof(customerId));
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Booking end must be after start.", nameof(endsAtUtc));
        StartsAtUtc = startsAtUtc.ToUniversalTime();
        EndsAtUtc = endsAtUtc.ToUniversalTime();
        Status = DomainGuard.Text(status, nameof(status));
        if (!BookingStatuses.IsKnown(Status) || Status == BookingStatuses.Cancelled)
            throw new ArgumentException("Create bookings with an active lifecycle status.", nameof(status));
    }

    public Guid Id { get; private set; }
    public Guid SalonId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid SeatId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public string Status { get; private set; } = BookingStatuses.Confirmed;
    public bool IsActive => CancelledAtUtc is null;

    public void Reschedule(Guid employeeId, Guid seatId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        if (CancelledAtUtc is not null || Status == BookingStatuses.Cancelled)
            throw new InvalidOperationException("Cancelled booking cannot be rescheduled.");
        if (BookingStatuses.IsTerminal(Status))
            throw new InvalidOperationException("This booking cannot be rescheduled.");
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Booking end must be after start.", nameof(endsAtUtc));
        StartsAtUtc = startsAtUtc.ToUniversalTime();
        EndsAtUtc = endsAtUtc.ToUniversalTime();
    }

    public void Cancel(DateTimeOffset atUtc) => TransitionTo(BookingStatuses.Cancelled, atUtc);

    public void TransitionTo(string target, DateTimeOffset atUtc)
    {
        var next = DomainGuard.Text(target, nameof(target));
        if (!BookingStatusTransitions.CanTransition(Status, next))
            throw new InvalidOperationException($"Cannot change status from {Status} to {next}.");
        Status = next;
        if (next == BookingStatuses.Cancelled)
            CancelledAtUtc = atUtc.ToUniversalTime();
    }
}

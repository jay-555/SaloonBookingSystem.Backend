namespace Salon.Domain.Entities;

public sealed class Booking
{
    private Booking() { }

    public Booking(Guid id, Guid salonId, Guid serviceId, Guid employeeId, Guid seatId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, Guid? customerId = null)
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
    public bool IsActive => CancelledAtUtc is null;

    public void Reschedule(Guid employeeId, Guid seatId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        if (CancelledAtUtc is not null)
            throw new InvalidOperationException("Cancelled booking cannot be rescheduled.");
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Booking end must be after start.", nameof(endsAtUtc));
        StartsAtUtc = startsAtUtc.ToUniversalTime();
        EndsAtUtc = endsAtUtc.ToUniversalTime();
    }

    public void Cancel(DateTimeOffset atUtc)
    {
        if (CancelledAtUtc is not null)
            throw new InvalidOperationException("Booking is already cancelled.");
        CancelledAtUtc = atUtc.ToUniversalTime();
    }
}

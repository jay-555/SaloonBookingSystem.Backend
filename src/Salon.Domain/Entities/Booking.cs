namespace Salon.Domain.Entities;

public sealed class Booking
{
    private Booking() { }

    public Booking(Guid id, Guid salonId, Guid serviceId, Guid employeeId, Guid seatId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        ServiceId = DomainGuard.Id(serviceId, nameof(serviceId));
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
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
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
}

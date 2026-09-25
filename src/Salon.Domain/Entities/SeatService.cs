namespace Salon.Domain.Entities;

public sealed class SeatService
{
    private SeatService() { }

    public SeatService(Guid seatId, Guid serviceId)
    {
        SeatId = DomainGuard.Id(seatId, nameof(seatId));
        ServiceId = DomainGuard.Id(serviceId, nameof(serviceId));
    }

    public Guid SeatId { get; private set; }
    public Guid ServiceId { get; private set; }
}

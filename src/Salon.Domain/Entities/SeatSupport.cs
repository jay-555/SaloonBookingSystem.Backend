namespace Salon.Domain.Entities;

public static class SeatSupport
{
    public static SeatService[] Services(Guid seatId, Guid salonId, IReadOnlyCollection<Guid> serviceIds, IReadOnlyDictionary<Guid, Guid> serviceSalonById)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        ArgumentNullException.ThrowIfNull(serviceSalonById);
        DomainGuard.Id(seatId, nameof(seatId));
        DomainGuard.Id(salonId, nameof(salonId));
        if (serviceIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Service identifiers must not be empty.", nameof(serviceIds));
        if (serviceIds.Distinct().Count() != serviceIds.Count)
            throw new ArgumentException("Supported services must be unique.", nameof(serviceIds));
        foreach (var serviceId in serviceIds)
        {
            if (!serviceSalonById.TryGetValue(serviceId, out var owner) || owner != salonId)
                throw new ArgumentException("Supported services must reference services from the same salon.", nameof(serviceIds));
        }
        return serviceIds.Select(id => new SeatService(seatId, id)).ToArray();
    }
}

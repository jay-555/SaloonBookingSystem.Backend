using Salon.Domain.Entities;

namespace Salon.Application;

public sealed record SeatInput(string Name, string Type, Guid[] ServiceIds)
{
    public SeatService[] Validate(Guid seatId, Guid salonId, IReadOnlyDictionary<Guid, Guid> serviceSalonById)
    {
        _ = new Seat(seatId, salonId, Name, Type);
        if (ServiceIds is null) throw new ArgumentException("Supply a supported service list.", nameof(ServiceIds));
        return SeatSupport.Services(seatId, salonId, ServiceIds, serviceSalonById);
    }
}

public sealed record SeatSummary(Guid Id, string Name, string Type);
public sealed record SeatDetail(Guid Id, string Name, string Type, Guid[] ServiceIds);

public interface ISeatDirectory
{
    Task<IReadOnlyList<SeatSummary>> List(Guid userId, CancellationToken cancellationToken);
    Task<SeatDetail?> Get(Guid userId, Guid seatId, CancellationToken cancellationToken);
    Task<SeatDetail> Create(Guid userId, SeatInput input, CancellationToken cancellationToken);
    Task<SeatDetail> Update(Guid userId, Guid seatId, SeatInput input, CancellationToken cancellationToken);
    Task Delete(Guid userId, Guid seatId, CancellationToken cancellationToken);
}

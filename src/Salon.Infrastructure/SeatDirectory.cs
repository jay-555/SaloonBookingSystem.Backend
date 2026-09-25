using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class SeatDirectory(SalonDbContext database) : ISeatDirectory
{
    public async Task<IReadOnlyList<SeatSummary>> List(Guid userId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await database.Seats.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .OrderBy(x => x.Name)
            .Select(x => new SeatSummary(x.Id, x.Name, x.Type))
            .ToListAsync(cancellationToken);
    }

    public async Task<SeatDetail?> Get(Guid userId, Guid seatId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await Read(salonId, seatId, cancellationToken);
    }

    public async Task<SeatDetail> Create(Guid userId, SeatInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var seatId = Guid.NewGuid();
        var services = await ServiceMap(salonId, cancellationToken);
        var links = input.Validate(seatId, salonId, services);
        database.Seats.Add(new Seat(seatId, salonId, input.Name, input.Type));
        database.SeatServices.AddRange(links);
        await database.SaveChangesAsync(cancellationToken);
        return (await Read(salonId, seatId, cancellationToken))!;
    }

    public async Task<SeatDetail> Update(Guid userId, Guid seatId, SeatInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var seat = await database.Seats
            .FromSqlInterpolated($"SELECT * FROM \"Seats\" WHERE \"Id\" = {seatId} AND \"SalonId\" = {salonId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException();
        var services = await ServiceMap(salonId, cancellationToken);
        var links = input.Validate(seat.Id, salonId, services);
        seat.UpdateProfile(input.Name, input.Type);
        await database.SeatServices.Where(x => x.SeatId == seat.Id).ExecuteDeleteAsync(cancellationToken);
        database.SeatServices.AddRange(links);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await Read(salonId, seat.Id, cancellationToken))!;
    }

    public async Task Delete(Guid userId, Guid seatId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var deleted = await database.Seats.Where(x => x.Id == seatId && x.SalonId == salonId).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0) throw new KeyNotFoundException();
    }

    private async Task<Guid> RequireSalon(Guid userId, bool write, CancellationToken cancellationToken)
    {
        var account = await database.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Role, x.SalonId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (account.SalonId is not Guid salonId) throw new UnauthorizedAccessException();
        if (write && account.Role != SalonRoles.OwnerAdmin) throw new UnauthorizedAccessException();
        if (!write && account.Role == "Employee") throw new UnauthorizedAccessException();
        if (!write && account.Role is not (SalonRoles.OwnerAdmin or "Manager"))
            throw new UnauthorizedAccessException();
        return salonId;
    }

    private async Task<Dictionary<Guid, Guid>> ServiceMap(Guid salonId, CancellationToken cancellationToken) =>
        await database.Services.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .ToDictionaryAsync(x => x.Id, x => x.SalonId, cancellationToken);

    private async Task<SeatDetail?> Read(Guid salonId, Guid seatId, CancellationToken cancellationToken)
    {
        var seat = await database.Seats.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == seatId && x.SalonId == salonId, cancellationToken);
        if (seat is null) return null;
        var services = await database.SeatServices.AsNoTracking()
            .Where(x => x.SeatId == seatId).Select(x => x.ServiceId).ToArrayAsync(cancellationToken);
        return new SeatDetail(seat.Id, seat.Name, seat.Type, services);
    }
}

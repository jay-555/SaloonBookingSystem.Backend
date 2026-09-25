using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class ServiceCatalog(SalonDbContext database) : IServiceCatalog
{
    public async Task<IReadOnlyList<ServiceSummary>> List(Guid userId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await database.Services.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .OrderBy(x => x.Name)
            .Select(x => new ServiceSummary(x.Id, x.Name, x.Category, x.Price, x.DurationMinutes))
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceDetail?> Get(Guid userId, Guid serviceId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await Read(salonId, serviceId, cancellationToken);
    }

    public async Task<ServiceDetail> Create(Guid userId, ServiceInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var serviceId = Guid.NewGuid();
        input.Validate(serviceId, salonId);
        database.Services.Add(new Service(serviceId, salonId, input.Name, input.Category, input.Price, input.DurationMinutes));
        await database.SaveChangesAsync(cancellationToken);
        return (await Read(salonId, serviceId, cancellationToken))!;
    }

    public async Task<ServiceDetail> Update(Guid userId, Guid serviceId, ServiceInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var service = await database.Services
            .SingleOrDefaultAsync(x => x.Id == serviceId && x.SalonId == salonId, cancellationToken)
            ?? throw new KeyNotFoundException();
        input.Validate(service.Id, salonId);
        service.UpdateProfile(input.Name, input.Category, input.Price, input.DurationMinutes);
        await database.SaveChangesAsync(cancellationToken);
        return (await Read(salonId, service.Id, cancellationToken))!;
    }

    public async Task Delete(Guid userId, Guid serviceId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var exists = await database.Services.AnyAsync(x => x.Id == serviceId && x.SalonId == salonId, cancellationToken);
        if (!exists) throw new KeyNotFoundException();
        var linked = await database.EmployeeSkills.AnyAsync(x => x.ServiceId == serviceId, cancellationToken)
            || await database.SeatServices.AnyAsync(x => x.ServiceId == serviceId, cancellationToken);
        if (linked)
            throw new InvalidOperationException("Remove this service from employee skills and seat support lists before deleting it.");
        var deleted = await database.Services.Where(x => x.Id == serviceId && x.SalonId == salonId).ExecuteDeleteAsync(cancellationToken);
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

    private async Task<ServiceDetail?> Read(Guid salonId, Guid serviceId, CancellationToken cancellationToken)
    {
        var service = await database.Services.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == serviceId && x.SalonId == salonId, cancellationToken);
        return service is null
            ? null
            : new ServiceDetail(service.Id, service.Name, service.Category, service.Price, service.DurationMinutes);
    }
}

using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class EmployeeDirectory(SalonDbContext database) : IEmployeeDirectory
{
    public async Task<IReadOnlyList<EmployeeSummary>> List(Guid userId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await database.Employees.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .OrderBy(x => x.Name)
            .Select(x => new EmployeeSummary(x.Id, x.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<EmployeeDetail?> Get(Guid userId, Guid employeeId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await Read(salonId, employeeId, cancellationToken);
    }

    public async Task<EmployeeDetail> Create(Guid userId, EmployeeInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var employeeId = Guid.NewGuid();
        var services = await ServiceMap(salonId, cancellationToken);
        var (days, breaks, skills) = input.Validate(employeeId, salonId, services);
        database.Employees.Add(new Employee(employeeId, salonId, input.Name));
        database.EmployeeWorkingDays.AddRange(days);
        database.EmployeeBreaks.AddRange(breaks);
        database.EmployeeSkills.AddRange(skills);
        await database.SaveChangesAsync(cancellationToken);
        return (await Read(salonId, employeeId, cancellationToken))!;
    }

    public async Task<EmployeeDetail> Update(Guid userId, Guid employeeId, EmployeeInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var employee = await database.Employees
            .FromSqlInterpolated($"SELECT * FROM \"Employees\" WHERE \"Id\" = {employeeId} AND \"SalonId\" = {salonId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException();
        var services = await ServiceMap(salonId, cancellationToken);
        var (days, breaks, skills) = input.Validate(employee.Id, salonId, services);
        employee.Rename(input.Name);
        await database.EmployeeWorkingDays.Where(x => x.EmployeeId == employee.Id).ExecuteDeleteAsync(cancellationToken);
        await database.EmployeeBreaks.Where(x => x.EmployeeId == employee.Id).ExecuteDeleteAsync(cancellationToken);
        await database.EmployeeSkills.Where(x => x.EmployeeId == employee.Id).ExecuteDeleteAsync(cancellationToken);
        database.EmployeeWorkingDays.AddRange(days);
        database.EmployeeBreaks.AddRange(breaks);
        database.EmployeeSkills.AddRange(skills);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await Read(salonId, employee.Id, cancellationToken))!;
    }

    public async Task Delete(Guid userId, Guid employeeId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: true, cancellationToken);
        var deleted = await database.Employees.Where(x => x.Id == employeeId && x.SalonId == salonId).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0) throw new KeyNotFoundException();
    }

    public async Task<IReadOnlyList<ServiceSummary>> Services(Guid userId, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, write: false, cancellationToken);
        return await database.Services.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .OrderBy(x => x.Name)
            .Select(x => new ServiceSummary(x.Id, x.Name, x.Category))
            .ToListAsync(cancellationToken);
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
        if (!write && account.Role is not (SalonRoles.OwnerAdmin or "Manager" or "Employee"))
            throw new UnauthorizedAccessException();
        // Spec: Employee Identity role has no employee-management write; for reads,
        // Manager and OwnerAdmin can list. Employee role: "no employee-management write"
        // — list/read for Employee role is not required; Manager can read. Deny Employee
        // role from employee directory entirely to keep calendar self-service later.
        if (!write && account.Role == "Employee") throw new UnauthorizedAccessException();
        return salonId;
    }

    private async Task<Dictionary<Guid, Guid>> ServiceMap(Guid salonId, CancellationToken cancellationToken) =>
        await database.Services.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .ToDictionaryAsync(x => x.Id, x => x.SalonId, cancellationToken);

    private async Task<EmployeeDetail?> Read(Guid salonId, Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await database.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == employeeId && x.SalonId == salonId, cancellationToken);
        if (employee is null) return null;
        var days = await database.EmployeeWorkingDays.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).OrderBy(x => x.Day).ToArrayAsync(cancellationToken);
        var breaks = await database.EmployeeBreaks.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).ToArrayAsync(cancellationToken);
        var skills = await database.EmployeeSkills.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).Select(x => x.ServiceId).ToArrayAsync(cancellationToken);
        return new EmployeeDetail(employee.Id, employee.Name, skills,
            days.Select(day => new EmployeeDayInput(
                day.Day,
                Format(day.OpensAt),
                Format(day.ClosesAt),
                breaks.Where(item => item.Day == day.Day)
                    .OrderBy(item => item.StartsAt)
                    .Select(item => new BreakInput(item.Day, Format(item.StartsAt)!, Format(item.EndsAt)!))
                    .ToArray())).ToArray());
    }

    private static string? Format(int? minutes) => minutes is int value ? $"{value / 60:00}:{value % 60:00}" : null;
}

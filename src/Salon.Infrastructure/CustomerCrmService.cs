using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class CustomerCrmService(SalonDbContext database) : ICustomerCrm
{
    private const int SearchLimit = 50;
    private const int MinQueryLength = 2;

    public async Task<IReadOnlyList<CustomerSearchItem>> Search(Guid userId, string? query, CancellationToken cancellationToken)
    {
        var salonId = await RequireStaffSalon(userId, cancellationToken);
        var trimmed = (query ?? "").Trim();
        if (trimmed.Length < MinQueryLength)
            return [];

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        var phoneNeedle = digits.Length >= MinQueryLength ? digits : null;
        var nameNeedle = trimmed.ToLowerInvariant();

        var rows = await database.Customers.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .Where(x =>
                x.Name.ToLower().Contains(nameNeedle) ||
                (phoneNeedle != null && x.Phone.Contains(phoneNeedle)))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Phone)
            .Take(SearchLimit)
            .Select(x => new CustomerSearchItem(x.Id, x.Name, x.Phone, x.Email))
            .ToListAsync(cancellationToken);
        return rows;
    }

    public async Task<CustomerProfile> Get(Guid userId, Guid customerId, CancellationToken cancellationToken)
    {
        var salonId = await RequireStaffSalon(userId, cancellationToken);
        var customer = await database.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == customerId && x.SalonId == salonId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer was not found.");

        var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var nowUtc = DateTimeOffset.UtcNow;

        var bookings = await (
            from booking in database.Bookings.AsNoTracking()
            join service in database.Services.AsNoTracking() on booking.ServiceId equals service.Id
            join employee in database.Employees.AsNoTracking() on booking.EmployeeId equals employee.Id
            where booking.SalonId == salonId && booking.CustomerId == customerId
            orderby booking.StartsAtUtc descending
            select new
            {
                booking.Id,
                booking.ServiceId,
                ServiceName = service.Name,
                ServicePrice = service.Price,
                booking.EmployeeId,
                EmployeeName = employee.Name,
                booking.StartsAtUtc,
                booking.EndsAtUtc,
                booking.Status,
            }
        ).ToListAsync(cancellationToken);

        var history = bookings.Select(x => ToHistory(x.Id, x.ServiceId, x.ServiceName, x.EmployeeId, x.EmployeeName,
            x.StartsAtUtc, x.EndsAtUtc, x.Status, zone)).ToArray();

        var counted = bookings.Where(x => x.Status != BookingStatuses.Cancelled).ToArray();
        var visitCount = counted.Length;
        var totalSpend = counted.Sum(x => x.ServicePrice);

        var last = counted
            .Where(x => x.StartsAtUtc <= nowUtc)
            .OrderByDescending(x => x.StartsAtUtc)
            .FirstOrDefault();
        CustomerVisitSummary? lastVisit = last is null
            ? null
            : new CustomerVisitSummary(
                last.Id,
                last.ServiceName,
                FormatLocal(last.StartsAtUtc, zone),
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(last.StartsAtUtc, zone).DateTime));

        var upcomingRow = counted
            .Where(x => x.StartsAtUtc > nowUtc)
            .OrderBy(x => x.StartsAtUtc)
            .FirstOrDefault();
        CustomerHistoryItem? upcoming = upcomingRow is null
            ? null
            : ToHistory(upcomingRow.Id, upcomingRow.ServiceId, upcomingRow.ServiceName, upcomingRow.EmployeeId,
                upcomingRow.EmployeeName, upcomingRow.StartsAtUtc, upcomingRow.EndsAtUtc, upcomingRow.Status, zone);

        var serviceSummary = counted
            .GroupBy(x => new { x.ServiceId, x.ServiceName })
            .Select(g => new CustomerServiceSummary(g.Key.ServiceId, g.Key.ServiceName, g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.ServiceName)
            .ToArray();

        return new CustomerProfile(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Email,
            visitCount,
            totalSpend,
            lastVisit,
            upcoming,
            serviceSummary,
            history);
    }

    private async Task<Guid> RequireStaffSalon(Guid userId, CancellationToken cancellationToken)
    {
        var account = await database.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Role, x.SalonId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (account.SalonId is not Guid salonId) throw new UnauthorizedAccessException();
        if (account.Role is not (SalonRoles.OwnerAdmin or "Manager"))
            throw new UnauthorizedAccessException();
        return salonId;
    }

    private static CustomerHistoryItem ToHistory(
        Guid bookingId, Guid serviceId, string serviceName, Guid employeeId, string employeeName,
        DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, string status, TimeZoneInfo zone)
    {
        var startLocal = TimeZoneInfo.ConvertTime(startsAtUtc, zone);
        var endLocal = TimeZoneInfo.ConvertTime(endsAtUtc, zone);
        return new CustomerHistoryItem(
            bookingId,
            serviceId,
            serviceName,
            employeeId,
            employeeName,
            FormatLocal(startsAtUtc, zone),
            $"{endLocal.Hour:D2}:{endLocal.Minute:D2}",
            DateOnly.FromDateTime(startLocal.DateTime),
            status);
    }

    private static string FormatLocal(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        return $"{local.Hour:D2}:{local.Minute:D2}";
    }
}

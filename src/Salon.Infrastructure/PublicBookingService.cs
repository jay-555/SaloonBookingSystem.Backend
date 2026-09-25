using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class PublicBookingService(SalonDbContext database, IAvailabilityService availability, IConfiguration configuration) : IPublicBooking
{
    public async Task<PublicSalonInfo> Salon(CancellationToken cancellationToken)
    {
        var salonId = await ResolvePublicSalonId(cancellationToken);
        var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);
        return new PublicSalonInfo(salon.Id, salon.Name, salon.TimeZoneId);
    }

    public async Task<IReadOnlyList<PublicServiceSummary>> Services(CancellationToken cancellationToken)
    {
        var salonId = await ResolvePublicSalonId(cancellationToken);
        var services = await database.Services.AsNoTracking()
            .Where(x => x.SalonId == salonId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var bookable = new List<PublicServiceSummary>();
        foreach (var service in services)
        {
            if (await IsBookable(salonId, service.Id, cancellationToken))
                bookable.Add(new PublicServiceSummary(service.Id, service.Name, service.Category, service.Price, service.DurationMinutes));
        }
        return bookable;
    }

    public async Task<IReadOnlyList<PublicEmployeeSummary>> Employees(Guid serviceId, CancellationToken cancellationToken)
    {
        var salonId = await ResolvePublicSalonId(cancellationToken);
        if (!await IsBookable(salonId, serviceId, cancellationToken))
            throw new KeyNotFoundException("Service was not found.");
        return await (
            from skill in database.EmployeeSkills.AsNoTracking()
            join employee in database.Employees.AsNoTracking() on skill.EmployeeId equals employee.Id
            where skill.ServiceId == serviceId && employee.SalonId == salonId
            orderby employee.Name
            select new PublicEmployeeSummary(employee.Id, employee.Name)
        ).ToListAsync(cancellationToken);
    }

    public async Task<AvailabilityResult> Availability(AvailabilityQuery query, CancellationToken cancellationToken)
    {
        var salonId = await ResolvePublicSalonId(cancellationToken);
        if (!await IsBookable(salonId, query.ServiceId, cancellationToken))
            throw new KeyNotFoundException("Service was not found.");
        return await availability.QueryForSalon(salonId, query, cancellationToken);
    }

    public async Task<PublicBookingConfirmation> Create(CreatePublicBookingInput input, CancellationToken cancellationToken)
    {
        var salonId = await ResolvePublicSalonId(cancellationToken);
        var customer = new Customer(Guid.NewGuid(), salonId, input.CustomerName, input.CustomerPhone, input.CustomerEmail);
        if (!TimeOnly.TryParseExact(input.StartsAtLocal, "HH:mm", out var localTime))
            throw new ArgumentException("Start time must use HH:mm.", nameof(input.StartsAtLocal));

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var slots = await availability.QueryForSalon(salonId,
                new AvailabilityQuery(input.ServiceId, input.Date, input.EmployeeId), cancellationToken);
            var match = slots.Slots.FirstOrDefault(x => x.StartsAtLocal == input.StartsAtLocal);
            if (match is null) throw new SlotUnavailableException();
            if (input.EmployeeId is Guid wanted && match.EmployeeId != wanted)
                throw new SlotUnavailableException();

            var service = await database.Services.AsNoTracking()
                .SingleAsync(x => x.Id == input.ServiceId && x.SalonId == salonId, cancellationToken);
            var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);
            var requirement = await database.ServiceResourceRequirements.AsNoTracking()
                .SingleAsync(x => x.ServiceId == service.Id, cancellationToken);
            var endUtc = match.StartsAtUtc.AddMinutes(service.DurationMinutes + requirement.BufferMinutes);

            database.Customers.Add(customer);
            var bookingId = Guid.NewGuid();
            database.Bookings.Add(new Booking(bookingId, salonId, service.Id, match.EmployeeId, match.SeatId,
                match.StartsAtUtc, endUtc, customer.Id));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PublicBookingConfirmation(bookingId, salon.Name, service.Name, input.StartsAtLocal, input.Date, customer.Name);
        }
        catch (DbUpdateException error) when (IsExclusionViolation(error))
        {
            throw new SlotUnavailableException();
        }
    }

    private async Task<bool> IsBookable(Guid salonId, Guid serviceId, CancellationToken cancellationToken)
    {
        var service = await database.Services.AsNoTracking()
            .AnyAsync(x => x.Id == serviceId && x.SalonId == salonId, cancellationToken);
        if (!service) return false;
        var requirement = await database.ServiceResourceRequirements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ServiceId == serviceId, cancellationToken);
        if (requirement is null) return false;
        var skilled = await database.EmployeeSkills.AsNoTracking().AnyAsync(x => x.ServiceId == serviceId, cancellationToken);
        if (!skilled) return false;
        return await (
            from seat in database.Seats.AsNoTracking()
            join link in database.SeatServices.AsNoTracking() on seat.Id equals link.SeatId
            where seat.SalonId == salonId && seat.Type == requirement.SeatType && link.ServiceId == serviceId
            select seat.Id
        ).AnyAsync(cancellationToken);
    }

    private async Task<Guid> ResolvePublicSalonId(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(configuration["Booking:PublicSalonId"], out var configured))
        {
            if (!await database.Salons.AsNoTracking().AnyAsync(x => x.Id == configured, cancellationToken))
                throw new InvalidOperationException("Configured public salon was not found.");
            return configured;
        }
        var ids = await database.Salons.AsNoTracking().Select(x => x.Id).Take(2).ToListAsync(cancellationToken);
        if (ids.Count == 1) return ids[0];
        throw new InvalidOperationException("Configure Booking:PublicSalonId when more than one salon exists.");
    }

    private static bool IsExclusionViolation(DbUpdateException error) =>
        error.InnerException is PostgresException postgres &&
        (postgres.SqlState == PostgresErrorCodes.ExclusionViolation || postgres.ConstraintName is "no_employee_overlap" or "no_seat_overlap");
}

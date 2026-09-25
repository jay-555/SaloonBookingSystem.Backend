using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class AvailabilityService(SalonDbContext database) : IAvailabilityService
{
    public async Task<AvailabilityResult> Query(Guid userId, AvailabilityQuery query, CancellationToken cancellationToken)
    {
        var salonId = await RequireSalon(userId, cancellationToken);
        var service = await database.Services.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == query.ServiceId && x.SalonId == salonId, cancellationToken)
            ?? throw new KeyNotFoundException("Service was not found.");
        var requirement = await database.ServiceResourceRequirements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ServiceId == service.Id, cancellationToken)
            ?? throw new ArgumentException("Configure resource requirements for this service before querying availability.", "serviceId");

        var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);
        var day = (int)query.Date.DayOfWeek;

        var skilledEmployeeIds = await database.EmployeeSkills.AsNoTracking()
            .Where(x => x.ServiceId == service.Id)
            .Select(x => x.EmployeeId)
            .ToListAsync(cancellationToken);
        if (skilledEmployeeIds.Count == 0)
            throw new ArgumentException("At least one employee must be skilled for this service.", "serviceId");

        if (query.EmployeeId is Guid requested)
        {
            if (!skilledEmployeeIds.Contains(requested))
                throw new ArgumentException("The selected employee is not skilled for this service.", "employeeId");
            var owned = await database.Employees.AsNoTracking()
                .AnyAsync(x => x.Id == requested && x.SalonId == salonId, cancellationToken);
            if (!owned) throw new KeyNotFoundException("Employee was not found.");
            skilledEmployeeIds = [requested];
        }

        var hours = await database.EmployeeWorkingDays.AsNoTracking()
            .Where(x => skilledEmployeeIds.Contains(x.EmployeeId) && x.Day == day)
            .ToListAsync(cancellationToken);
        var breaks = await database.EmployeeBreaks.AsNoTracking()
            .Where(x => skilledEmployeeIds.Contains(x.EmployeeId) && x.Day == day)
            .ToListAsync(cancellationToken);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var dayStartLocal = new DateTime(query.Date.Year, query.Date.Month, query.Date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, zone));
        var dayEndUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, zone));

        var leave = await database.EmployeeLeaves.AsNoTracking()
            .Where(x => skilledEmployeeIds.Contains(x.EmployeeId) && x.StartsAtUtc < dayEndUtc && x.EndsAtUtc > dayStartUtc)
            .ToListAsync(cancellationToken);
        var employeeBookings = await database.Bookings.AsNoTracking()
            .Where(x => skilledEmployeeIds.Contains(x.EmployeeId) && x.StartsAtUtc < dayEndUtc && x.EndsAtUtc > dayStartUtc)
            .ToListAsync(cancellationToken);

        var seats = await (
            from seat in database.Seats.AsNoTracking()
            join link in database.SeatServices.AsNoTracking() on seat.Id equals link.SeatId
            where seat.SalonId == salonId && seat.Type == requirement.SeatType && link.ServiceId == service.Id
            select seat
        ).ToListAsync(cancellationToken);
        if (seats.Count == 0)
            throw new ArgumentException("No seats of the required type support this service.", "serviceId");

        var seatIds = seats.Select(x => x.Id).ToArray();
        var seatBookings = await database.Bookings.AsNoTracking()
            .Where(x => seatIds.Contains(x.SeatId) && x.StartsAtUtc < dayEndUtc && x.EndsAtUtc > dayStartUtc)
            .ToListAsync(cancellationToken);

        var employees = skilledEmployeeIds.Select(id =>
        {
            var hour = hours.SingleOrDefault(x => x.EmployeeId == id);
            AvailabilityEngine.DayInterval? interval = hour is { OpensAt: not null, ClosesAt: not null }
                ? new AvailabilityEngine.DayInterval(hour.OpensAt!.Value, hour.ClosesAt!.Value)
                : null;
            return new AvailabilityEngine.EmployeeDay(
                id,
                interval,
                breaks.Where(x => x.EmployeeId == id).Select(x => new AvailabilityEngine.BreakInterval(x.StartsAt, x.EndsAt)).ToArray(),
                leave.Where(x => x.EmployeeId == id).Select(x => new AvailabilityEngine.BusyInterval(x.StartsAtUtc, x.EndsAtUtc)).ToArray(),
                employeeBookings.Where(x => x.EmployeeId == id).Select(x => new AvailabilityEngine.BusyInterval(x.StartsAtUtc, x.EndsAtUtc)).ToArray());
        }).ToArray();

        var seatDays = seats.Select(seat => new AvailabilityEngine.SeatDay(
            seat.Id,
            seatBookings.Where(x => x.SeatId == seat.Id).Select(x => new AvailabilityEngine.BusyInterval(x.StartsAtUtc, x.EndsAtUtc)).ToArray()
        )).ToArray();

        var slots = AvailabilityEngine.FindSlots(new AvailabilityEngine.Request(
            query.Date, salon.TimeZoneId, service.DurationMinutes, requirement.BufferMinutes, employees, seatDays));

        return new AvailabilityResult(
            service.Id,
            query.Date,
            salon.TimeZoneId,
            slots.Select(x => new AvailableSlot(x.StartsAtLocal, x.StartsAtUtc, x.EmployeeId, x.SeatId)).ToArray());
    }

    private async Task<Guid> RequireSalon(Guid userId, CancellationToken cancellationToken)
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
}

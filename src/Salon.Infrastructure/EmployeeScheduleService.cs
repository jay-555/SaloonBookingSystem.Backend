using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class EmployeeScheduleService(SalonDbContext database) : IEmployeeSchedule
{
    public async Task<EmployeeScheduleResult> Query(Guid userId, Guid employeeId, DateOnly date, string view, CancellationToken cancellationToken)
    {
        var account = await database.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Role, x.SalonId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (account.SalonId is not Guid salonId) throw new UnauthorizedAccessException();

        // Identity↔Employee link is not modeled yet; Employee role cannot open calendars
        // (avoids leaking other staff schedules). OwnerAdmin/Manager read any salon employee.
        if (account.Role is not (SalonRoles.OwnerAdmin or "Manager"))
            throw new UnauthorizedAccessException();

        view = view.Trim().ToLowerInvariant();
        var (from, to) = EmployeeScheduleCalendar.Window(view, date);

        var employee = await database.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == employeeId && x.SalonId == salonId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee was not found.");
        var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var rangeStartLocal = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var rangeEndLocal = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var rangeStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, zone));
        var rangeEndUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(rangeEndLocal, zone));

        var bookings = await (
            from booking in database.Bookings.AsNoTracking()
            join service in database.Services.AsNoTracking() on booking.ServiceId equals service.Id
            join seat in database.Seats.AsNoTracking() on booking.SeatId equals seat.Id
            where booking.EmployeeId == employeeId && booking.CancelledAtUtc == null && booking.StartsAtUtc < rangeEndUtc && booking.EndsAtUtc > rangeStartUtc
            select new EmployeeScheduleCalendar.BookingSlice(booking.Id, booking.StartsAtUtc, booking.EndsAtUtc, service.Name, seat.Name)
        ).ToListAsync(cancellationToken);

        var leaves = await database.EmployeeLeaves.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.StartsAtUtc < rangeEndUtc && x.EndsAtUtc > rangeStartUtc)
            .Select(x => new EmployeeScheduleCalendar.LeaveSlice(x.Id, x.StartsAtUtc, x.EndsAtUtc))
            .ToListAsync(cancellationToken);

        var breaks = await database.EmployeeBreaks.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => new EmployeeScheduleCalendar.BreakSlice(x.Day, x.StartsAt, x.EndsAt))
            .ToListAsync(cancellationToken);

        var items = EmployeeScheduleCalendar.Assemble(salon.TimeZoneId, from, to, bookings, leaves, breaks)
            .Select(x => new ScheduleItem(x.Kind, x.Date, x.StartsAtLocal, x.EndsAtLocal, x.Label, x.SourceId, x.SeatName))
            .ToArray();

        return new EmployeeScheduleResult(employee.Id, employee.Name, salon.TimeZoneId, view, date, from, to, items);
    }
}

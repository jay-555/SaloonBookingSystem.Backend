using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class SeatScheduleService(SalonDbContext database) : ISeatSchedule
{
    public async Task<SeatScheduleResult> Query(Guid userId, Guid seatId, DateOnly date, string view, CancellationToken cancellationToken)
    {
        var account = await database.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Role, x.SalonId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (account.SalonId is not Guid salonId) throw new UnauthorizedAccessException();
        if (account.Role is not (SalonRoles.OwnerAdmin or "Manager"))
            throw new UnauthorizedAccessException();

        view = view.Trim().ToLowerInvariant();
        var (from, to) = EmployeeScheduleCalendar.Window(view, date);

        var seat = await database.Seats.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == seatId && x.SalonId == salonId, cancellationToken)
            ?? throw new KeyNotFoundException("Seat was not found.");
        var salon = await database.Salons.AsNoTracking().SingleAsync(x => x.Id == salonId, cancellationToken);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var rangeStartLocal = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var rangeEndLocal = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var rangeStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, zone));
        var rangeEndUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(rangeEndLocal, zone));

        var bookings = await (
            from booking in database.Bookings.AsNoTracking()
            join service in database.Services.AsNoTracking() on booking.ServiceId equals service.Id
            join employee in database.Employees.AsNoTracking() on booking.EmployeeId equals employee.Id
            where booking.SeatId == seatId && booking.CancelledAtUtc == null && booking.StartsAtUtc < rangeEndUtc && booking.EndsAtUtc > rangeStartUtc
            select new EmployeeScheduleCalendar.BookingSlice(
                booking.Id,
                booking.StartsAtUtc,
                booking.EndsAtUtc,
                service.Name + " · " + employee.Name,
                employee.Name)
        ).ToListAsync(cancellationToken);

        var items = EmployeeScheduleCalendar.Assemble(salon.TimeZoneId, from, to, bookings, [], [])
            .Select(x => new ScheduleItem(x.Kind, x.Date, x.StartsAtLocal, x.EndsAtLocal, x.Label, x.SourceId, x.SeatName))
            .ToArray();

        return new SeatScheduleResult(seat.Id, seat.Name, seat.Type, salon.TimeZoneId, view, date, from, to, items);
    }
}

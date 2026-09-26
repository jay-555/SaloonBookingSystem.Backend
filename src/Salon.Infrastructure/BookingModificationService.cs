using Microsoft.EntityFrameworkCore;
using Npgsql;
using Salon.Application;
using Salon.Domain.Entities;

namespace Salon.Infrastructure;

public sealed class BookingModificationService(SalonDbContext database, IAvailabilityService availability) : IBookingModification
{
    public async Task<StaffBookingDetail> Get(Guid userId, Guid bookingId, CancellationToken cancellationToken)
    {
        var salonId = await RequireStaffSalon(userId, cancellationToken);
        return await LoadDetail(salonId, bookingId, cancellationToken)
            ?? throw new KeyNotFoundException("Booking was not found.");
    }

    public async Task<StaffBookingDetail> Reschedule(Guid userId, Guid bookingId, RescheduleBookingInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireStaffSalon(userId, cancellationToken);
        if (!TimeOnly.TryParseExact(input.StartsAtLocal, "HH:mm", out _))
            throw new ArgumentException("Start time must use HH:mm.", nameof(input.StartsAtLocal));

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var booking = await database.Bookings
                .SingleOrDefaultAsync(x => x.Id == bookingId && x.SalonId == salonId, cancellationToken)
                ?? throw new KeyNotFoundException("Booking was not found.");
            if (!booking.IsActive)
                throw new InvalidOperationException("Cancelled booking cannot be rescheduled.");

            var fromStart = booking.StartsAtUtc;
            var fromEnd = booking.EndsAtUtc;
            var fromEmployee = booking.EmployeeId;
            var fromSeat = booking.SeatId;

            var slots = await availability.QueryForSalon(salonId,
                new AvailabilityQuery(booking.ServiceId, input.Date, input.EmployeeId, booking.Id), cancellationToken);
            var match = slots.Slots.FirstOrDefault(x => x.StartsAtLocal == input.StartsAtLocal);
            if (match is null) throw new SlotUnavailableException();
            if (input.EmployeeId is Guid wanted && match.EmployeeId != wanted)
                throw new SlotUnavailableException();

            var service = await database.Services.AsNoTracking()
                .SingleAsync(x => x.Id == booking.ServiceId && x.SalonId == salonId, cancellationToken);
            var requirement = await database.ServiceResourceRequirements.AsNoTracking()
                .SingleAsync(x => x.ServiceId == service.Id, cancellationToken);
            var endUtc = match.StartsAtUtc.AddMinutes(service.DurationMinutes + requirement.BufferMinutes);

            booking.Reschedule(match.EmployeeId, match.SeatId, match.StartsAtUtc, endUtc);
            database.BookingAudits.Add(new BookingAudit(
                Guid.NewGuid(), booking.Id, salonId, userId, DateTimeOffset.UtcNow,
                BookingAuditOperations.Reschedule,
                fromStart, fromEnd, fromEmployee, fromSeat,
                match.StartsAtUtc, endUtc, match.EmployeeId, match.SeatId,
                booking.Status, booking.Status));

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (await LoadDetail(salonId, booking.Id, cancellationToken))!;
        }
        catch (DbUpdateException error) when (IsExclusionViolation(error))
        {
            throw new SlotUnavailableException();
        }
    }

    public async Task Cancel(Guid userId, Guid bookingId, CancellationToken cancellationToken)
    {
        await Transition(userId, bookingId, new TransitionBookingInput(BookingStatuses.Cancelled), cancellationToken);
    }

    public async Task<StaffBookingDetail> Transition(Guid userId, Guid bookingId, TransitionBookingInput input, CancellationToken cancellationToken)
    {
        var salonId = await RequireStaffSalon(userId, cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var booking = await database.Bookings
            .SingleOrDefaultAsync(x => x.Id == bookingId && x.SalonId == salonId, cancellationToken)
            ?? throw new KeyNotFoundException("Booking was not found.");

        var fromStatus = booking.Status;
        var fromStart = booking.StartsAtUtc;
        var fromEnd = booking.EndsAtUtc;
        var fromEmployee = booking.EmployeeId;
        var fromSeat = booking.SeatId;
        var at = DateTimeOffset.UtcNow;
        try
        {
            booking.TransitionTo(input.Status, at);
        }
        catch (InvalidOperationException)
        {
            throw;
        }

        var operation = input.Status == BookingStatuses.Cancelled && fromStatus != BookingStatuses.Cancelled
            ? BookingAuditOperations.Cancel
            : BookingAuditOperations.StatusChange;
        database.BookingAudits.Add(new BookingAudit(
            Guid.NewGuid(), booking.Id, salonId, userId, at,
            operation,
            fromStart, fromEnd, fromEmployee, fromSeat,
            operation == BookingAuditOperations.Cancel ? null : fromStart,
            operation == BookingAuditOperations.Cancel ? null : fromEnd,
            operation == BookingAuditOperations.Cancel ? null : fromEmployee,
            operation == BookingAuditOperations.Cancel ? null : fromSeat,
            fromStatus, booking.Status));

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await LoadDetail(salonId, booking.Id, cancellationToken))!;
    }

    private async Task<StaffBookingDetail?> LoadDetail(Guid salonId, Guid bookingId, CancellationToken cancellationToken)
    {
        var row = await (
            from booking in database.Bookings.AsNoTracking()
            join service in database.Services.AsNoTracking() on booking.ServiceId equals service.Id
            join employee in database.Employees.AsNoTracking() on booking.EmployeeId equals employee.Id
            join seat in database.Seats.AsNoTracking() on booking.SeatId equals seat.Id
            join salon in database.Salons.AsNoTracking() on booking.SalonId equals salon.Id
            where booking.Id == bookingId && booking.SalonId == salonId
            select new { booking, service, employee, seat, salon }
        ).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var customerName = row.booking.CustomerId is Guid customerId
            ? await database.Customers.AsNoTracking().Where(x => x.Id == customerId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
            : null;

        var zone = TimeZoneInfo.FindSystemTimeZoneById(row.salon.TimeZoneId);
        var startLocal = TimeZoneInfo.ConvertTime(row.booking.StartsAtUtc, zone);
        var endLocal = TimeZoneInfo.ConvertTime(row.booking.EndsAtUtc, zone);
        return new StaffBookingDetail(
            row.booking.Id,
            row.service.Id,
            row.service.Name,
            row.employee.Id,
            row.employee.Name,
            row.seat.Id,
            row.seat.Name,
            startLocal.ToString("HH:mm"),
            endLocal.ToString("HH:mm"),
            DateOnly.FromDateTime(startLocal.DateTime),
            row.salon.TimeZoneId,
            customerName,
            !row.booking.IsActive,
            row.booking.Status,
            BookingStatusTransitions.Next(row.booking.Status));
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

    private static bool IsExclusionViolation(DbUpdateException error) =>
        error.InnerException is PostgresException postgres &&
        (postgres.SqlState == PostgresErrorCodes.ExclusionViolation || postgres.ConstraintName is "no_employee_overlap" or "no_seat_overlap");
}

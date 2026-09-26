namespace Salon.Domain.Entities;

public sealed class BookingAudit
{
    private BookingAudit() { }

    public BookingAudit(
        Guid id,
        Guid bookingId,
        Guid salonId,
        Guid actorUserId,
        DateTimeOffset atUtc,
        string operation,
        DateTimeOffset fromStartsAtUtc,
        DateTimeOffset fromEndsAtUtc,
        Guid fromEmployeeId,
        Guid fromSeatId,
        DateTimeOffset? toStartsAtUtc,
        DateTimeOffset? toEndsAtUtc,
        Guid? toEmployeeId,
        Guid? toSeatId,
        string? fromStatus = null,
        string? toStatus = null)
    {
        Id = DomainGuard.Id(id, nameof(id));
        BookingId = DomainGuard.Id(bookingId, nameof(bookingId));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        ActorUserId = DomainGuard.Id(actorUserId, nameof(actorUserId));
        AtUtc = atUtc.ToUniversalTime();
        Operation = DomainGuard.Text(operation, nameof(operation));
        if (Operation is not (BookingAuditOperations.Reschedule or BookingAuditOperations.Cancel or BookingAuditOperations.StatusChange))
            throw new ArgumentException("Unsupported booking audit operation.", nameof(operation));
        FromStartsAtUtc = fromStartsAtUtc.ToUniversalTime();
        FromEndsAtUtc = fromEndsAtUtc.ToUniversalTime();
        FromEmployeeId = DomainGuard.Id(fromEmployeeId, nameof(fromEmployeeId));
        FromSeatId = DomainGuard.Id(fromSeatId, nameof(fromSeatId));

        if (Operation == BookingAuditOperations.StatusChange)
        {
            if (fromStatus is null || toStatus is null)
                throw new ArgumentException("Status change audit requires from and to statuses.", nameof(toStatus));
            FromStatus = DomainGuard.Text(fromStatus, nameof(fromStatus));
            ToStatus = DomainGuard.Text(toStatus, nameof(toStatus));
            if (!BookingStatuses.IsKnown(FromStatus) || !BookingStatuses.IsKnown(ToStatus))
                throw new ArgumentException("Status change audit requires known statuses.", nameof(toStatus));
            ToStartsAtUtc = ToStartsAtUtcOrSame(toStartsAtUtc, fromStartsAtUtc);
            ToEndsAtUtc = ToEndsAtUtcOrSame(toEndsAtUtc, fromEndsAtUtc);
            ToEmployeeId = toEmployeeId ?? fromEmployeeId;
            ToSeatId = toSeatId ?? fromSeatId;
            return;
        }

        if (Operation == BookingAuditOperations.Cancel)
        {
            if (toStartsAtUtc is not null || toEndsAtUtc is not null || toEmployeeId is not null || toSeatId is not null)
                throw new ArgumentException("Cancel audit must not include a destination reservation.", nameof(operation));
            FromStatus = fromStatus;
            ToStatus = toStatus ?? BookingStatuses.Cancelled;
            return;
        }

        if (toStartsAtUtc is not DateTimeOffset toStart || toEndsAtUtc is not DateTimeOffset toEnd)
            throw new ArgumentException("Reschedule audit requires destination times.", nameof(toStartsAtUtc));
        if (toEnd <= toStart)
            throw new ArgumentException("Booking end must be after start.", nameof(toEndsAtUtc));
        ToStartsAtUtc = toStart.ToUniversalTime();
        ToEndsAtUtc = toEnd.ToUniversalTime();
        ToEmployeeId = DomainGuard.Id(toEmployeeId!.Value, nameof(toEmployeeId));
        ToSeatId = DomainGuard.Id(toSeatId!.Value, nameof(toSeatId));
        FromStatus = fromStatus;
        ToStatus = toStatus;
    }

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid SalonId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }
    public string Operation { get; private set; } = "";
    public DateTimeOffset FromStartsAtUtc { get; private set; }
    public DateTimeOffset FromEndsAtUtc { get; private set; }
    public Guid FromEmployeeId { get; private set; }
    public Guid FromSeatId { get; private set; }
    public DateTimeOffset? ToStartsAtUtc { get; private set; }
    public DateTimeOffset? ToEndsAtUtc { get; private set; }
    public Guid? ToEmployeeId { get; private set; }
    public Guid? ToSeatId { get; private set; }
    public string? FromStatus { get; private set; }
    public string? ToStatus { get; private set; }

    private static DateTimeOffset ToStartsAtUtcOrSame(DateTimeOffset? value, DateTimeOffset fallback) =>
        (value ?? fallback).ToUniversalTime();

    private static DateTimeOffset ToEndsAtUtcOrSame(DateTimeOffset? value, DateTimeOffset fallback) =>
        (value ?? fallback).ToUniversalTime();
}

public static class BookingAuditOperations
{
    public const string Reschedule = "Reschedule";
    public const string Cancel = "Cancel";
    public const string StatusChange = "StatusChange";
}

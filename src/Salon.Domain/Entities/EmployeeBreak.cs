namespace Salon.Domain.Entities;

public sealed class EmployeeBreak
{
    private EmployeeBreak() { }

    public EmployeeBreak(Guid employeeId, int day, int startsAt, int endsAt)
    {
        Id = Guid.NewGuid();
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        if (day is < 0 or > 6) throw new ArgumentException("Day must be between Sunday (0) and Saturday (6).", nameof(day));
        if (startsAt is < 0 or > 1439 || endsAt is < 0 or > 1439 || startsAt >= endsAt)
            throw new ArgumentException("Breaks must be same-day intervals with a start before the end.", nameof(startsAt));
        Day = day;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public int Day { get; private set; }
    public int StartsAt { get; private set; }
    public int EndsAt { get; private set; }
}

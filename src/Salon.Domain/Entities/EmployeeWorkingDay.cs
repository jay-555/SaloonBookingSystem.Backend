namespace Salon.Domain.Entities;

public sealed class EmployeeWorkingDay
{
    private EmployeeWorkingDay() { }

    public EmployeeWorkingDay(Guid employeeId, int day, int? opensAt, int? closesAt)
    {
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        if (day is < 0 or > 6) throw new ArgumentException("Day must be between Sunday (0) and Saturday (6).", nameof(day));
        if ((opensAt is null) != (closesAt is null) ||
            opensAt is < 0 or > 1439 || closesAt is < 0 or > 1439 || opensAt >= closesAt)
            throw new ArgumentException("Use one same-day interval with opening before closing, or leave both times empty.", nameof(opensAt));
        Day = day;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public Guid EmployeeId { get; private set; }
    public int Day { get; private set; }
    public int? OpensAt { get; private set; }
    public int? ClosesAt { get; private set; }

    public bool IsOpen => OpensAt is not null;
}

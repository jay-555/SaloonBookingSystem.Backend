namespace Salon.Domain.Entities;

public sealed class WorkingDay
{
    private WorkingDay() { }

    public WorkingDay(Guid salonId, int day, int? opensAt, int? closesAt)
    {
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        if (day is < 0 or > 6) throw new ArgumentException("Day must be between Sunday (0) and Saturday (6).", nameof(day));
        if ((opensAt is null) != (closesAt is null) ||
            opensAt is < 0 or > 1439 || closesAt is < 0 or > 1439 || opensAt >= closesAt)
            throw new ArgumentException("Use one same-day interval with opening before closing, or leave both times empty.", nameof(opensAt));
        Day = day;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public Guid SalonId { get; private set; }
    public int Day { get; private set; }
    public int? OpensAt { get; private set; }
    public int? ClosesAt { get; private set; }

    public static void ValidateWeek(IReadOnlyCollection<WorkingDay> days)
    {
        if (days.Count != 7 || days.Select(day => day.Day).Distinct().Count() != 7 ||
            days.Select(day => day.SalonId).Distinct().Count() != 1)
            throw new ArgumentException("Supply every weekday exactly once for the same salon.", nameof(days));
    }
}

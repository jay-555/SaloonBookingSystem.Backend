namespace Salon.Domain.Entities;

public static class EmployeeSchedule
{
    public static EmployeeSkill[] Skills(Guid employeeId, Guid salonId, IReadOnlyCollection<Guid> serviceIds, IReadOnlyDictionary<Guid, Guid> serviceSalonById)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        ArgumentNullException.ThrowIfNull(serviceSalonById);
        DomainGuard.Id(employeeId, nameof(employeeId));
        DomainGuard.Id(salonId, nameof(salonId));
        if (serviceIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Service identifiers must not be empty.", nameof(serviceIds));
        if (serviceIds.Distinct().Count() != serviceIds.Count)
            throw new ArgumentException("Service skills must be unique.", nameof(serviceIds));
        foreach (var serviceId in serviceIds)
        {
            if (!serviceSalonById.TryGetValue(serviceId, out var owner) || owner != salonId)
                throw new ArgumentException("Skills must reference services from the same salon.", nameof(serviceIds));
        }
        return serviceIds.Select(id => new EmployeeSkill(employeeId, id)).ToArray();
    }

    public static void ValidateWeek(IReadOnlyCollection<EmployeeWorkingDay> days, IReadOnlyCollection<EmployeeBreak> breaks)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(breaks);
        if (days.Count != 7 || days.Select(day => day.Day).Distinct().Count() != 7 ||
            days.Select(day => day.EmployeeId).Distinct().Count() != 1)
            throw new ArgumentException("Supply every weekday exactly once for the same employee.", nameof(days));
        var employeeId = days.First().EmployeeId;
        if (breaks.Any(item => item.EmployeeId != employeeId))
            throw new ArgumentException("Breaks must belong to the same employee as the weekly hours.", nameof(breaks));

        var openByDay = days.ToDictionary(day => day.Day);
        foreach (var group in breaks.GroupBy(item => item.Day))
        {
            var day = openByDay[group.Key];
            if (!day.IsOpen)
                throw new ArgumentException("Breaks are not allowed on closed days.", nameof(breaks));
            var ordered = group.OrderBy(item => item.StartsAt).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var item = ordered[index];
                if (item.StartsAt < day.OpensAt || item.EndsAt > day.ClosesAt)
                    throw new ArgumentException("Breaks must fall inside the open interval.", nameof(breaks));
                if (index > 0 && ordered[index - 1].EndsAt > item.StartsAt)
                    throw new ArgumentException("Breaks on the same day must not overlap.", nameof(breaks));
            }
        }
    }

    public static EmployeeWorkingDay[] ClosedWeek(Guid employeeId) =>
        Enumerable.Range(0, 7).Select(day => new EmployeeWorkingDay(employeeId, day, null, null)).ToArray();
}

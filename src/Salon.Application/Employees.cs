using System.Globalization;
using Salon.Domain.Entities;

namespace Salon.Application;

public sealed record BreakInput(int Day, string StartsAt, string EndsAt);
public sealed record EmployeeDayInput(int Day, string? OpensAt, string? ClosesAt, BreakInput[] Breaks);
public sealed record EmployeeInput(string Name, Guid[] ServiceIds, EmployeeDayInput[] Hours)
{
    public (EmployeeWorkingDay[] Days, EmployeeBreak[] Breaks, EmployeeSkill[] Skills) Validate(
        Guid employeeId, Guid salonId, IReadOnlyDictionary<Guid, Guid> serviceSalonById)
    {
        _ = new Employee(employeeId, salonId, Name);
        if (Hours is null || Hours.Any(day => day is null))
            throw new ArgumentException("Supply all seven weekdays.", nameof(Hours));
        if (ServiceIds is null) throw new ArgumentException("Supply a service skill list.", nameof(ServiceIds));
        var days = Hours.Select(day => new EmployeeWorkingDay(employeeId, day.Day, Minutes(day.OpensAt), Minutes(day.ClosesAt))).ToArray();
        var breaks = Hours.SelectMany(day =>
        {
            if (day.Breaks is null || day.Breaks.Any(item => item is null))
                throw new ArgumentException("Break lists must not contain empty entries.", nameof(Hours));
            return day.Breaks.Select(item =>
            {
                if (item.Day != day.Day)
                    throw new ArgumentException("Breaks must use the same weekday as their parent day.", nameof(Hours));
                return new EmployeeBreak(employeeId, item.Day, RequiredMinutes(item.StartsAt), RequiredMinutes(item.EndsAt));
            });
        }).ToArray();
        EmployeeSchedule.ValidateWeek(days, breaks);
        var skills = EmployeeSchedule.Skills(employeeId, salonId, ServiceIds, serviceSalonById);
        return (days, breaks, skills);
    }

    private static int? Minutes(string? value)
    {
        if (value is null) return null;
        return RequiredMinutes(value);
    }

    private static int RequiredMinutes(string value)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Times must use HH:mm.", nameof(Hours));
        return time.Hour * 60 + time.Minute;
    }
}

public sealed record EmployeeSummary(Guid Id, string Name);
public sealed record EmployeeDetail(Guid Id, string Name, Guid[] ServiceIds, EmployeeDayInput[] Hours);

public interface IEmployeeDirectory
{
    Task<IReadOnlyList<EmployeeSummary>> List(Guid userId, CancellationToken cancellationToken);
    Task<EmployeeDetail?> Get(Guid userId, Guid employeeId, CancellationToken cancellationToken);
    Task<EmployeeDetail> Create(Guid userId, EmployeeInput input, CancellationToken cancellationToken);
    Task<EmployeeDetail> Update(Guid userId, Guid employeeId, EmployeeInput input, CancellationToken cancellationToken);
    Task Delete(Guid userId, Guid employeeId, CancellationToken cancellationToken);
}

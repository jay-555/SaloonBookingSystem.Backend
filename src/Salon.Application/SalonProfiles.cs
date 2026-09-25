using System.Globalization;
using Salon.Domain.Entities;

namespace Salon.Application;

public static class SalonRoles
{
    public const string OwnerAdmin = "OwnerAdmin";
    public static bool IsValid(string role) => role is OwnerAdmin or "Manager" or "Employee";
}

public sealed record AccountProfile(Guid Id, string Login, string Role, Guid? SalonId);
public sealed record DayInput(int Day, string? OpensAt, string? ClosesAt);
public sealed record SalonInput(string Name, string TimeZoneId, DayInput[] Hours)
{
    public WorkingDay[] Validate(Guid salonId)
    {
        _ = new Domain.Entities.Salon(salonId, Name, TimeZoneId);
        if (Hours is null || Hours.Any(day => day is null)) throw new ArgumentException("Supply all seven weekdays.", nameof(Hours));
        var result = Hours.Select(day => new WorkingDay(salonId, day.Day, Minutes(day.OpensAt), Minutes(day.ClosesAt))).ToArray();
        WorkingDay.ValidateWeek(result);
        return result;
    }

    private static int? Minutes(string? value)
    {
        if (value is null) return null;
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Times must use HH:mm.", nameof(Hours));
        return time.Hour * 60 + time.Minute;
    }
}

public sealed record SalonProfile(Guid Id, string Name, string TimeZoneId, DayInput[] Hours);
public sealed class SetupConflictException : Exception;
public interface ISalonProfiles
{
    Task<AccountProfile?> Account(Guid userId, CancellationToken cancellationToken);
    Task<SalonProfile?> Read(Guid userId, CancellationToken cancellationToken);
    Task<SalonProfile> Save(Guid userId, SalonInput input, bool create, CancellationToken cancellationToken);
}

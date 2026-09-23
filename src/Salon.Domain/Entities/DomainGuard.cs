namespace Salon.Domain.Entities;

internal static class DomainGuard
{
    internal static Guid Id(Guid value, string parameterName) => value != Guid.Empty
        ? value : throw new ArgumentException("An identifier must not be empty.", parameterName);

    internal static string Text(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    internal static string IanaTimeZone(string value, string parameterName)
    {
        var id = Text(value, parameterName);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            if (zone.HasIanaId || id == "UTC")
            {
                return id;
            }
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        throw new ArgumentException("Use a valid IANA time-zone identifier.", parameterName);
    }
}

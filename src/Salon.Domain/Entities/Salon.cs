namespace Salon.Domain.Entities;

public sealed class Salon
{
    public const string DefaultTimeZoneId = "Asia/Kolkata";

    public Salon(Guid id, string name, string timeZoneId = DefaultTimeZoneId)
    {
        Id = DomainGuard.Id(id, nameof(id));
        Name = DomainGuard.Text(name, nameof(name));
        TimeZoneId = DomainGuard.IanaTimeZone(timeZoneId, nameof(timeZoneId));
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string TimeZoneId { get; private set; }

    public void UpdateProfile(string name, string timeZoneId)
    {
        var validName = DomainGuard.Text(name, nameof(name));
        var validZone = DomainGuard.IanaTimeZone(timeZoneId, nameof(timeZoneId));
        Name = validName;
        TimeZoneId = validZone;
    }
}

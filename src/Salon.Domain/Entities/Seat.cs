namespace Salon.Domain.Entities;

public sealed class Seat
{
    public Seat(Guid id, Guid salonId, string name, string type)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        Name = DomainGuard.Text(name, nameof(name));
        Type = DomainGuard.Text(type, nameof(type));
    }

    public Guid Id { get; private set; }
    public Guid SalonId { get; private set; }
    public string Name { get; private set; }
    public string Type { get; private set; }
}

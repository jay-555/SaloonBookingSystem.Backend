namespace Salon.Domain.Entities;

public sealed class Employee
{
    public Employee(Guid id, Guid salonId, string name)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        Name = DomainGuard.Text(name, nameof(name));
    }

    public Guid Id { get; private set; }
    public Guid SalonId { get; private set; }
    public string Name { get; private set; }

    public void Rename(string name) => Name = DomainGuard.Text(name, nameof(name));
}

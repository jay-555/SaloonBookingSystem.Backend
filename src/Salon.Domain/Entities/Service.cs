namespace Salon.Domain.Entities;

public sealed class Service
{
    public Service(Guid id, Guid salonId, string name, string category, decimal price, int durationMinutes)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        Name = DomainGuard.Text(name, nameof(name));
        Category = DomainGuard.Text(category, nameof(category));
        if (price < 0 || decimal.Round(price, 2) != price)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "INR price must be non-negative with at most two fractional digits.");
        }
        if (durationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), "Duration must be a positive whole number of minutes.");
        }
        Price = price;
        DurationMinutes = durationMinutes;
    }

    public Guid Id { get; private set; }
    public Guid SalonId { get; private set; }
    public string Name { get; private set; }
    public string Category { get; private set; }
    public decimal Price { get; private set; }
    public int DurationMinutes { get; private set; }
}

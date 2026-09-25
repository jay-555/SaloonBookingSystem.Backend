namespace Salon.Domain.Entities;

public sealed class Customer
{
    private Customer() { }

    public Customer(Guid id, Guid salonId, string name, string phone, string? email)
    {
        Id = DomainGuard.Id(id, nameof(id));
        SalonId = DomainGuard.Id(salonId, nameof(salonId));
        Name = DomainGuard.Text(name, nameof(name));
        Phone = NormalizePhone(phone);
        Email = NormalizeEmail(email);
    }

    public Guid Id { get; private set; }
    public Guid SalonId { get; private set; }
    public string Name { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public string? Email { get; private set; }

    public static string NormalizePhone(string phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phone, nameof(phone));
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
            digits = digits[2..];
        if (digits.Length != 10 || digits[0] is < '6' or > '9')
            throw new ArgumentException("Use a 10-digit Indian mobile number.", nameof(phone));
        return digits;
    }

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var value = email.Trim();
        if (value.Length > 200 || !value.Contains('@') || value.StartsWith('@') || value.EndsWith('@'))
            throw new ArgumentException("Enter a valid email address.", nameof(email));
        return value;
    }
}

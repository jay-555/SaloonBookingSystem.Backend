namespace Salon.Domain.Entities;

public sealed class EmployeeLeave
{
    private EmployeeLeave() { }

    public EmployeeLeave(Guid id, Guid employeeId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        Id = DomainGuard.Id(id, nameof(id));
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Leave end must be after start.", nameof(endsAtUtc));
        StartsAtUtc = startsAtUtc.ToUniversalTime();
        EndsAtUtc = endsAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
}

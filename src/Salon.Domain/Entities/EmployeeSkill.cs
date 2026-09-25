namespace Salon.Domain.Entities;

public sealed class EmployeeSkill
{
    private EmployeeSkill() { }

    public EmployeeSkill(Guid employeeId, Guid serviceId)
    {
        EmployeeId = DomainGuard.Id(employeeId, nameof(employeeId));
        ServiceId = DomainGuard.Id(serviceId, nameof(serviceId));
    }

    public Guid EmployeeId { get; private set; }
    public Guid ServiceId { get; private set; }
}

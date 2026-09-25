namespace Salon.Domain.Entities;

public sealed class ServiceResourceRequirement
{
    private ServiceResourceRequirement() { }

    public ServiceResourceRequirement(Guid serviceId, string seatType, int bufferMinutes, int employeeCapacity = 1)
    {
        ServiceId = DomainGuard.Id(serviceId, nameof(serviceId));
        Apply(seatType, bufferMinutes, employeeCapacity);
    }

    public Guid ServiceId { get; private set; }
    public int EmployeeCapacity { get; private set; }
    public string SeatType { get; private set; } = "";
    public int BufferMinutes { get; private set; }

    public void Replace(string seatType, int bufferMinutes, int employeeCapacity = 1) =>
        Apply(seatType, bufferMinutes, employeeCapacity);

    private void Apply(string seatType, int bufferMinutes, int employeeCapacity)
    {
        if (employeeCapacity != 1)
            throw new ArgumentOutOfRangeException(nameof(employeeCapacity), "Each service requires exactly one employee.");
        if (bufferMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferMinutes), "Buffer minutes must be zero or a positive whole number.");
        SeatType = DomainGuard.Text(seatType, nameof(seatType));
        EmployeeCapacity = employeeCapacity;
        BufferMinutes = bufferMinutes;
    }
}

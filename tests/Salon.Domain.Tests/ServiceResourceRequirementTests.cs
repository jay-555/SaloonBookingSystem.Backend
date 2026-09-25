using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class ServiceResourceRequirementTests
{
    [Fact]
    public void Requirement_requires_one_employee_seat_type_and_non_negative_buffer()
    {
        var serviceId = Guid.NewGuid();
        var requirement = new ServiceResourceRequirement(serviceId, " Chair ", 10);
        Assert.Equal(serviceId, requirement.ServiceId);
        Assert.Equal(1, requirement.EmployeeCapacity);
        Assert.Equal("Chair", requirement.SeatType);
        Assert.Equal(10, requirement.BufferMinutes);

        requirement.Replace(" FacialRoom ", 0);
        Assert.Equal("FacialRoom", requirement.SeatType);
        Assert.Equal(0, requirement.BufferMinutes);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceResourceRequirement(serviceId, "Chair", 5, employeeCapacity: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceResourceRequirement(serviceId, "Chair", -1));
        Assert.Throws<ArgumentException>(() => new ServiceResourceRequirement(serviceId, "  ", 0));
        Assert.Throws<ArgumentException>(() => new ServiceResourceRequirement(Guid.Empty, "Chair", 0));
    }
}

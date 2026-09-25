using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class EmployeeScheduleTests
{
    [Fact]
    public void Employee_rename_trims_and_rejects_blank()
    {
        var employee = new Employee(Guid.NewGuid(), Guid.NewGuid(), " Ava ");
        Assert.Equal("Ava", employee.Name);
        employee.Rename(" Jordan ");
        Assert.Equal("Jordan", employee.Name);
        Assert.Throws<ArgumentException>(() => employee.Rename("  "));
    }

    [Fact]
    public void Skills_require_same_salon_unique_services()
    {
        var employeeId = Guid.NewGuid();
        var salonId = Guid.NewGuid();
        var service = Guid.NewGuid();
        var otherSalon = Guid.NewGuid();
        var map = new Dictionary<Guid, Guid> { [service] = salonId, [Guid.NewGuid()] = otherSalon };
        Assert.Single(EmployeeSchedule.Skills(employeeId, salonId, [service], map));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.Skills(employeeId, salonId, [service, service], map));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.Skills(employeeId, salonId, [Guid.Empty], map));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.Skills(employeeId, salonId, [Guid.NewGuid()], map));
        var foreign = map.Keys.First(id => map[id] == otherSalon);
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.Skills(employeeId, salonId, [foreign], map));
    }

    [Theory]
    [InlineData(-1, 600, 700)]
    [InlineData(7, 600, 700)]
    [InlineData(1, 700, 600)]
    [InlineData(1, 600, 600)]
    [InlineData(1, -1, 700)]
    [InlineData(1, 600, 1440)]
    public void Invalid_breaks_are_rejected(int day, int starts, int ends) =>
        Assert.Throws<ArgumentException>(() => new EmployeeBreak(Guid.NewGuid(), day, starts, ends));

    [Fact]
    public void Week_and_breaks_must_be_consistent()
    {
        var id = Guid.NewGuid();
        var days = Enumerable.Range(0, 7)
            .Select(day => day == 1 ? new EmployeeWorkingDay(id, day, 9 * 60, 18 * 60) : new EmployeeWorkingDay(id, day, null, null))
            .ToArray();
        EmployeeSchedule.ValidateWeek(days, []);
        EmployeeSchedule.ValidateWeek(days, [new EmployeeBreak(id, 1, 12 * 60, 13 * 60)]);
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.ValidateWeek(days, [new EmployeeBreak(id, 0, 12 * 60, 13 * 60)]));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.ValidateWeek(days, [new EmployeeBreak(id, 1, 8 * 60, 9 * 60)]));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.ValidateWeek(days, [
            new EmployeeBreak(id, 1, 12 * 60, 13 * 60),
            new EmployeeBreak(id, 1, 12 * 60 + 30, 14 * 60),
        ]));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.ValidateWeek(days.Take(6).ToArray(), []));
        Assert.Throws<ArgumentException>(() => EmployeeSchedule.ValidateWeek(days, [new EmployeeBreak(Guid.NewGuid(), 1, 12 * 60, 13 * 60)]));
    }
}

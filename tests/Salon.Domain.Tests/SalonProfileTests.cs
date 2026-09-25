using Salon.Domain.Entities;
using Xunit;
using SalonEntity = Salon.Domain.Entities.Salon;

namespace Salon.Domain.Tests;

public sealed class SalonProfileTests
{
    [Fact]
    public void Profile_validation_is_atomic()
    {
        var salon = new SalonEntity(Guid.NewGuid(), "Before");
        Assert.Throws<ArgumentException>(() => salon.UpdateProfile("After", "Not/AZone"));
        Assert.Equal("Before", salon.Name);
        salon.UpdateProfile(" After ", "Europe/London");
        Assert.Equal("After", salon.Name);
        Assert.Equal("Europe/London", salon.TimeZoneId);
        Assert.Throws<ArgumentException>(() => salon.UpdateProfile("  ", "UTC"));
    }

    [Theory]
    [InlineData(-1, null, null)]
    [InlineData(7, null, null)]
    [InlineData(1, 600, null)]
    [InlineData(1, null, 600)]
    [InlineData(1, 600, 600)]
    [InlineData(1, 800, 700)]
    [InlineData(1, -1, 700)]
    [InlineData(1, 600, 1440)]
    public void Invalid_working_intervals_are_rejected(int day, int? opens, int? closes) =>
        Assert.Throws<ArgumentException>(() => new WorkingDay(Guid.NewGuid(), day, opens, closes));

    [Fact]
    public void A_week_requires_seven_unique_days_and_one_salon()
    {
        var id = Guid.NewGuid();
        var days = Enumerable.Range(0, 7).Select(day => new WorkingDay(id, day, null, null)).ToArray();
        WorkingDay.ValidateWeek(days);
        WorkingDay.ValidateWeek(Enumerable.Range(0, 7).Select(day => new WorkingDay(id, day, 0, 1439)).ToArray());
        Assert.Throws<ArgumentException>(() => new WorkingDay(Guid.Empty, 0, null, null));
        Assert.Throws<ArgumentException>(() => WorkingDay.ValidateWeek(days.Take(6).ToArray()));
        days[6] = days[0];
        Assert.Throws<ArgumentException>(() => WorkingDay.ValidateWeek(days));
        days[6] = new WorkingDay(Guid.NewGuid(), 6, null, null);
        Assert.Throws<ArgumentException>(() => WorkingDay.ValidateWeek(days));
    }
}

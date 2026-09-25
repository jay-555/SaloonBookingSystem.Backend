using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class ServiceProfileTests
{
    [Fact]
    public void Service_profile_trims_and_rejects_invalid_price_or_duration()
    {
        var service = new Service(Guid.NewGuid(), Guid.NewGuid(), " Haircut ", " Hair ", 500m, 45);
        Assert.Equal("Haircut", service.Name);
        Assert.Equal("Hair", service.Category);
        Assert.Equal(500m, service.Price);
        Assert.Equal(45, service.DurationMinutes);

        service.UpdateProfile(" Facial ", " Skin ", 800.50m, 60);
        Assert.Equal("Facial", service.Name);
        Assert.Equal("Skin", service.Category);
        Assert.Equal(800.50m, service.Price);
        Assert.Equal(60, service.DurationMinutes);

        Assert.Throws<ArgumentException>(() => service.UpdateProfile("  ", "Hair", 100m, 30));
        Assert.Throws<ArgumentException>(() => service.UpdateProfile("Cut", "  ", 100m, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateProfile("Cut", "Hair", -1m, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateProfile("Cut", "Hair", 10.999m, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateProfile("Cut", "Hair", 100m, 0));
        Assert.Throws<ArgumentException>(() => new Service(Guid.Empty, Guid.NewGuid(), "A", "B", 1m, 1));
    }
}

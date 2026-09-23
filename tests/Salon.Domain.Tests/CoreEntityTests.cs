using Salon.Domain.Entities;
using Xunit;
using SalonEntity = Salon.Domain.Entities.Salon;

namespace Salon.Domain.Tests;

public sealed class CoreEntityTests
{
    [Fact]
    public void Creates_core_entities_with_trimmed_text_and_ownership()
    {
        var salon = new SalonEntity(Guid.NewGuid(), "  Studio  ");
        var employee = new Employee(Guid.NewGuid(), salon.Id, "  Jay  ");
        var seat = new Seat(Guid.NewGuid(), salon.Id, "  Chair 1  ", "  Styling  ");
        var service = new Service(Guid.NewGuid(), salon.Id, " Haircut ", " Hair ", 500.25m, 45);
        Assert.Equal("Studio", salon.Name);
        Assert.Equal("Asia/Kolkata", salon.TimeZoneId);
        Assert.Equal("Jay", employee.Name);
        Assert.Equal("Chair 1", seat.Name);
        Assert.Equal("Styling", seat.Type);
        Assert.Equal("Haircut", service.Name);
        Assert.Equal("Hair", service.Category);
        Assert.Equal(500.25m, service.Price);
        Assert.Equal(45, service.DurationMinutes);
        Assert.All(new[] { employee.SalonId, seat.SalonId, service.SalonId }, id => Assert.Equal(salon.Id, id));
    }

    [Fact]
    public void Rejects_empty_entity_and_ownership_identifiers()
    {
        var id = Guid.NewGuid();
        Action[] invalid =
        [
            () => _ = new SalonEntity(Guid.Empty, "Studio"),
            () => _ = new Employee(Guid.Empty, id, "Jay"),
            () => _ = new Employee(id, Guid.Empty, "Jay"),
            () => _ = new Seat(Guid.Empty, id, "Chair", "Styling"),
            () => _ = new Seat(id, Guid.Empty, "Chair", "Styling"),
            () => _ = new Service(Guid.Empty, id, "Cut", "Hair", 0m, 1),
            () => _ = new Service(id, Guid.Empty, "Cut", "Hair", 0m, 1)
        ];
        foreach (var create in invalid) Assert.ThrowsAny<ArgumentException>(create);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\u00a0\u2003")]
    public void Rejects_blank_required_text(string? invalid)
    {
        var id = Guid.NewGuid();
        Action[] creates =
        [
            () => _ = new SalonEntity(id, invalid!),
            () => _ = new Employee(id, id, invalid!),
            () => _ = new Seat(id, id, invalid!, "Styling"),
            () => _ = new Seat(id, id, "Chair", invalid!),
            () => _ = new Service(id, id, invalid!, "Hair", 0, 1),
            () => _ = new Service(id, id, "Cut", invalid!, 0, 1)
        ];
        foreach (var create in creates) Assert.ThrowsAny<ArgumentException>(create);
    }

    [Theory]
    [InlineData("Asia/Kolkata")]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    [InlineData("Etc/UTC")]
    [InlineData("UTC")]
    public void Accepts_Iana_zones(string zone) =>
        Assert.Equal(zone, new SalonEntity(Guid.NewGuid(), "Studio", zone).TimeZoneId);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Not/AZone")]
    [InlineData("India Standard Time")]
    [InlineData("Eastern Standard Time")]
    public void Rejects_explicit_invalid_or_Windows_only_zones(string? zone) =>
        Assert.ThrowsAny<ArgumentException>(() => new SalonEntity(Guid.NewGuid(), "Studio", zone!));

    [Fact]
    public void Accepts_zero_cents_and_exact_decimal_extremes_but_rejects_rounding()
    {
        foreach (var price in new[] { 0m, 0.01m, 500.25m, 1.2300m, decimal.MaxValue })
        {
            Assert.Equal(price, new Service(Guid.NewGuid(), Guid.NewGuid(), "Cut", "Hair", price, 1).Price);
        }
        foreach (var price in new[] { -1m, -0.01m, 0.001m, 1.005m, decimal.MinValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Service(Guid.NewGuid(), Guid.NewGuid(), "Cut", "Hair", price, 1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_non_positive_duration(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Service(Guid.NewGuid(), Guid.NewGuid(), "Cut", "Hair", 0, minutes));
}

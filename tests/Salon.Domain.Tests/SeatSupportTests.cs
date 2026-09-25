using Salon.Domain.Entities;
using Xunit;

namespace Salon.Domain.Tests;

public sealed class SeatSupportTests
{
    [Fact]
    public void Seat_profile_trims_and_rejects_blank()
    {
        var seat = new Seat(Guid.NewGuid(), Guid.NewGuid(), " Chair 1 ", " Styling ");
        Assert.Equal("Chair 1", seat.Name);
        Assert.Equal("Styling", seat.Type);
        seat.UpdateProfile(" Facial 1 ", " FacialRoom ");
        Assert.Equal("Facial 1", seat.Name);
        Assert.Equal("FacialRoom", seat.Type);
        Assert.Throws<ArgumentException>(() => seat.UpdateProfile("  ", "Chair"));
        Assert.Throws<ArgumentException>(() => seat.UpdateProfile("Chair", "  "));
        Assert.Throws<ArgumentException>(() => new Seat(Guid.Empty, Guid.NewGuid(), "A", "B"));
    }

    [Fact]
    public void Supported_services_require_same_salon_unique_ids()
    {
        var seatId = Guid.NewGuid();
        var salonId = Guid.NewGuid();
        var service = Guid.NewGuid();
        var otherSalon = Guid.NewGuid();
        var map = new Dictionary<Guid, Guid> { [service] = salonId, [Guid.NewGuid()] = otherSalon };
        Assert.Single(SeatSupport.Services(seatId, salonId, [service], map));
        Assert.Throws<ArgumentException>(() => SeatSupport.Services(seatId, salonId, [service, service], map));
        Assert.Throws<ArgumentException>(() => SeatSupport.Services(seatId, salonId, [Guid.Empty], map));
        Assert.Throws<ArgumentException>(() => SeatSupport.Services(seatId, salonId, [Guid.NewGuid()], map));
        var foreign = map.Keys.First(id => map[id] == otherSalon);
        Assert.Throws<ArgumentException>(() => SeatSupport.Services(seatId, salonId, [foreign], map));
    }
}

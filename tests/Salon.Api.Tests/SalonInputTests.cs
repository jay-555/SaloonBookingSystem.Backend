using Salon.Application;
using Xunit;

namespace Salon.Api.Tests;

public sealed class SalonInputTests
{
    [Theory]
    [InlineData("9:00", "18:00")]
    [InlineData("09:00:01", "18:00")]
    [InlineData("09:00", "24:00")]
    [InlineData("", "18:00")]
    [InlineData("18:00", "09:00")]
    public void Invalid_time_inputs_are_rejected(string opens, string closes)
    {
        var input = new SalonInput("Studio", "Asia/Kolkata", Enumerable.Range(0, 7).Select(day => new DayInput(day, opens, closes)).ToArray());
        Assert.Throws<ArgumentException>(() => input.Validate(Guid.NewGuid()));
    }
    [Fact]
    public void Null_days_are_validation_errors_not_server_errors()
    {
        var input = new SalonInput("Studio", "UTC", [null!]);
        Assert.Throws<ArgumentException>(() => input.Validate(Guid.NewGuid()));
    }
}

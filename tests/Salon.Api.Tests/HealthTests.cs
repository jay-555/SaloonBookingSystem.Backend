using System.Net;
using Xunit;

namespace Salon.Api.Tests;

public sealed class HealthTests
{
    [Fact]
    public async Task Liveness_is_independent_of_database_and_readiness_is_sanitized()
    {
        await using var factory = new ApiFactory(
            "Host=127.0.0.1;Port=1;Database=salon;Username=probe;Password=never-expose-this;Timeout=1;Pooling=false");
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await live.Content.ReadAsStringAsync());

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await ready.Content.ReadAsStringAsync());
        Assert.True(ready.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task OpenApi_describes_health_endpoints()
    {
        await using var factory = new ApiFactory("Host=127.0.0.1;Port=1;Database=salon;Username=probe");
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains("/health/live", document);
        Assert.Contains("/health/ready", document);
        Assert.Contains("503", document);
    }
}

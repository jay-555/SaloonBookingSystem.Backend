using System.Net;
using Testcontainers.PostgreSql;
using Xunit;

namespace Salon.Api.Tests;

public sealed class DatabaseHealthTests
{
    [Fact]
    [Trait("Category", "Container")]
    public async Task Readiness_tracks_real_database_outage_and_recovery()
    {
        await using var database = new TestDatabase();
        await database.StartAsync();
        await using var factory = new ApiFactory(database.GetConnectionString() + ";Timeout=2;Pooling=false");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        await database.StopAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        await database.StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }
}

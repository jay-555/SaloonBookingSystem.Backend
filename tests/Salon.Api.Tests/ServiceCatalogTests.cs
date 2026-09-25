using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Application;
using Salon.Domain.Entities;
using Salon.Infrastructure;
using Xunit;

namespace Salon.Api.Tests;

[Trait("Category", "Container")]
public sealed class ServiceCatalogTests
{
    private const string Password = "Test-only!Password42";

    [Fact]
    public async Task Owner_crud_manager_read_isolation_and_linked_delete_conflict()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        var created = await Send(owner, "/services", new ServiceInput(" Haircut ", " Hair ", 500m, 45));
        created.EnsureSuccessStatusCode();
        var service = (await created.Content.ReadFromJsonAsync<ServiceDetail>())!;
        Assert.Equal("Haircut", service.Name);
        Assert.Equal("Hair", service.Category);
        Assert.Equal(500m, service.Price);
        Assert.Equal(45, service.DurationMinutes);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/services/{service.Id}",
            new ServiceInput("Haircut", "Hair", -1m, 45), HttpMethod.Put)).StatusCode);
        Assert.Equal(500m, (await owner.GetFromJsonAsync<ServiceDetail>($"/services/{service.Id}"))!.Price);

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, "/services", new ServiceInput("Nope", "Hair", 1m, 10))).StatusCode);

        await Provision(factory, "other", "OwnerAdmin");
        using var other = Client(factory); await Login(other, "other");
        (await Send(other, "/salon", new SalonInput("Other", "UTC", Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/services/{service.Id}")).StatusCode);
        Assert.Empty(await other.GetFromJsonAsync<ServiceSummary[]>("/services") ?? []);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var seatId = Guid.NewGuid();
            context.Seats.Add(new Seat(seatId, salon.Id, "Chair", "Chair"));
            context.SeatServices.Add(new SeatService(seatId, service.Id));
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/services/{service.Id}", new { }, HttpMethod.Delete)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            await context.SeatServices.Where(x => x.ServiceId == service.Id).ExecuteDeleteAsync();
        }
        (await Send(owner, $"/services/{service.Id}", new { }, HttpMethod.Delete)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/services/{service.Id}")).StatusCode);
    }

    private static WebApplicationFactory<Program> Factory(string connection) =>
        new ApiFactory(connection).WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Migrate(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.MigrateAsync();
    }
    private static async Task Provision(WebApplicationFactory<Program> factory, string login, string role, Guid? salonId = null)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AccountProvisioner>().Provision(login, role, salonId, Password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
    }
    private static async Task Login(HttpClient client, string login) =>
        (await Send(client, "/auth/login", new { login, password = Password })).EnsureSuccessStatusCode();
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        var token = (await client.GetFromJsonAsync<Csrf>("/auth/csrf"))!.Token;
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    private sealed record Csrf(string Token);
}

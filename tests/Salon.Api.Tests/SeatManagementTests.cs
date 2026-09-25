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
public sealed class SeatManagementTests
{
    private const string Password = "Test-only!Password42";

    [Fact]
    public async Task Owner_crud_manager_read_isolation_and_service_validation()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceId = Guid.NewGuid();
            context.Services.Add(new Service(serviceId, salon.Id, "Facial", "Skin", 800m, 60));
            await context.SaveChangesAsync();
        }

        var created = await Send(owner, "/seats", new SeatInput(" Chair A ", " Styling ", [serviceId]));
        created.EnsureSuccessStatusCode();
        var seat = (await created.Content.ReadFromJsonAsync<SeatDetail>())!;
        Assert.Equal("Chair A", seat.Name);
        Assert.Equal("Styling", seat.Type);
        Assert.Equal(serviceId, Assert.Single(seat.ServiceIds));

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/seats/{seat.Id}",
            new SeatInput("Chair A", "Styling", [Guid.NewGuid()]), HttpMethod.Put)).StatusCode);
        Assert.Equal("Chair A", (await owner.GetFromJsonAsync<SeatDetail>($"/seats/{seat.Id}"))!.Name);

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/seats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, "/seats", new SeatInput("Nope", "Chair", []))).StatusCode);

        await Provision(factory, "other", "OwnerAdmin");
        using var other = Client(factory); await Login(other, "other");
        (await Send(other, "/salon", new SalonInput("Other", "UTC", Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/seats/{seat.Id}")).StatusCode);
        Assert.Empty(await other.GetFromJsonAsync<SeatSummary[]>("/seats") ?? []);

        (await Send(owner, $"/seats/{seat.Id}", new { }, HttpMethod.Delete)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/seats/{seat.Id}")).StatusCode);
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

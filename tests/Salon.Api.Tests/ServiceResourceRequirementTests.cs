using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Application;
using Salon.Infrastructure;
using Xunit;

namespace Salon.Api.Tests;

[Trait("Category", "Container")]
public sealed class ServiceRequirementApiTests
{
    private const string Password = "Test-only!Password42";

    [Fact]
    public async Task Owner_sets_requirements_manager_read_only_and_isolation()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        var created = await Send(owner, "/services", new ServiceInput("Haircut", "Hair", 500m, 45));
        created.EnsureSuccessStatusCode();
        var service = (await created.Content.ReadFromJsonAsync<ServiceDetail>())!;
        Assert.Null(service.Requirements);

        var set = await Send(owner, $"/services/{service.Id}/requirements",
            new ResourceRequirementInput(" Chair ", 10), HttpMethod.Put);
        set.EnsureSuccessStatusCode();
        var requirement = (await set.Content.ReadFromJsonAsync<ResourceRequirementDetail>())!;
        Assert.Equal(1, requirement.EmployeeCapacity);
        Assert.Equal("Chair", requirement.SeatType);
        Assert.Equal(10, requirement.BufferMinutes);
        Assert.Equal("Chair", (await owner.GetFromJsonAsync<ServiceDetail>($"/services/{service.Id}"))!.Requirements!.SeatType);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/services/{service.Id}/requirements",
            new ResourceRequirementInput("Chair", -1), HttpMethod.Put)).StatusCode);
        Assert.Equal(10, (await owner.GetFromJsonAsync<ServiceDetail>($"/services/{service.Id}"))!.Requirements!.BufferMinutes);

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, $"/services/{service.Id}/requirements",
            new ResourceRequirementInput("Chair", 5), HttpMethod.Put)).StatusCode);

        await Provision(factory, "other", "OwnerAdmin");
        using var other = Client(factory); await Login(other, "other");
        (await Send(other, "/salon", new SalonInput("Other", "UTC", Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/services/{service.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(other, $"/services/{service.Id}/requirements",
            new ResourceRequirementInput("Chair", 0), HttpMethod.Put)).StatusCode);

        (await Send(owner, $"/services/{service.Id}/requirements", new { }, HttpMethod.Delete)).EnsureSuccessStatusCode();
        Assert.Null((await owner.GetFromJsonAsync<ServiceDetail>($"/services/{service.Id}"))!.Requirements);
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

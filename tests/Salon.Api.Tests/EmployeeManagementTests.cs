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
public sealed class EmployeeManagementTests
{
    private const string Password = "Test-only!Password42";

    private static EmployeeInput MondaySchedule(string name, Guid[] services) => new(
        name, services,
        Enumerable.Range(0, 7).Select(day => day == 1
            ? new EmployeeDayInput(1, "09:00", "18:00", [new BreakInput(1, "12:00", "13:00")])
            : new EmployeeDayInput(day, null, null, [])).ToArray());

    private static EmployeeInput ClosedEmployee(string name) => new(
        name, [],
        Enumerable.Range(0, 7).Select(day => new EmployeeDayInput(day, null, null, [])).ToArray());

    [Fact]
    public async Task Owner_crud_manager_read_isolation_and_schedule_validation()
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
            context.Services.Add(new Service(serviceId, salon.Id, "Haircut", "Hair", 500m, 45));
            await context.SaveChangesAsync();
        }

        var created = await Send(owner, "/employees", MondaySchedule(" Ava ", [serviceId]));
        created.EnsureSuccessStatusCode();
        var employee = (await created.Content.ReadFromJsonAsync<EmployeeDetail>())!;
        Assert.Equal("Ava", employee.Name);
        Assert.Equal(serviceId, Assert.Single(employee.ServiceIds));
        Assert.Equal("12:00", employee.Hours.Single(day => day.Day == 1).Breaks.Single().StartsAt);

        var invalidBreak = MondaySchedule("Ava", [serviceId]) with
        {
            Hours = Enumerable.Range(0, 7).Select(day => day == 1
                ? new EmployeeDayInput(1, "09:00", "18:00", [new BreakInput(1, "08:00", "08:30")])
                : new EmployeeDayInput(day, null, null, [])).ToArray(),
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"/employees/{employee.Id}", invalidBreak, HttpMethod.Put)).StatusCode);
        Assert.Equal("12:00", (await owner.GetFromJsonAsync<EmployeeDetail>($"/employees/{employee.Id}"))!.Hours.Single(d => d.Day == 1).Breaks.Single().StartsAt);

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, "/employees", ClosedEmployee("Nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, $"/employees/{employee.Id}", ClosedEmployee("Nope"), HttpMethod.Put)).StatusCode);

        await Provision(factory, "other", "OwnerAdmin");
        using var other = Client(factory); await Login(other, "other");
        (await Send(other, "/salon", new SalonInput("Other", "UTC", Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/employees/{employee.Id}")).StatusCode);
        Assert.Empty(await other.GetFromJsonAsync<EmployeeSummary[]>("/employees") ?? []);

        (await Send(owner, $"/employees/{employee.Id}", new { }, HttpMethod.Delete)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/employees/{employee.Id}")).StatusCode);
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

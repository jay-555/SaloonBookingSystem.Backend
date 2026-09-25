using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Application;
using Salon.Domain.Entities;
using Salon.Infrastructure;
using SalonEntity = Salon.Domain.Entities.Salon;
using Xunit;

namespace Salon.Api.Tests;

[Trait("Category", "Container")]
public sealed class EmployeeScheduleApiTests
{
    private const string Password = "Test-only!Password42";
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public async Task Admin_reads_day_and_week_schedule_with_auth_and_isolation()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid employeeId, otherSalonEmployee;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            employeeId = Guid.NewGuid();
            otherSalonEmployee = Guid.NewGuid();
            var serviceId = Guid.NewGuid();
            var seatId = Guid.NewGuid();
            var foreignSalon = Guid.NewGuid();
            context.Services.Add(new Service(serviceId, salon.Id, "Haircut", "Hair", 500m, 60));
            context.Employees.Add(new Employee(employeeId, salon.Id, "Alex"));
            context.EmployeeBreaks.Add(new EmployeeBreak(employeeId, (int)DayOfWeek.Monday, 12 * 60, 13 * 60));
            context.Seats.Add(new Seat(seatId, salon.Id, "Chair 1", "Chair"));
            var start = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
            context.Bookings.Add(new Booking(Guid.NewGuid(), salon.Id, serviceId, employeeId, seatId, start, start.AddHours(1)));
            context.EmployeeLeaves.Add(new EmployeeLeave(Guid.NewGuid(), employeeId,
                DateTimeOffset.Parse("2026-09-29T18:30:00Z"), DateTimeOffset.Parse("2026-10-01T18:30:00Z")));
            context.Salons.Add(new SalonEntity(foreignSalon, "Other", "Asia/Kolkata"));
            for (var day = 0; day < 7; day++) context.WorkingDays.Add(new WorkingDay(foreignSalon, day, null, null));
            context.Employees.Add(new Employee(otherSalonEmployee, foreignSalon, "Foreign"));
            await context.SaveChangesAsync();
        }

        var dayView = await owner.GetFromJsonAsync<EmployeeScheduleResult>(
            $"/employees/{employeeId}/schedule?date={Monday:yyyy-MM-dd}&view=day");
        Assert.Equal("day", dayView!.View);
        Assert.Contains(dayView.Items, x => x is { Kind: "booking", StartsAtLocal: "10:00", Label: "Haircut", SeatName: "Chair 1" });
        Assert.Contains(dayView.Items, x => x is { Kind: "break", StartsAtLocal: "12:00" });

        var weekView = await owner.GetFromJsonAsync<EmployeeScheduleResult>(
            $"/employees/{employeeId}/schedule?date={Monday:yyyy-MM-dd}&view=week");
        Assert.Equal(Monday, weekView!.RangeStart);
        Assert.Contains(weekView.Items, x => x.Kind == "leave" && x.Date == new DateOnly(2026, 9, 30));

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/employees/{otherSalonEmployee}/schedule?date={Monday:yyyy-MM-dd}&view=day")).StatusCode);

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/employees/{employeeId}/schedule?date={Monday:yyyy-MM-dd}&view=day")).StatusCode);

        await Provision(factory, "staff", "Employee", salon.Id);
        using var staff = Client(factory); await Login(staff, "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/employees/{employeeId}/schedule?date={Monday:yyyy-MM-dd}&view=day")).StatusCode);

        using var anon = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/employees/{employeeId}/schedule?date={Monday:yyyy-MM-dd}&view=day")).StatusCode);
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

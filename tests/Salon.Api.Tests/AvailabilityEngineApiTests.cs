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
public sealed class AvailabilityEngineApiTests
{
    private const string Password = "Test-only!Password42";
    // Section 12 fixture clocks: Asia/Kolkata Monday 2026-09-28; Haircut 60m + 0 buffer.
    private static readonly DateOnly FixtureDate = new(2026, 9, 28);

    [Fact]
    public async Task Section12_success_and_conflicts_plus_authorization()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceId, employeeId, seatId, otherEmployeeId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceId = Guid.NewGuid();
            employeeId = Guid.NewGuid();
            otherEmployeeId = Guid.NewGuid();
            seatId = Guid.NewGuid();
            context.Services.Add(new Service(serviceId, salon.Id, "Haircut", "Hair", 500m, 60));
            context.ServiceResourceRequirements.Add(new ServiceResourceRequirement(serviceId, "Chair", 0));
            context.Employees.Add(new Employee(employeeId, salon.Id, "Alex"));
            context.Employees.Add(new Employee(otherEmployeeId, salon.Id, "Blake"));
            context.EmployeeSkills.Add(new EmployeeSkill(employeeId, serviceId));
            context.EmployeeSkills.Add(new EmployeeSkill(otherEmployeeId, serviceId));
            context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(employeeId, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
            context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(otherEmployeeId, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
            context.Seats.Add(new Seat(seatId, salon.Id, "Chair 1", "Chair"));
            context.SeatServices.Add(new SeatService(seatId, serviceId));
            await context.SaveChangesAsync();
        }

        var success = await owner.GetFromJsonAsync<AvailabilityResult>(
            $"/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}&employeeId={employeeId}");
        Assert.Contains(success!.Slots, s => s.StartsAtLocal == "09:00");
        Assert.Contains(success.Slots, s => s.StartsAtLocal == "10:00");

        // Employee conflict: Alex booked 10:00–11:00 IST.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var start = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
            context.Bookings.Add(new Booking(Guid.NewGuid(), salon.Id, serviceId, employeeId, seatId, start, start.AddHours(1)));
            await context.SaveChangesAsync();
        }
        var afterEmployee = await owner.GetFromJsonAsync<AvailabilityResult>(
            $"/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}&employeeId={employeeId}");
        Assert.DoesNotContain(afterEmployee!.Slots, s => s.StartsAtLocal == "10:00");
        Assert.Contains(afterEmployee.Slots, s => s.StartsAtLocal == "09:00");

        // Clear and seed seat-only conflict: Blake holds the only Chair at 10:00; Alex is free.
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            await context.Bookings.ExecuteDeleteAsync();
            var start = DateTimeOffset.Parse("2026-09-28T04:30:00Z");
            context.Bookings.Add(new Booking(Guid.NewGuid(), salon.Id, serviceId, otherEmployeeId, seatId, start, start.AddHours(1)));
            await context.SaveChangesAsync();
        }
        var afterSeat = await owner.GetFromJsonAsync<AvailabilityResult>(
            $"/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}&employeeId={employeeId}");
        Assert.DoesNotContain(afterSeat!.Slots, s => s.StartsAtLocal == "10:00");
        Assert.Contains(afterSeat.Slots, s => s.StartsAtLocal == "09:00");

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}")).StatusCode);

        await Provision(factory, "staff", "Employee", salon.Id);
        using var staff = Client(factory); await Login(staff, "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}")).StatusCode);

        using var anon = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}")).StatusCode);
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

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
public sealed class WalkInBookingApiTests
{
    private const string Password = "Test-only!Password42";
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public async Task Staff_walk_in_create_conflict_and_authorization()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceId, employeeId, seatId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceId = Guid.NewGuid();
            employeeId = Guid.NewGuid();
            seatId = Guid.NewGuid();
            context.Services.Add(new Service(serviceId, salon.Id, "Haircut", "Hair", 500m, 60));
            context.ServiceResourceRequirements.Add(new ServiceResourceRequirement(serviceId, "Chair", 0));
            context.Employees.Add(new Employee(employeeId, salon.Id, "Alex"));
            context.EmployeeSkills.Add(new EmployeeSkill(employeeId, serviceId));
            context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(employeeId, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
            context.Seats.Add(new Seat(seatId, salon.Id, "Chair 1", "Chair"));
            context.SeatServices.Add(new SeatService(seatId, serviceId));
            await context.SaveChangesAsync();
        }

        var skilled = await owner.GetFromJsonAsync<PublicEmployeeSummary[]>($"/services/{serviceId}/employees");
        Assert.Contains(skilled!, x => x.Id == employeeId);

        var created = await (await Send(owner, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "09:00", null, "Walk In", "9876543210", null)))
            .Content.ReadFromJsonAsync<PublicBookingConfirmation>();
        Assert.Equal("Haircut", created!.ServiceName);
        Assert.Equal(1, await CountBookings(factory));

        var conflict = await Send(owner, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "09:00", null, "Other", "9876543211", null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1, await CountBookings(factory));

        using var anonPublic = Client(factory);
        var publicConflict = await Send(anonPublic, "/public/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "09:00", null, "Web", "9876543212", null));
        Assert.Equal(HttpStatusCode.Conflict, publicConflict.StatusCode);

        await Provision(factory, "manager", "Manager", salon.Id);
        using var manager = Client(factory); await Login(manager, "manager");
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "10:00", employeeId, "Mgr Guest", "9876543213", null))).StatusCode);

        await Provision(factory, "staff", "Employee", salon.Id);
        using var staff = Client(factory); await Login(staff, "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staff, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "11:00", null, "No", "9876543214", null))).StatusCode);

        using var anon = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "11:00", null, "No", "9876543215", null))).StatusCode);

        using var noCsrf = new HttpRequestMessage(HttpMethod.Post, "/bookings")
        {
            Content = JsonContent.Create(new CreatePublicBookingInput(serviceId, Monday, "11:00", null, "No", "9876543215", null)),
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(noCsrf)).StatusCode);
        Assert.Equal(2, await CountBookings(factory));

        var invalid = await Send(owner, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "11:00", null, " ", "123", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(2, await CountBookings(factory));
    }

    [Fact]
    public async Task Walk_in_stays_inside_membership_salon()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner-a", "OwnerAdmin");
        using var ownerA = Client(factory);
        await Login(ownerA, "owner-a");
        var salonA = (await (await Send(ownerA, "/salon", new SalonInput("Studio A", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        await Provision(factory, "owner-b", "OwnerAdmin");
        using var ownerB = Client(factory);
        await Login(ownerB, "owner-b");
        var salonB = (await (await Send(ownerB, "/salon", new SalonInput("Studio B", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceA, serviceB;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceA = Guid.NewGuid();
            serviceB = Guid.NewGuid();
            SeedBookable(context, salonA.Id, serviceA, Guid.NewGuid(), Guid.NewGuid(), "Cut A");
            SeedBookable(context, salonB.Id, serviceB, Guid.NewGuid(), Guid.NewGuid(), "Cut B");
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await ownerA.GetAsync($"/services/{serviceB}/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ownerA, "/bookings",
            new CreatePublicBookingInput(serviceB, Monday, "09:00", null, "Leak", "9876543210", null))).StatusCode);
        Assert.Equal(0, await CountBookings(factory));

        Assert.Equal(HttpStatusCode.OK, (await Send(ownerB, "/bookings",
            new CreatePublicBookingInput(serviceB, Monday, "09:00", null, "Guest B", "9876543211", null))).StatusCode);
        Assert.Equal(1, await CountBookings(factory));
    }

    private static void SeedBookable(SalonDbContext context, Guid salonId, Guid serviceId, Guid employeeId, Guid seatId, string name)
    {
        context.Services.Add(new Service(serviceId, salonId, name, "Hair", 500m, 60));
        context.ServiceResourceRequirements.Add(new ServiceResourceRequirement(serviceId, "Chair", 0));
        context.Employees.Add(new Employee(employeeId, salonId, "Alex"));
        context.EmployeeSkills.Add(new EmployeeSkill(employeeId, serviceId));
        context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(employeeId, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
        context.Seats.Add(new Seat(seatId, salonId, "Chair 1", "Chair"));
        context.SeatServices.Add(new SeatService(seatId, serviceId));
    }

    private static async Task<int> CountBookings(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Bookings.CountAsync();
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

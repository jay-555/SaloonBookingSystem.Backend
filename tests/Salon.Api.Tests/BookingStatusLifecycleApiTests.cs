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
public sealed class BookingStatusLifecycleApiTests
{
    private const string Password = "Test-only!Password42";
    private static readonly DateOnly Monday = new(2026, 9, 28);

    [Fact]
    public async Task Staff_advances_status_rejects_illegal_moves_and_cancel_frees_slot()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceId, employeeId, seatId, bookingId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceId = Guid.NewGuid();
            employeeId = Guid.NewGuid();
            seatId = Guid.NewGuid();
            bookingId = Guid.NewGuid();
            context.Services.Add(new Service(serviceId, salon.Id, "Haircut", "Hair", 500m, 60));
            context.ServiceResourceRequirements.Add(new ServiceResourceRequirement(serviceId, "Chair", 0));
            context.Employees.Add(new Employee(employeeId, salon.Id, "Alex"));
            context.EmployeeSkills.Add(new EmployeeSkill(employeeId, serviceId));
            context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(employeeId, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
            context.Seats.Add(new Seat(seatId, salon.Id, "Chair 1", "Chair"));
            context.SeatServices.Add(new SeatService(seatId, serviceId));
            var start = new DateTimeOffset(2026, 9, 28, 4, 30, 0, TimeSpan.Zero);
            context.Bookings.Add(new Booking(bookingId, salon.Id, serviceId, employeeId, seatId, start, start.AddHours(1)));
            await context.SaveChangesAsync();
        }

        var detail = await owner.GetFromJsonAsync<StaffBookingDetail>($"/bookings/{bookingId}");
        Assert.Equal(BookingStatuses.Confirmed, detail!.Status);
        Assert.Contains(BookingStatuses.CheckedIn, detail.AllowedNextStatuses);

        var checkedIn = await (await Send(owner, $"/bookings/{bookingId}/status",
            new TransitionBookingInput(BookingStatuses.CheckedIn))).Content.ReadFromJsonAsync<StaffBookingDetail>();
        Assert.Equal(BookingStatuses.CheckedIn, checkedIn!.Status);

        var illegal = await Send(owner, $"/bookings/{bookingId}/status",
            new TransitionBookingInput(BookingStatuses.Completed));
        Assert.Equal(HttpStatusCode.Conflict, illegal.StatusCode);

        var inService = await (await Send(owner, $"/bookings/{bookingId}/status",
            new TransitionBookingInput(BookingStatuses.InService))).Content.ReadFromJsonAsync<StaffBookingDetail>();
        Assert.Equal(BookingStatuses.InService, inService!.Status);

        var completed = await (await Send(owner, $"/bookings/{bookingId}/status",
            new TransitionBookingInput(BookingStatuses.Completed))).Content.ReadFromJsonAsync<StaffBookingDetail>();
        Assert.Equal(BookingStatuses.Completed, completed!.Status);
        Assert.Empty(completed.AllowedNextStatuses);

        // Fresh booking for cancel → frees slot for walk-in.
        Guid cancelId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            cancelId = Guid.NewGuid();
            var start = new DateTimeOffset(2026, 9, 28, 5, 30, 0, TimeSpan.Zero); // 11:00 IST
            context.Bookings.Add(new Booking(cancelId, salon.Id, serviceId, employeeId, seatId, start, start.AddHours(1)));
            await context.SaveChangesAsync();
        }

        var cancelled = await (await Send(owner, $"/bookings/{cancelId}/status",
            new TransitionBookingInput(BookingStatuses.Cancelled))).Content.ReadFromJsonAsync<StaffBookingDetail>();
        Assert.Equal(BookingStatuses.Cancelled, cancelled!.Status);
        Assert.True(cancelled.Cancelled);

        Assert.Equal(HttpStatusCode.OK, (await Send(owner, "/bookings",
            new CreatePublicBookingInput(serviceId, Monday, "11:00", null, "Walk In", "9876543210", null))).StatusCode);

        Assert.True(await CountStatusAudits(factory) >= 4);

        await Provision(factory, "staff", "Employee", salon.Id);
        using var staff = Client(factory); await Login(staff, "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staff, $"/bookings/{bookingId}/status",
            new TransitionBookingInput(BookingStatuses.CheckedIn))).StatusCode);

        using var anon = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anon, $"/bookings/{bookingId}/status",
            new TransitionBookingInput(BookingStatuses.CheckedIn))).StatusCode);
    }

    [Fact]
    public async Task Status_transition_stays_inside_membership_salon()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner-a", "OwnerAdmin");
        using var ownerA = Client(factory); await Login(ownerA, "owner-a");
        var salonA = (await (await Send(ownerA, "/salon", new SalonInput("A", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        await Provision(factory, "owner-b", "OwnerAdmin");
        using var ownerB = Client(factory); await Login(ownerB, "owner-b");
        var salonB = (await (await Send(ownerB, "/salon", new SalonInput("B", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid bookingB;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var serviceB = Guid.NewGuid();
            var employeeB = Guid.NewGuid();
            var seatB = Guid.NewGuid();
            bookingB = Guid.NewGuid();
            context.Services.Add(new Service(serviceB, salonB.Id, "Cut B", "Hair", 500m, 60));
            context.ServiceResourceRequirements.Add(new ServiceResourceRequirement(serviceB, "Chair", 0));
            context.Employees.Add(new Employee(employeeB, salonB.Id, "Alex"));
            context.EmployeeSkills.Add(new EmployeeSkill(employeeB, serviceB));
            context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(employeeB, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
            context.Seats.Add(new Seat(seatB, salonB.Id, "Chair 1", "Chair"));
            context.SeatServices.Add(new SeatService(seatB, serviceB));
            var start = new DateTimeOffset(2026, 9, 28, 4, 30, 0, TimeSpan.Zero);
            context.Bookings.Add(new Booking(bookingB, salonB.Id, serviceB, employeeB, seatB, start, start.AddHours(1)));
            await context.SaveChangesAsync();
        }

        _ = salonA;
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ownerA, $"/bookings/{bookingB}/status",
            new TransitionBookingInput(BookingStatuses.CheckedIn))).StatusCode);
    }

    private static async Task<int> CountStatusAudits(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().BookingAudits
            .CountAsync(x => x.Operation == BookingAuditOperations.StatusChange || x.Operation == BookingAuditOperations.Cancel);
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

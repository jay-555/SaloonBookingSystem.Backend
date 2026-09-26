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
public sealed class CustomerCrmApiTests
{
    private const string Password = "Test-only!Password42";

    [Fact]
    public async Task Staff_search_profile_totals_isolation_and_authorization()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceId, employeeId, seatId, customerId, bookingPastId, bookingCancelId, bookingFutureId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceId = Guid.NewGuid();
            employeeId = Guid.NewGuid();
            seatId = Guid.NewGuid();
            customerId = Guid.NewGuid();
            bookingPastId = Guid.NewGuid();
            bookingCancelId = Guid.NewGuid();
            bookingFutureId = Guid.NewGuid();
            context.Services.Add(new Service(serviceId, salon.Id, "Haircut", "Hair", 500m, 60));
            context.ServiceResourceRequirements.Add(new ServiceResourceRequirement(serviceId, "Chair", 0));
            context.Employees.Add(new Employee(employeeId, salon.Id, "Alex"));
            context.EmployeeSkills.Add(new EmployeeSkill(employeeId, serviceId));
            context.EmployeeWorkingDays.Add(new EmployeeWorkingDay(employeeId, (int)DayOfWeek.Monday, 9 * 60, 18 * 60));
            context.Seats.Add(new Seat(seatId, salon.Id, "Chair 1", "Chair"));
            context.SeatServices.Add(new SeatService(seatId, serviceId));
            context.Customers.Add(new Customer(customerId, salon.Id, "Priya Sharma", "9876543210", "priya@example.com"));
            var past = new DateTimeOffset(2026, 9, 28, 4, 30, 0, TimeSpan.Zero);
            var future = DateTimeOffset.UtcNow.AddDays(3);
            context.Bookings.Add(new Booking(bookingPastId, salon.Id, serviceId, employeeId, seatId, past, past.AddHours(1), customerId, BookingStatuses.Completed));
            var cancel = new Booking(bookingCancelId, salon.Id, serviceId, employeeId, seatId, past.AddHours(2), past.AddHours(3), customerId);
            cancel.Cancel(DateTimeOffset.UtcNow);
            context.Bookings.Add(cancel);
            context.Bookings.Add(new Booking(bookingFutureId, salon.Id, serviceId, employeeId, seatId, future, future.AddHours(1), customerId, BookingStatuses.Confirmed));
            await context.SaveChangesAsync();
        }

        var empty = await owner.GetFromJsonAsync<CustomerSearchItem[]>("/customers?q=a");
        Assert.Empty(empty!);

        var byName = await owner.GetFromJsonAsync<CustomerSearchItem[]>("/customers?q=Priya");
        Assert.Contains(byName!, x => x.Id == customerId);

        var byPhone = await owner.GetFromJsonAsync<CustomerSearchItem[]>("/customers?q=9876543210");
        Assert.Contains(byPhone!, x => x.Id == customerId);

        var profile = await owner.GetFromJsonAsync<CustomerProfile>($"/customers/{customerId}");
        Assert.Equal("Priya Sharma", profile!.Name);
        Assert.Equal(2, profile.VisitCount); // completed + future confirmed; cancelled excluded
        Assert.Equal(1000m, profile.TotalSpend);
        Assert.NotNull(profile.LastVisit);
        Assert.Equal(bookingPastId, profile.LastVisit!.BookingId);
        Assert.NotNull(profile.Upcoming);
        Assert.Equal(bookingFutureId, profile.Upcoming!.BookingId);
        Assert.Equal(3, profile.History.Count);
        Assert.Contains(profile.History, x => x.Status == BookingStatuses.Cancelled);
        Assert.Contains(profile.ServiceSummary, x => x.ServiceName == "Haircut" && x.Count == 2);

        await Provision(factory, "staff", "Employee", salon.Id);
        using var staff = Client(factory); await Login(staff, "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/customers?q=Priya")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/customers/{customerId}")).StatusCode);

        using var anon = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/customers?q=Priya")).StatusCode);
    }

    [Fact]
    public async Task Customer_profile_stays_inside_membership_salon()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner-a", "OwnerAdmin");
        using var ownerA = Client(factory); await Login(ownerA, "owner-a");
        _ = (await (await Send(ownerA, "/salon", new SalonInput("A", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        await Provision(factory, "owner-b", "OwnerAdmin");
        using var ownerB = Client(factory); await Login(ownerB, "owner-b");
        var salonB = (await (await Send(ownerB, "/salon", new SalonInput("B", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(d => new DayInput(d, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid customerB;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            customerB = Guid.NewGuid();
            context.Customers.Add(new Customer(customerB, salonB.Id, "Other Guest", "9123456789", null));
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await ownerA.GetAsync($"/customers/{customerB}")).StatusCode);
        Assert.DoesNotContain((await ownerA.GetFromJsonAsync<CustomerSearchItem[]>("/customers?q=Other"))!, x => x.Id == customerB);
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

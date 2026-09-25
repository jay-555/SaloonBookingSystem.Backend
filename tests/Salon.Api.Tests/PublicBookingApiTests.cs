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
public sealed class PublicBookingApiTests
{
    private const string Password = "Test-only!Password42";
    private static readonly DateOnly FixtureDate = new(2026, 9, 28);

    [Fact]
    public async Task Anonymous_create_conflict_csrf_and_validation()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        await Login(owner, "owner");
        var salon = (await (await Send(owner, "/salon", new SalonInput("Studio", "Asia/Kolkata",
            Enumerable.Range(0, 7).Select(day => new DayInput(day, null, null)).ToArray()))).Content.ReadFromJsonAsync<SalonProfile>())!;

        Guid serviceId, employeeId, seatId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            serviceId = Guid.NewGuid();
            employeeId = Guid.NewGuid();
            seatId = Guid.NewGuid();
            SeedBookable(context, salon.Id, serviceId, employeeId, seatId, "Haircut");
            await context.SaveChangesAsync();
        }

        using var anon = Client(factory);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/public/salon")).StatusCode);
        var services = await anon.GetFromJsonAsync<PublicServiceSummary[]>("/public/services");
        Assert.Contains(services!, s => s.Id == serviceId);

        var slots = await anon.GetFromJsonAsync<AvailabilityResult>(
            $"/public/availability?serviceId={serviceId}&date={FixtureDate:yyyy-MM-dd}");
        Assert.Contains(slots!.Slots, s => s.StartsAtLocal == "09:00");

        using var noCsrf = new HttpRequestMessage(HttpMethod.Post, "/public/bookings")
        {
            Content = JsonContent.Create(new CreatePublicBookingInput(serviceId, FixtureDate, "09:00", null, "Priya", "9876543210", null)),
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.SendAsync(noCsrf)).StatusCode);
        Assert.Equal(0, await CountBookings(factory));

        var created = await (await Send(anon, "/public/bookings",
            new CreatePublicBookingInput(serviceId, FixtureDate, "09:00", null, "Priya", "9876543210", "priya@example.com")))
            .Content.ReadFromJsonAsync<PublicBookingConfirmation>();
        Assert.Equal("Haircut", created!.ServiceName);
        Assert.Equal("Priya", created.CustomerName);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var booking = await context.Bookings.SingleAsync(x => x.Id == created.BookingId);
            Assert.Equal(serviceId, booking.ServiceId);
            Assert.NotNull(booking.CustomerId);
            var customer = await context.Customers.SingleAsync(x => x.Id == booking.CustomerId);
            Assert.Equal("9876543210", customer.Phone);
            Assert.Equal(TimeSpan.FromHours(1), booking.EndsAtUtc - booking.StartsAtUtc);
        }

        var conflict = await Send(anon, "/public/bookings",
            new CreatePublicBookingInput(serviceId, FixtureDate, "09:00", null, "Other", "9876543211", null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1, await CountBookings(factory));

        var invalid = await Send(anon, "/public/bookings",
            new CreatePublicBookingInput(serviceId, FixtureDate, "10:00", null, " ", "123", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(1, await CountBookings(factory));
    }

    [Fact]
    public async Task PublicSalonId_config_targets_named_salon_only()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        var firstSalon = Guid.NewGuid();
        var secondSalon = Guid.NewGuid();
        await using var factory = new ApiFactory(db.GetConnectionString()).WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Booking:PublicSalonId", secondSalon.ToString());
        });
        await Migrate(factory);

        Guid firstService, secondService;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            firstService = Guid.NewGuid();
            secondService = Guid.NewGuid();
            context.Salons.Add(new SalonEntity(firstSalon, "First", "Asia/Kolkata"));
            context.Salons.Add(new SalonEntity(secondSalon, "Second", "Asia/Kolkata"));
            for (var day = 0; day < 7; day++)
            {
                context.WorkingDays.Add(new WorkingDay(firstSalon, day, null, null));
                context.WorkingDays.Add(new WorkingDay(secondSalon, day, null, null));
            }
            SeedBookable(context, firstSalon, firstService, Guid.NewGuid(), Guid.NewGuid(), "First cut");
            SeedBookable(context, secondSalon, secondService, Guid.NewGuid(), Guid.NewGuid(), "Second cut");
            await context.SaveChangesAsync();
        }

        using var anon = Client(factory);
        var salon = await anon.GetFromJsonAsync<PublicSalonInfo>("/public/salon");
        Assert.Equal(secondSalon, salon!.Id);
        var services = await anon.GetFromJsonAsync<PublicServiceSummary[]>("/public/services");
        Assert.NotNull(services);
        Assert.Single(services);
        Assert.Equal(secondService, services[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/public/services/{firstService}/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anon.GetAsync($"/public/availability?serviceId={firstService}&date={FixtureDate:yyyy-MM-dd}")).StatusCode);
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

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Salon.Application;
using Salon.Infrastructure;
using Xunit;

namespace Salon.Api.Tests;

[Trait("Category", "Container")]
public sealed class AuthSalonTests
{
    private const string Password = "Test-only!Password42";
    private static SalonInput Profile(string name = "Studio") => new(name, "Asia/Kolkata",
        Enumerable.Range(0, 7).Select(day => new DayInput(day, day == 1 ? "09:00" : null, day == 1 ? "18:00" : null)).ToArray());

    [Fact]
    public async Task Login_setup_edit_roles_isolation_csrf_and_logout_work_end_to_end()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString());
        await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var owner = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/auth/login", new { login = "owner", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(owner, "/auth/login", new { login = "owner", password = "wrong" })).StatusCode);
        await Login(owner, "owner");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/salon")).StatusCode);
        var created = await Send(owner, "/salon", Profile()); created.EnsureSuccessStatusCode();
        var salon = (await created.Content.ReadFromJsonAsync<SalonProfile>())!;
        Assert.Equal(7, salon.Hours.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, "/salon", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/salon", Profile("CSRF bypass"))).StatusCode);
        using (var tampered = new HttpRequestMessage(HttpMethod.Put, "/salon") { Content = JsonContent.Create(Profile("Tampered")) })
        {
            tampered.Headers.Add("X-CSRF-TOKEN", "invalid-token");
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(tampered)).StatusCode);
        }
        var invalid = Profile("Changed") with { TimeZoneId = "invalid/zone" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, "/salon", invalid, HttpMethod.Put)).StatusCode);
        Assert.Equal("Studio", (await owner.GetFromJsonAsync<SalonProfile>("/salon"))!.Name);
        (await Send(owner, "/salon", Profile(" Updated "), HttpMethod.Put)).EnsureSuccessStatusCode();
        Assert.Equal("Updated", (await owner.GetFromJsonAsync<SalonProfile>("/salon"))!.Name);

        foreach (var role in new[] { "Manager", "Employee" })
        {
            await Provision(factory, role, role, salon.Id);
            using var staff = Client(factory); await Login(staff, role);
            Assert.Equal(salon.Id, (await staff.GetFromJsonAsync<SalonProfile>("/salon"))!.Id);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(staff, "/salon", Profile("Unauthorized"), HttpMethod.Put)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(staff, "/salon", Profile())).StatusCode);
        }
        await Provision(factory, "other", "OwnerAdmin");
        using var other = Client(factory); await Login(other, "other");
        (await Send(other, "/salon", Profile("Other salon"))).EnsureSuccessStatusCode();
        other.DefaultRequestHeaders.Add("X-Salon-Id", salon.Id.ToString());
        (await Send(other, "/salon", new { name = "Only own salon", timeZoneId = "UTC", hours = Profile().Hours, id = salon.Id, role = "OwnerAdmin", salonId = salon.Id }, HttpMethod.Put)).EnsureSuccessStatusCode();
        Assert.Equal("Updated", (await owner.GetFromJsonAsync<SalonProfile>("/salon"))!.Name);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/salon/{salon.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(owner, "/auth/register", new { })).StatusCode);
        (await Send(owner, "/auth/logout", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_setup_and_safe_provisioning_keep_membership_consistent()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString()); await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var first = Client(factory); using var second = Client(factory);
        await Login(first, "owner"); await Login(second, "owner");
        var results = await Task.WhenAll(Send(first, "/salon", Profile()), Send(second, "/salon", Profile()));
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        Assert.Equal(1, await context.Salons.CountAsync());
        Assert.Equal(7, await context.WorkingDays.CountAsync());
        var provisioning = scope.ServiceProvider.GetRequiredService<AccountProvisioner>();
        Assert.False((await provisioning.Provision("OWNER", "OwnerAdmin", null, Password)).Succeeded);
        Assert.False((await provisioning.Provision("staff", "Manager", null, Password)).Succeeded);
        Assert.False((await provisioning.Provision("staff", "Manager", Guid.NewGuid(), Password)).Succeeded);
        Assert.False((await provisioning.Provision("bad", "SuperAdmin", null, Password)).Succeeded);
        Assert.False((await provisioning.Provision("weak", "OwnerAdmin", null, "weak")).Succeeded);
        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task Cookies_have_absolute_expiry_and_failed_logins_lock_out()
    {
        var clock = new TestClock();
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString()).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock)));
        await Migrate(factory); await Provision(factory, "owner", "OwnerAdmin");
        using var client = Client(factory);
        var login = await Send(client, "/auth/login", new { login = "owner", password = Password });
        login.EnsureSuccessStatusCode();
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("salon.session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        clock.Advance(TimeSpan.FromHours(7));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/me")).StatusCode);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        using var locked = Client(factory);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Send(locked, "/auth/login", new { login = "owner", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(locked, "/auth/login", new { login = "owner", password = Password })).StatusCode);
    }

    [Fact]
    public async Task Migration_preserves_phase_one_and_constraints_survive_rollback_reapply()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var context = new SalonDbContext(new DbContextOptionsBuilder<SalonDbContext>().UseNpgsql(db.GetConnectionString()).Options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260923172137_InitialCoreDomain");
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Salons\" (\"Id\", \"Name\", \"TimeZoneId\") VALUES ({id}, 'Existing', 'Asia/Kolkata')");
        context.Employees.Add(new Salon.Domain.Entities.Employee(Guid.NewGuid(), id, "Existing employee"));
        context.Seats.Add(new Salon.Domain.Entities.Seat(Guid.NewGuid(), id, "Existing chair", "Styling"));
        context.Services.Add(new Salon.Domain.Entities.Service(Guid.NewGuid(), id, "Existing cut", "Hair", 500.25m, 45));
        await context.SaveChangesAsync();
        await context.Database.MigrateAsync(); await context.Database.MigrateAsync();
        Assert.Equal("Existing employee", (await context.Employees.SingleAsync()).Name);
        Assert.Equal("Existing chair", (await context.Seats.SingleAsync()).Name);
        Assert.Equal(500.25m, (await context.Services.SingleAsync()).Price);
        Assert.Equal(7, await context.WorkingDays.CountAsync()); Assert.Empty(await context.Users.ToListAsync());
        Assert.Equal("Existing", (await context.Salons.SingleAsync()).Name);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE \"WorkingDays\" SET \"OpensAt\" = 100, \"ClosesAt\" = 90"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
        var orphan = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WorkingDays\" SET \"SalonId\" = {Guid.NewGuid()}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, orphan.SqlState);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"WorkingDays\" (\"SalonId\", \"Day\") VALUES ({id}, 0)"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        await migrator.MigrateAsync("20260923172137_InitialCoreDomain");
        Assert.Equal("Existing", (await context.Salons.SingleAsync()).Name);
        await context.Database.MigrateAsync(); Assert.Equal(7, await context.WorkingDays.CountAsync());
        await using var factory = Factory(db.GetConnectionString());
        await Provision(factory, "existing-owner", "OwnerAdmin", id);
        using var client = Client(factory); await Login(client, "existing-owner");
        Assert.Equal(id, (await client.GetFromJsonAsync<SalonProfile>("/salon"))!.Id);
        var badMember = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"SalonId\" = {Guid.NewGuid()}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, badMember.SqlState);
        var missingMember = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE \"AspNetUsers\" SET \"Role\" = 'Manager', \"SalonId\" = NULL"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, missingMember.SqlState);
    }

    [Fact]
    public async Task Pretracked_identity_cannot_repeat_setup_or_overwrite_new_membership()
    {
        await using var db = new TestDatabase(); await db.StartAsync();
        await using var factory = Factory(db.GetConnectionString()); await Migrate(factory);
        await Provision(factory, "owner", "OwnerAdmin");
        using var scope1 = factory.Services.CreateScope();
        using var scope2 = factory.Services.CreateScope();
        var first = scope1.ServiceProvider.GetRequiredService<SalonDbContext>();
        var second = scope2.ServiceProvider.GetRequiredService<SalonDbContext>();
        var user1 = await first.Users.SingleAsync();
        var staleUser = await second.Users.SingleAsync();
        await scope1.ServiceProvider.GetRequiredService<ISalonProfiles>().Save(user1.Id, Profile(), true, default);
        // Identity's optimistic concurrency must reject a pre-setup user update.
        var result = await scope2.ServiceProvider.GetRequiredService<UserManager<SalonUser>>().UpdateAsync(staleUser);
        Assert.False(result.Succeeded);
        await Assert.ThrowsAsync<SetupConflictException>(() => scope2.ServiceProvider.GetRequiredService<ISalonProfiles>().Save(staleUser.Id, Profile(), true, default));
        Assert.Single(await first.Salons.AsNoTracking().ToListAsync());
    }
    private static WebApplicationFactory<Program> Factory(string connection) =>
        new ApiFactory(connection).WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
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
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}

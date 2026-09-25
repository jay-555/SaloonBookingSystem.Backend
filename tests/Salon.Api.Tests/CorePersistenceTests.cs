using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Salon.Domain.Entities;
using Salon.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;
using SalonEntity = Salon.Domain.Entities.Salon;

namespace Salon.Api.Tests;

[Trait("Category", "Container")]
public sealed class CorePersistenceTests
{
    [Fact]
    public async Task Explicit_migration_round_trips_entities_is_repeatable_and_can_roll_back()
    {
        await using var postgres = new TestDatabase();
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        await using var database = CreateContext(connection);

        // API startup and health must not apply business migrations.
        await using (var factory = new ApiFactory(connection))
        {
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(0, await BusinessTableCount(database));
        }

        await database.Database.MigrateAsync();
        Assert.Equal(4, await BusinessTableCount(database));
        var salon = new SalonEntity(Guid.NewGuid(), " Studio ", "Europe/London");
        var employee = new Employee(Guid.NewGuid(), salon.Id, " Jay ");
        var seat = new Seat(Guid.NewGuid(), salon.Id, " Chair 1 ", " Styling ");
        var service = new Service(Guid.NewGuid(), salon.Id, " Haircut ", " Hair ", 500.25m, 45);
        database.AddRange(salon, employee, seat, service);
        database.Services.Add(new Service(Guid.NewGuid(), salon.Id, "Free consultation", "Consultation", 0m, 1));
        database.Services.Add(new Service(Guid.NewGuid(), salon.Id, "Numeric boundary", "Test", decimal.MaxValue, 1));
        await database.SaveChangesAsync();
        await database.Database.MigrateAsync();
        Assert.Equal(2, (await database.Database.GetAppliedMigrationsAsync()).Count());

        await using (var fresh = CreateContext(connection))
        {
            var storedSalon = await fresh.Salons.SingleAsync();
            Assert.Equal(salon.Id, storedSalon.Id);
            Assert.Equal("Studio", storedSalon.Name);
            Assert.Equal("Europe/London", storedSalon.TimeZoneId);
            var storedEmployee = await fresh.Employees.SingleAsync();
            Assert.Equal(employee.Id, storedEmployee.Id);
            Assert.Equal(salon.Id, storedEmployee.SalonId);
            Assert.Equal("Jay", storedEmployee.Name);
            var storedSeat = await fresh.Seats.SingleAsync();
            Assert.Equal(seat.Id, storedSeat.Id);
            Assert.Equal(salon.Id, storedSeat.SalonId);
            Assert.Equal("Chair 1", storedSeat.Name);
            Assert.Equal("Styling", storedSeat.Type);
            var storedService = await fresh.Services.SingleAsync(item => item.Id == service.Id);
            Assert.Equal(salon.Id, storedService.SalonId);
            Assert.Equal("Haircut", storedService.Name);
            Assert.Equal("Hair", storedService.Category);
            Assert.Equal(500.25m, storedService.Price);
            Assert.Equal(45, storedService.DurationMinutes);
            Assert.Equal(3, await fresh.Services.CountAsync());
            Assert.Equal(decimal.MaxValue, await fresh.Services.MaxAsync(item => item.Price));
        }

        await database.GetService<IMigrator>().MigrateAsync("0");
        Assert.Equal(0, await BusinessTableCount(database));
        Assert.Empty(await database.Database.GetAppliedMigrationsAsync());
        await database.Database.MigrateAsync();
        Assert.Equal(4, await BusinessTableCount(database));
        Assert.Empty(await database.Salons.ToListAsync());
    }

    [Fact]
    public async Task Direct_database_writes_cannot_bypass_invariants_or_delete_owned_records()
    {
        await using var postgres = new TestDatabase();
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        await using var database = CreateContext(connection);
        await database.Database.MigrateAsync();
        var salonId = Guid.NewGuid();
        database.Salons.Add(new SalonEntity(salonId, "Studio"));
        await database.SaveChangesAsync();

        foreach (var table in new[] { "Salons", "Employees", "Seats", "Services" })
        {
            await RejectInsert(database, table, salonId, "Id", Guid.Empty, PostgresErrorCodes.CheckViolation);
            foreach (var blank in new[] { "", " \t\r\n", "\u00a0\u2003" })
                await RejectInsert(database, table, salonId, "Name", blank, PostgresErrorCodes.CheckViolation);
            await RejectInsert(database, table, salonId, "Name", DBNull.Value, PostgresErrorCodes.NotNullViolation);
        }
        foreach (var field in new[] { ("Salons", "TimeZoneId"), ("Seats", "Type"), ("Services", "Category") })
        {
            await RejectInsert(database, field.Item1, salonId, field.Item2, " \t", PostgresErrorCodes.CheckViolation);
            await RejectInsert(database, field.Item1, salonId, field.Item2, DBNull.Value, PostgresErrorCodes.NotNullViolation);
        }
        foreach (var price in new[] { -1m, 0.001m, 1.005m })
            await RejectInsert(database, "Services", salonId, "Price", price, PostgresErrorCodes.CheckViolation);
        foreach (var minutes in new[] { 0, -1 })
            await RejectInsert(database, "Services", salonId, "DurationMinutes", minutes, PostgresErrorCodes.CheckViolation);
        foreach (var special in new[] { "NaN", "Infinity", "-Infinity", "79228162514264337593543950336", "79228162514264337593543950334.01" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Services\" (\"Id\", \"SalonId\", \"Name\", \"Category\", \"Price\", \"DurationMinutes\") VALUES ({Guid.NewGuid()}, {salonId}, 'Cut', 'Hair', CAST({special} AS numeric), 1)"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }

        foreach (var table in new[] { "Employees", "Seats", "Services" })
        {
            await RejectInsert(database, table, salonId, "SalonId", Guid.NewGuid(), PostgresErrorCodes.ForeignKeyViolation);
            await RejectInsert(database, table, salonId, "SalonId", Guid.Empty, PostgresErrorCodes.ForeignKeyViolation);
            await RejectInsert(database, table, salonId, "SalonId", DBNull.Value, PostgresErrorCodes.NotNullViolation);

            // Test each relationship alone so one FK cannot hide another's cascade.
            await Insert(database, table, salonId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"Salons\" WHERE \"Id\" = {salonId}"));
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.RestrictViolation });
            await using var fresh = CreateContext(connection);
            Assert.True(await fresh.Salons.AnyAsync(item => item.Id == salonId));
            var countSql = $"SELECT count(*)::int AS \"Value\" FROM \"{table}\"";
            Assert.Equal(1, await fresh.Database.SqlQueryRaw<int>(countSql).SingleAsync());
            var deleteSql = $"DELETE FROM \"{table}\"";
            await database.Database.ExecuteSqlRawAsync(deleteSql);
        }
        Assert.Equal(1, await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Salons\" WHERE \"Id\" = {salonId}"));
    }

    private static SalonDbContext CreateContext(string connection) =>
        new(new DbContextOptionsBuilder<SalonDbContext>().UseNpgsql(connection).Options);

    private static Task<int> BusinessTableCount(SalonDbContext database) => database.Database.SqlQueryRaw<int>(
        "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('Salons', 'Employees', 'Seats', 'Services')").SingleAsync();

    private static async Task RejectInsert(SalonDbContext database, string table, Guid salonId, string column, object value, string sqlState)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Insert(database, table, salonId, column, value));
        Assert.Equal(sqlState, error.SqlState);
    }

    private static Task<int> Insert(SalonDbContext database, string table, Guid salonId, string? column = null, object? value = null)
    {
        // Identifiers are hard-coded test cases, never user input; values are parameters.
        var row = new Dictionary<string, object> { ["Id"] = Guid.NewGuid(), ["Name"] = "Test" };
        if (table == "Salons") row["TimeZoneId"] = "Asia/Kolkata";
        else row["SalonId"] = salonId;
        if (table == "Seats") row["Type"] = "Styling";
        if (table == "Services")
        {
            row["Category"] = "Hair";
            row["Price"] = 0m;
            row["DurationMinutes"] = 1;
        }
        if (column is not null) row[column] = value!;
        var columns = string.Join(", ", row.Keys.Select(key => $"\"{key}\""));
        var values = new List<object>();
        var parameters = string.Join(", ", row.Values.Select(item =>
        {
            if (item is DBNull) return "NULL";
            values.Add(item);
            return "{" + (values.Count - 1) + "}";
        }));
        var sql = $"INSERT INTO \"{table}\" ({columns}) VALUES ({parameters})";
        return database.Database.ExecuteSqlRawAsync(sql, values.ToArray());
    }
}

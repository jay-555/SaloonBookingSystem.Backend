using Npgsql;
using Testcontainers.PostgreSql;

namespace Salon.Api.Tests;

// CI uses a container by default. A local disposable PostgreSQL server can be used
// without Docker; every test still receives a new, isolated database.
public sealed class TestDatabase : IAsyncDisposable
{
    private PostgreSqlContainer? container;
    private string? connection;
    private string? admin;
    private readonly string name = "salon_test_" + Guid.NewGuid().ToString("N");
    public async Task StartAsync()
    {
        if (connection is not null)
        {
            if (container is not null) await container.StartAsync();
            else await SetAvailable(true);
            return;
        }
        admin = Environment.GetEnvironmentVariable("SALON_TEST_POSTGRES");
        if (admin is null)
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync();
            connection = container.GetConnectionString();
            return;
        }
        await using var db = new NpgsqlConnection(admin);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", db);
        await command.ExecuteNonQueryAsync();
        connection = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
    }
    public async Task StopAsync()
    {
        if (container is not null) await container.StopAsync();
        else await SetAvailable(false);
    }
    private async Task SetAvailable(bool available)
    {
        await using var db = new NpgsqlConnection(admin);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand($"ALTER DATABASE \"{name}\" ALLOW_CONNECTIONS {(available ? "true" : "false")}", db);
        await command.ExecuteNonQueryAsync();
        if (!available)
        {
            await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name", db);
            terminate.Parameters.AddWithValue("name", name);
            await terminate.ExecuteNonQueryAsync();
        }
    }
    public string GetConnectionString() => connection ?? throw new InvalidOperationException("Start the test database first.");
    public async ValueTask DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
        else if (admin is not null && connection is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new NpgsqlConnection(admin);
            await db.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", db);
            await command.ExecuteNonQueryAsync();
        }
    }
}

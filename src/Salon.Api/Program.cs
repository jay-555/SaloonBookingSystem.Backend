using Microsoft.EntityFrameworkCore;
using Salon.Application;
using Salon.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SalonDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connection = configuration.GetConnectionString("Salon")
        ?? throw new InvalidOperationException("Configure ConnectionStrings__Salon.");
    options.UseNpgsql(connection, postgres => postgres.CommandTimeout(3));
});
builder.Services.AddScoped<IDatabaseReadiness, DatabaseReadiness>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
        .AllowAnyHeader().WithMethods("GET")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health/live", () => Results.Ok(new HealthStatus("healthy")))
    .WithName("Liveness").WithSummary("API process liveness; independent of PostgreSQL.")
    .Produces<HealthStatus>();

app.MapGet("/health/ready", async (IDatabaseReadiness database, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(4));
    try
    {
        if (await database.CanConnectAsync(timeout.Token))
        {
            return Results.Ok(new HealthStatus("healthy"));
        }
    }
    catch (Exception)
    {
        // Dependency failure is a readiness result. Never return connection details.
    }

    return Results.Json(new HealthStatus("unavailable"), statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithName("Readiness").WithSummary("Readiness based on an actual PostgreSQL connection.")
    .Produces<HealthStatus>().Produces<HealthStatus>(StatusCodes.Status503ServiceUnavailable);

app.Run();

public sealed record HealthStatus(string Status);
public partial class Program;

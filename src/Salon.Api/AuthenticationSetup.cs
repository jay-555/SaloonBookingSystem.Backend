using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Salon.Application;
using Salon.Infrastructure;

namespace Salon.Api;

public static class AuthenticationSetup
{
    public static void AddSalonAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityCore<SalonUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        }).AddEntityFrameworkStores<SalonDbContext>().AddSignInManager();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "salon.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                context.ShouldRenew = false; // Preserve the original absolute expiry.
            };
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "salon.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        var protection = builder.Services.AddDataProtection().SetApplicationName("SalonBookingSystem");
        if (builder.Configuration["DataProtection:KeyPath"] is string path)
            protection.PersistKeysToFileSystem(new DirectoryInfo(path));
        builder.Services.AddScoped<ISalonProfiles, SalonProfiles>();
        builder.Services.AddScoped<IEmployeeDirectory, EmployeeDirectory>();
        builder.Services.AddScoped<ISeatDirectory, SeatDirectory>();
        builder.Services.AddScoped<IServiceCatalog, ServiceCatalog>();
        builder.Services.AddScoped<AccountProvisioner>();
    }

    public static void MapSalonEndpoints(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (IsProtectedApi(context.Request.Path))
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                {
                    try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                    catch (AntiforgeryValidationException)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsJsonAsync(new { error = "Refresh the page and try again." });
                        return;
                    }
                }
            }
            await next(context);
        });
        app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapPost("/auth/login", async (LoginInput input, SignInManager<SalonUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(input.Login) || string.IsNullOrEmpty(input.Password)) return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(input.Login.Trim(), input.Password, false, lockoutOnFailure: true);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        app.MapPost("/auth/logout", async (SignInManager<SalonUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/auth/me", async (HttpContext context, ISalonProfiles profiles, CancellationToken ct) =>
        {
            var account = await profiles.Account(UserId(context), ct);
            return account is null ? Results.Unauthorized() : Results.Ok(account);
        }).RequireAuthorization();
        app.MapGet("/salon", async (HttpContext context, ISalonProfiles profiles, CancellationToken ct) =>
        {
            if (await profiles.Account(UserId(context), ct) is null) return Results.Unauthorized();
            var profile = await profiles.Read(UserId(context), ct);
            return profile is null ? Results.NotFound(new { error = "Salon setup required." }) : Results.Ok(profile);
        }).RequireAuthorization();
        app.MapPost("/salon", (SalonInput input, HttpContext context, ISalonProfiles profiles, CancellationToken ct) => SaveSalon(input, context, profiles, true, ct)).RequireAuthorization();
        app.MapPut("/salon", (SalonInput input, HttpContext context, ISalonProfiles profiles, CancellationToken ct) => SaveSalon(input, context, profiles, false, ct)).RequireAuthorization();
        app.MapGet("/services", async (HttpContext context, IServiceCatalog services, CancellationToken ct) =>
        {
            try { return Results.Ok(await services.List(UserId(context), ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
        app.MapGet("/services/{id:guid}", async (Guid id, HttpContext context, IServiceCatalog services, CancellationToken ct) =>
        {
            try
            {
                var service = await services.Get(UserId(context), id, ct);
                return service is null ? Results.NotFound() : Results.Ok(service);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
        app.MapPost("/services", async (ServiceInput input, HttpContext context, IServiceCatalog services, CancellationToken ct) =>
        {
            try { return Results.Ok(await services.Create(UserId(context), input, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentOutOfRangeException error) { return ServiceValidation(error); }
            catch (ArgumentException error) { return ServiceValidation(error); }
        }).RequireAuthorization();
        app.MapPut("/services/{id:guid}", async (Guid id, ServiceInput input, HttpContext context, IServiceCatalog services, CancellationToken ct) =>
        {
            try { return Results.Ok(await services.Update(UserId(context), id, input, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentOutOfRangeException error) { return ServiceValidation(error); }
            catch (ArgumentException error) { return ServiceValidation(error); }
        }).RequireAuthorization();
        app.MapDelete("/services/{id:guid}", async (Guid id, HttpContext context, IServiceCatalog services, CancellationToken ct) =>
        {
            try
            {
                await services.Delete(UserId(context), id, ct);
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        }).RequireAuthorization();
        app.MapGet("/employees", async (HttpContext context, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            try { return Results.Ok(await employees.List(UserId(context), ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
        app.MapGet("/employees/{id:guid}", async (Guid id, HttpContext context, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            try
            {
                var employee = await employees.Get(UserId(context), id, ct);
                return employee is null ? Results.NotFound() : Results.Ok(employee);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
        app.MapPost("/employees", async (EmployeeInput input, HttpContext context, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            try { return Results.Ok(await employees.Create(UserId(context), input, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException error) { return EmployeeValidation(error); }
        }).RequireAuthorization();
        app.MapPut("/employees/{id:guid}", async (Guid id, EmployeeInput input, HttpContext context, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            try { return Results.Ok(await employees.Update(UserId(context), id, input, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException error) { return EmployeeValidation(error); }
        }).RequireAuthorization();
        app.MapDelete("/employees/{id:guid}", async (Guid id, HttpContext context, IEmployeeDirectory employees, CancellationToken ct) =>
        {
            try
            {
                await employees.Delete(UserId(context), id, ct);
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).RequireAuthorization();
        app.MapGet("/seats", async (HttpContext context, ISeatDirectory seats, CancellationToken ct) =>
        {
            try { return Results.Ok(await seats.List(UserId(context), ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
        app.MapGet("/seats/{id:guid}", async (Guid id, HttpContext context, ISeatDirectory seats, CancellationToken ct) =>
        {
            try
            {
                var seat = await seats.Get(UserId(context), id, ct);
                return seat is null ? Results.NotFound() : Results.Ok(seat);
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
        app.MapPost("/seats", async (SeatInput input, HttpContext context, ISeatDirectory seats, CancellationToken ct) =>
        {
            try { return Results.Ok(await seats.Create(UserId(context), input, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException error) { return SeatValidation(error); }
        }).RequireAuthorization();
        app.MapPut("/seats/{id:guid}", async (Guid id, SeatInput input, HttpContext context, ISeatDirectory seats, CancellationToken ct) =>
        {
            try { return Results.Ok(await seats.Update(UserId(context), id, input, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException error) { return SeatValidation(error); }
        }).RequireAuthorization();
        app.MapDelete("/seats/{id:guid}", async (Guid id, HttpContext context, ISeatDirectory seats, CancellationToken ct) =>
        {
            try
            {
                await seats.Delete(UserId(context), id, ct);
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).RequireAuthorization();
    }

    private static bool IsProtectedApi(PathString path) =>
        path.StartsWithSegments("/auth") || path.StartsWithSegments("/salon") ||
        path.StartsWithSegments("/employees") || path.StartsWithSegments("/services") ||
        path.StartsWithSegments("/seats");

    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> SaveSalon(SalonInput input, HttpContext context, ISalonProfiles profiles, bool create, CancellationToken ct)
    {
        try { return Results.Ok(await profiles.Save(UserId(context), input, create, ct)); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (SetupConflictException) { return Results.Conflict(new { error = "Salon setup state changed. Reload the page." }); }
        catch (ArgumentException error)
        {
            var field = error.ParamName switch { "name" => "name", "timeZoneId" => "timeZoneId", _ => "hours" };
            return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error.Message] });
        }
    }

    private static IResult EmployeeValidation(ArgumentException error)
    {
        var field = error.ParamName switch
        {
            "name" => "name",
            "serviceIds" or "ServiceIds" => "serviceIds",
            _ => "hours",
        };
        return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error.Message] });
    }

    private static IResult SeatValidation(ArgumentException error)
    {
        var field = error.ParamName switch
        {
            "name" => "name",
            "type" => "type",
            "serviceIds" or "ServiceIds" => "serviceIds",
            _ => "serviceIds",
        };
        return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error.Message] });
    }

    private static IResult ServiceValidation(ArgumentException error)
    {
        var field = error.ParamName switch
        {
            "name" => "name",
            "category" => "category",
            "price" => "price",
            "durationMinutes" => "durationMinutes",
            _ => "name",
        };
        return Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error.Message] });
    }

    public sealed record LoginInput(string Login, string Password);
}

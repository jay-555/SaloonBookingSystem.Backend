# Salon API — Core domain and migrations

ASP.NET Core/.NET 8 with separate Domain, Application, Infrastructure, and API
projects. Phase 1 adds Salon, Employee, Seat, and Service plus an explicit initial
migration. Application services
use direct DI; no MediatR or generic dispatcher is needed for this scaffold.
Future use cases will use explicit application interfaces and handlers rather
than adding a dispatcher dependency by default.

## Run locally

Install .NET SDK 8.0.425 and start PostgreSQL 17. From this repository in PowerShell:

```powershell
dotnet restore Salon.sln --locked-mode
$env:ConnectionStrings__Salon = 'Host=localhost;Port=5432;Database=salon;Username=salon;Password=YOUR_LOCAL_PASSWORD;Timeout=3'
$env:ASPNETCORE_URLS = 'http://localhost:8080'
dotnet run --project src/Salon.Api --no-launch-profile
```

Use the root workspace Compose instructions to run all services together. Keep
credentials in local environment configuration only. `Frontend__Origin` defaults
to `http://localhost:3000` and limits direct browser CORS access to that origin.
The Next.js application normally uses its own same-origin proxy.

## API contract

| Endpoint | Success | Dependency failure |
| --- | --- | --- |
| `GET /health/live` | 200, `{"status":"healthy"}` | Independent of PostgreSQL |
| `GET /health/ready` | 200, `{"status":"healthy"}` after a real connection | 503, `{"status":"unavailable"}` |
| `GET /swagger/v1/swagger.json` | OpenAPI description | — |
| `GET /swagger` | Interactive API documentation | — |

Readiness is not cached, has a four-second cancellation deadline, and never
returns exceptions or connection strings. Connection strings should include
`Timeout=3` to bound connection establishment. Startup and health checks do not
create or migrate the database schema; readiness checks connectivity only.

## Core model

Domain entities validate non-empty GUIDs, trimmed required text, positive integer
service duration, and non-negative INR prices with no rounding of extra fractional
digits. Properties have no public setters. Salon defaults to `Asia/Kolkata` only
when the zone argument is omitted; explicit invalid/Windows-only identifiers are
rejected. OS IANA data/ICU must be available (as in the supported Windows and Linux
CI environments); there is no fixed-offset fallback.

Employee, Seat, and Service have required SalonId foreign keys and ownership
indexes. A salon with dependents cannot be deleted. Names are not unique and
seat types/categories are text, not separate aggregates. No public CRUD API is
added in this phase.

PostgreSQL price uses unconstrained `numeric` plus CHECK constraints, rather than
`numeric(p,2)` which would round before validation. It rejects negative values,
extra fractional digits, non-finite values, and normalized coefficients beyond
the CLR decimal range. Required text checks recognize Unicode whitespace. The
database requires a nonblank time-zone ID; catalogue validation occurs in Domain.

## Explicit migrations

Run these commands from `backend/` with .NET SDK 8.0.425. Use a development
PostgreSQL database and configure its connection string as shown above. EF design
tools use `ConnectionStrings__Salon` directly, without starting the API.

```powershell
dotnet tool restore
dotnet restore Salon.sln --locked-mode
dotnet ef migrations list --project src/Salon.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/Salon.Infrastructure
dotnet ef migrations script --idempotent --project src/Salon.Infrastructure --output TestResults/migrations.sql
dotnet ef database update InitialCoreDomain --project src/Salon.Infrastructure
```

The local EF tool is pinned to 8.0.31. Review the generated SQL before applying it.
Running `database update` again on an up-to-date database is a no-op. The initial
migration and snapshot live in `src/Salon.Infrastructure/Migrations`. Neither
API startup nor Docker Compose automatically applies them.

Only for an isolated disposable test database, rollback and reapply can be
verified with:

```powershell
# Re-check ConnectionStrings__Salon points to disposable data before continuing.
dotnet ef database update 0 --project src/Salon.Infrastructure
dotnet ef database update InitialCoreDomain --project src/Salon.Infrastructure
```

Rollback deletes the four business tables and their data. It is not a production
rollback procedure. The integration tests create their own disposable PostgreSQL
containers and cover this scenario without touching a configured application DB.

To create a later migration after an intentional model change, use
`dotnet ef migrations add <DescriptiveName> --project src/Salon.Infrastructure`;
review its SQL and snapshot and rerun all tests. Do not regenerate the initial
migration against databases where it has already been applied.

## Checks

```powershell
dotnet build Salon.sln -c Release --no-restore
dotnet format Salon.sln --verify-no-changes --no-restore
dotnet test Salon.sln -c Release --no-build
```

The full test command requires a running Docker engine. Testcontainers starts an
isolated PostgreSQL instance, tests readiness, stops it, verifies failure and
liveness, restarts it, then checks recovery. Additional isolated tests apply and
reapply migrations, round-trip all entities, reject invalid direct database
writes, verify restricted deletion, and roll back/reapply. Tests fail if Docker
is missing.
For limited feedback without Docker, explicitly run
`dotnet test Salon.sln -c Release --no-build --filter 'Category!=Container'`.
That subset does not satisfy the full merge gate.

Domain-only checks require no database or EF Core:
`dotnet test tests/Salon.Domain.Tests/Salon.Domain.Tests.csproj -c Release`.
CI runs these on both Windows and Linux, including IANA/Windows time-zone cases.
The Linux job runs the entire suite and checks the migration snapshot against the
model, then uploads test results and generated migration SQL for review.

GitHub Actions runs the full suite and builds the API Docker image. NuGet lockfiles
are source artifacts; CI and containers use locked restore. The root workspace
and frontend are separate repositories and are not retrieved by cloning this repo.

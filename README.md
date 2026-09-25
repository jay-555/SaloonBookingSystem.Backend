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

## Phase 2: authentication and salon setup

Apply migrations explicitly before provisioning accounts. API startup never migrates
or creates users. From this repository, configure `ConnectionStrings__Salon` using
your normal secret environment configuration, then run:

```powershell
dotnet tool restore
dotnet ef database update --project src/Salon.Infrastructure
dotnet run --project src/Salon.Api --no-launch-profile -- provision owner OwnerAdmin
```

The provisioning command prompts for a hidden password. For unattended operation,
set `SALON_BOOTSTRAP_PASSWORD` in the process secret environment and unset it after
use; do not pass passwords on the command line. Passwords require at least 12
characters with upper/lowercase, a digit, and a non-alphanumeric character. Five
failed attempts lock the account for five minutes. Login names are normalized and
unique. Re-provisioning an existing login fails without changing credentials,
role, or salon membership.

An unassigned owner creates a salon through `/admin`. To attach an owner or staff
to an existing salon, explicitly pass that salon's GUID:

```powershell
dotnet run --project src/Salon.Api --no-launch-profile -- provision manager Manager <salon-guid>
dotnet run --project src/Salon.Api --no-launch-profile -- provision stylist Employee <salon-guid>
dotnet run --project src/Salon.Api --no-launch-profile -- provision owner2 OwnerAdmin <salon-guid>
```

The three roles are a single constrained field on the Identity account, rather
than a many-to-many role collection. Every protected operation resolves current
membership from PostgreSQL. Only OwnerAdmin can create/edit; staff can read their
own salon. Accounts and scheduling Employee entities are independent.

## Phase 3: employee management

Apply the additive `EmployeeManagement` migration after Phase 2:

```powershell
dotnet ef database update --project src/Salon.Infrastructure
```

OwnerAdmin can create, update, and delete employees for their salon, including
service skills (same-salon services only), weekly hours, and breaks. Manager can
list and read. Identity accounts remain separate from Employee rows. Hard delete
is allowed while no booking dependents exist. REST: `GET/POST /employees`,
`GET/PUT/DELETE /employees/{id}`, and `GET /services` for skill selection.
Admin UI lives at `/admin/employees` on the frontend.

Browser sessions use Identity cookies with an eight-hour absolute lifetime and
no remember-me or sliding renewal. Cookies are HttpOnly, host-only, SameSite=Lax,
and Secure outside Development. Development over HTTP is for loopback local use
only. Configure `DataProtection__KeyPath` to a persistent protected directory;
root Compose mounts `/home/app/keys`. Do not commit this directory. Production
hosting and encryption-at-rest configuration for key storage remain deployment
work. Cookie-authenticated unsafe requests require `X-CSRF-TOKEN`, acquired from
`GET /auth/csrf` and renewed after authentication changes. The browser communicates
through the Next.js same-origin proxy; no bearer token is stored in the browser.

| API | Behavior |
|---|---|
| GET `/auth/csrf` | Antiforgery request token and host cookie |
| POST `/auth/login` | `{login,password}`; 204 on success, generic 401 on failure |
| POST `/auth/logout` | Clears the current session; 204 |
| GET `/auth/me` | Current account ID, login, role, salonId; 401 if unauthenticated |
| GET `/salon` | Own profile; 404 means initial setup is required |
| POST `/salon` | Owner setup; atomically creates salon/membership; 409 on repeated setup |
| PUT `/salon` | Owner profile update; 403 for staff, 400 for invalid fields |

Salon writes take `{name,timeZoneId,hours:[{day,opensAt,closesAt},...]}`. Days are
Sunday=0 through Saturday=6, each exactly once. Both times are null for closed days;
otherwise use `HH:mm`, with opening earlier than closing on the same day. New setup
screens default to closed. The additive migration gives existing salons seven
closed days and does not infer account ownership. Profile changes are transactional.
There is no registration, recovery, public role assignment, or salon deletion API.

### Database tests and migration review

The default integration suite uses disposable PostgreSQL 17 Testcontainers:

```powershell
dotnet test Salon.sln -c Release
dotnet ef migrations has-pending-model-changes --project src/Salon.Infrastructure
dotnet ef migrations script --idempotent --project src/Salon.Infrastructure --output migrations.sql
```

Without Docker, set `SALON_TEST_POSTGRES` to a **disposable PostgreSQL server**
connection with database-creation permission. Each test creates and drops a unique
`salon_test_<guid>` database. The outage test disables connections only to its own
test database and restores them. Never point test configuration at user data.
CI continues to use containers, including the outage/recovery test.

For a disposable database only, rollback the new migration explicitly:

```powershell
dotnet ef database update 20260923172137_InitialCoreDomain --project src/Salon.Infrastructure
dotnet ef database update --project src/Salon.Infrastructure
```

Rollback deletes Phase 2 account/session-support tables and working hours; it
preserves Phase 1 domain tables. Do not use rollback as a production recovery plan.
`tests/fixtures/e2e.sql` is only for an empty migrated browser-test database. It
creates a staff fixture salon; CI then provisions a separate unassigned owner and
staff accounts for the real browser tests. No fixture runs at application startup.

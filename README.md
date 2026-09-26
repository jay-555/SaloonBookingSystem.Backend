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

## Phase 4: seat management

Apply the additive `SeatManagement` migration after Phase 3:

```powershell
dotnet ef database update --project src/Salon.Infrastructure
```

OwnerAdmin can create, update, and delete seats (chairs) for their salon,
including supported services (same-salon services only, separate from employee
skills). Manager can list and read. Hard delete is allowed while no booking
dependents exist. REST: `GET/POST /seats`, `GET/PUT/DELETE /seats/{id}`, and
`GET /services` for support-service selection. Admin UI lives at `/admin/seats`
on the frontend.

## Phase 5: service catalog

No new migration is required beyond Phase 4 — the Phase 1 `Services` table is
reused. Explicit `database update` remains the supported apply path:

```powershell
dotnet ef database update --project src/Salon.Infrastructure
```

OwnerAdmin can create, update, and delete services (name, category, INR price,
duration minutes). Manager can list and read. Hard delete is blocked while
employee skills or seat support links still reference the service (HTTP 409).
REST: `GET/POST /services`, `GET/PUT/DELETE /services/{id}`. Admin UI lives at
`/admin/services` on the frontend.

## Phase 6: service resource requirements

Apply the additive `ServiceResourceRequirements` migration after Phase 5:

```powershell
dotnet ef database update --project src/Salon.Infrastructure
```

OwnerAdmin can set and clear each service's resource requirements: employee
capacity fixed at 1, required seat type, and optional buffer minutes. Manager
can read requirements on service detail. REST:
`PUT/DELETE /services/{id}/requirements` (also returned on
`GET /services/{id}`). Admin UI is on the service editor under
`/admin/services/{id}`.

## Phase 7: booking engine core

Apply the additive `BookingEngineCore` migration after Phase 6:

```powershell
dotnet ef database update --project src/Salon.Infrastructure
```

Adds `Bookings` and `EmployeeLeaves` tables, with PostgreSQL GiST
`EXCLUDE` constraints preventing overlapping bookings per employee and per
seat (`btree_gist`). There is **no** public booking-create API in this phase.

OwnerAdmin and Manager can query free single-service slots:

`GET /availability?serviceId={guid}&date=yyyy-MM-dd&employeeId={guid?}`

Returns salon-local `startsAtLocal` (`HH:mm`) and `startsAtUtc`, plus a
candidate employee/seat pair. Checks working hours, breaks, leave, existing
bookings, Phase 6 buffer, and seats of the required type that support the
service. Employee and anonymous callers receive 403/401. Phase 8 wires the
public `/book` UI to this engine.

## Phase 8: customer booking flow

Apply the additive `CustomerBookingFlow` migration after Phase 7:

```powershell
dotnet ef database update --project src/Salon.Infrastructure
```

Adds `Customers` and optional `Bookings.CustomerId`. Public anonymous APIs reuse
the Phase 7 availability engine for a single targeted salon:

| API | Behavior |
|---|---|
| GET `/public/salon` | Public salon name and time zone |
| GET `/public/services` | Bookable services only (requirements + skilled employee + supporting seat) |
| GET `/public/services/{id}/employees` | Skilled employees for a bookable service |
| GET `/public/availability` | Same query shape as admin availability; salon from server config |
| POST `/public/bookings` | Create booking + customer; CSRF required; 409 if slot taken |

Salon targeting: set `Booking:PublicSalonId` (env `Booking__PublicSalonId`) to the
public salon GUID when more than one salon exists. If the setting is empty and
exactly one salon is present, that salon is used. Responses never accept an
arbitrary client-supplied salon GUID.

Phone validation expects a 10-digit Indian mobile (optional `+91` prefix). Email
is optional. Reserved window = service duration + Phase 6 buffer. Frontend
`/book` is the stepped customer UI; the session proxy allowlists the public
routes above.

## Phase 9: employee calendar (read view)

No new migration. OwnerAdmin and Manager can read an employee day or week
schedule (Monday-start weeks) from existing bookings, breaks, and leave:

`GET /employees/{id}/schedule?date=yyyy-MM-dd&view=day|week`

Returns salon-local timeline items (`booking`, `break`, `leave`) with labels
and optional seat name for bookings. Employee Identity accounts receive 403 —
Identity↔Employee linking for self-view is deferred. Admin UI: `/admin/calendar`.

## Phase 10: seat calendar (read view)

No new migration. OwnerAdmin and Manager can read a seat day or week schedule
(Monday-start weeks) from existing bookings on that seat:

`GET /seats/{id}/schedule?date=yyyy-MM-dd&view=day|week`

Returns salon-local booking items (service · employee labels). Employee
Identity accounts receive 403. Admin UI: `/admin/seats/calendar`. Reuses Phase 9
schedule assembly; seats have no breaks/leave.

## Phase 11: walk-in booking

No new migration. OwnerAdmin and Manager create reception bookings for their
membership salon using the Phase 7 engine (same path as public create):

| API | Behavior |
|---|---|
| GET `/services/{id}/employees` | Skilled employees for a bookable service |
| POST `/bookings` | Walk-in create + customer; CSRF; 409 if slot taken |

Uses staff `/availability` for slot browsing. Employee role receives 403.
Admin UI: `/admin/walk-in`. A walk-in occupies the same employee/seat window a
website booking would.

## Phase 12: booking modification

Additive migration `BookingModification` adds `Bookings.CancelledAtUtc`,
`BookingAudits`, and partial GiST exclusions so cancelled rows do not block
slots. OwnerAdmin and Manager may reschedule/reassign or cancel within their
salon using the Phase 7 engine (exclude the booking being moved from busy
checks):

| API | Behavior |
|---|---|
| GET `/bookings/{id}` | Staff booking detail |
| PUT `/bookings/{id}` | Reschedule/reassign (`date`, `startsAtLocal`, optional `employeeId`); CSRF; 409 if taken |
| POST `/bookings/{id}/cancel` | Soft-cancel; frees employee/seat window; writes audit |

Staff `/availability` accepts optional `excludeBookingId`. Calendars omit
cancelled bookings. Admin UI: `/admin/bookings/{id}` (linked from calendars).

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

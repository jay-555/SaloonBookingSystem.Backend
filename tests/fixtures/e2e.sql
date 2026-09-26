-- Disposable E2E database only. Never run against user data.
INSERT INTO "Salons" ("Id", "Name", "TimeZoneId")
VALUES ('11111111-1111-4111-8111-111111111111', 'Staff fixture salon', 'Asia/Kolkata');
INSERT INTO "WorkingDays" ("SalonId", "Day")
SELECT '11111111-1111-4111-8111-111111111111'::uuid, day FROM generate_series(0, 6) AS day;

-- Bookable catalog for anonymous /book (Phase 8). PublicSalonId should target this salon.
INSERT INTO "Services" ("Id", "SalonId", "Name", "Category", "Price", "DurationMinutes")
VALUES ('22222222-2222-4222-8222-222222222222', '11111111-1111-4111-8111-111111111111', 'Haircut', 'Hair', 500, 60);
INSERT INTO "ServiceResourceRequirements" ("ServiceId", "EmployeeCapacity", "SeatType", "BufferMinutes")
VALUES ('22222222-2222-4222-8222-222222222222', 1, 'Chair', 0);
INSERT INTO "Employees" ("Id", "SalonId", "Name")
VALUES ('33333333-3333-4333-8333-333333333333', '11111111-1111-4111-8111-111111111111', 'Alex');
INSERT INTO "EmployeeSkills" ("EmployeeId", "ServiceId")
VALUES ('33333333-3333-4333-8333-333333333333', '22222222-2222-4222-8222-222222222222');
INSERT INTO "EmployeeWorkingDays" ("EmployeeId", "Day", "OpensAt", "ClosesAt")
SELECT '33333333-3333-4333-8333-333333333333'::uuid, day, 9 * 60, 18 * 60 FROM generate_series(0, 6) AS day;
INSERT INTO "Seats" ("Id", "SalonId", "Name", "Type")
VALUES ('44444444-4444-4444-8444-444444444444', '11111111-1111-4111-8111-111111111111', 'Chair 1', 'Chair');
INSERT INTO "SeatServices" ("SeatId", "ServiceId")
VALUES ('44444444-4444-4444-8444-444444444444', '22222222-2222-4222-8222-222222222222');

-- Phase 9 calendar fixtures (Monday 2026-09-28 IST): booking 10:00, break 12:00, leave covering Wed.
INSERT INTO "EmployeeBreaks" ("Id", "EmployeeId", "Day", "StartsAt", "EndsAt")
VALUES ('55555555-5555-4555-8555-555555555555', '33333333-3333-4333-8333-333333333333', 1, 12 * 60, 13 * 60);
-- Phase 14 CRM fixture customer linked to the calendar booking.
INSERT INTO "Customers" ("Id", "SalonId", "Name", "Phone", "Email")
VALUES (
  '88888888-8888-4888-8888-888888888888',
  '11111111-1111-4111-8111-111111111111',
  'Priya Sharma',
  '9876543210',
  'priya@example.com'
);
INSERT INTO "Bookings" ("Id", "SalonId", "ServiceId", "EmployeeId", "SeatId", "CustomerId", "StartsAtUtc", "EndsAtUtc", "Status")
VALUES (
  '66666666-6666-4666-8666-666666666666',
  '11111111-1111-4111-8111-111111111111',
  '22222222-2222-4222-8222-222222222222',
  '33333333-3333-4333-8333-333333333333',
  '44444444-4444-4444-8444-444444444444',
  '88888888-8888-4888-8888-888888888888',
  '2026-09-28T04:30:00Z',
  '2026-09-28T05:30:00Z',
  'Confirmed'
);
INSERT INTO "EmployeeLeaves" ("Id", "EmployeeId", "StartsAtUtc", "EndsAtUtc")
VALUES (
  '77777777-7777-4777-8777-777777777777',
  '33333333-3333-4333-8333-333333333333',
  '2026-09-29T18:30:00Z',
  '2026-10-01T18:30:00Z'
);

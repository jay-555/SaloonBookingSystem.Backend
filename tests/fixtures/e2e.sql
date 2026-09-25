-- Disposable E2E database only. Never run against user data.
INSERT INTO "Salons" ("Id", "Name", "TimeZoneId")
VALUES ('11111111-1111-4111-8111-111111111111', 'Staff fixture salon', 'Asia/Kolkata');
INSERT INTO "WorkingDays" ("SalonId", "Day")
SELECT '11111111-1111-4111-8111-111111111111'::uuid, day FROM generate_series(0, 6) AS day;

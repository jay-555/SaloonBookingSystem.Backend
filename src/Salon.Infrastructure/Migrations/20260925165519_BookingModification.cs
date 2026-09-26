using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookingModification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalonId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    FromStartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FromEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromSeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToStartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ToEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ToEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToSeatId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingAudits", x => x.Id);
                    table.CheckConstraint("CK_BookingAudits_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_BookingAudits_Operation", "\"Operation\" IN ('Reschedule','Cancel')");
                    table.ForeignKey(
                        name: "FK_BookingAudits_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingAudits_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingAudits_BookingId",
                table: "BookingAudits",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingAudits_SalonId",
                table: "BookingAudits",
                column: "SalonId");

            // Cancelled bookings must not occupy employee/seat windows.
            migrationBuilder.Sql("""
                ALTER TABLE "Bookings" DROP CONSTRAINT IF EXISTS "no_employee_overlap";
                ALTER TABLE "Bookings" DROP CONSTRAINT IF EXISTS "no_seat_overlap";
                ALTER TABLE "Bookings" ADD CONSTRAINT "no_employee_overlap"
                  EXCLUDE USING gist ("EmployeeId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
                  WHERE ("CancelledAtUtc" IS NULL);
                ALTER TABLE "Bookings" ADD CONSTRAINT "no_seat_overlap"
                  EXCLUDE USING gist ("SeatId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
                  WHERE ("CancelledAtUtc" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Bookings" DROP CONSTRAINT IF EXISTS "no_employee_overlap";
                ALTER TABLE "Bookings" DROP CONSTRAINT IF EXISTS "no_seat_overlap";
                ALTER TABLE "Bookings" ADD CONSTRAINT "no_employee_overlap"
                  EXCLUDE USING gist ("EmployeeId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&);
                ALTER TABLE "Bookings" ADD CONSTRAINT "no_seat_overlap"
                  EXCLUDE USING gist ("SeatId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&);
                """);

            migrationBuilder.DropTable(
                name: "BookingAudits");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Bookings");
        }
    }
}

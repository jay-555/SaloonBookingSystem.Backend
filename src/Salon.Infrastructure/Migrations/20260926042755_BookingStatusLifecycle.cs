using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookingStatusLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingAudits_Operation",
                table: "BookingAudits");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Bookings",
                type: "text",
                nullable: false,
                defaultValue: "Confirmed");

            migrationBuilder.Sql("""
                UPDATE "Bookings"
                SET "Status" = 'Cancelled'
                WHERE "CancelledAtUtc" IS NOT NULL;
                """);

            migrationBuilder.AddColumn<string>(
                name: "FromStatus",
                table: "BookingAudits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToStatus",
                table: "BookingAudits",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bookings_Status",
                table: "Bookings",
                sql: "\"Status\" IN ('Pending','Confirmed','CheckedIn','InService','Completed','Cancelled','NoShow')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingAudits_Operation",
                table: "BookingAudits",
                sql: "\"Operation\" IN ('Reschedule','Cancel','StatusChange')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Bookings_Status",
                table: "Bookings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookingAudits_Operation",
                table: "BookingAudits");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "FromStatus",
                table: "BookingAudits");

            migrationBuilder.DropColumn(
                name: "ToStatus",
                table: "BookingAudits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookingAudits_Operation",
                table: "BookingAudits",
                sql: "\"Operation\" IN ('Reschedule','Cancel')");
        }
    }
}

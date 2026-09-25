using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeBreaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<int>(type: "integer", nullable: false),
                    EndsAt = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeBreaks", x => x.Id);
                    table.CheckConstraint("CK_EmployeeBreaks_Interval", "\"Day\" BETWEEN 0 AND 6 AND \"StartsAt\" >= 0 AND \"EndsAt\" <= 1439 AND \"StartsAt\" < \"EndsAt\"");
                    table.ForeignKey(
                        name: "FK_EmployeeBreaks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSkills",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSkills", x => new { x.EmployeeId, x.ServiceId });
                    table.ForeignKey(
                        name: "FK_EmployeeSkills_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeSkills_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeWorkingDays",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    OpensAt = table.Column<int>(type: "integer", nullable: true),
                    ClosesAt = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeWorkingDays", x => new { x.EmployeeId, x.Day });
                    table.CheckConstraint("CK_EmployeeWorkingDays_Interval", "\"Day\" BETWEEN 0 AND 6 AND ((\"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL) OR (\"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" >= 0 AND \"ClosesAt\" <= 1439 AND \"OpensAt\" < \"ClosesAt\"))");
                    table.ForeignKey(
                        name: "FK_EmployeeWorkingDays_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBreaks_EmployeeId",
                table: "EmployeeBreaks",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSkills_ServiceId",
                table: "EmployeeSkills",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeBreaks");

            migrationBuilder.DropTable(
                name: "EmployeeSkills");

            migrationBuilder.DropTable(
                name: "EmployeeWorkingDays");
        }
    }
}

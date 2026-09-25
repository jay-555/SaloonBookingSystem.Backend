using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServiceResourceRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceResourceRequirements",
                columns: table => new
                {
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeCapacity = table.Column<int>(type: "integer", nullable: false),
                    SeatType = table.Column<string>(type: "text", nullable: false),
                    BufferMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceResourceRequirements", x => x.ServiceId);
                    table.CheckConstraint("CK_ServiceResourceRequirements_Rules", "\"EmployeeCapacity\" = 1 AND btrim(\"SeatType\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000') <> '' AND \"BufferMinutes\" >= 0");
                    table.ForeignKey(
                        name: "FK_ServiceResourceRequirements_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceResourceRequirements");
        }
    }
}

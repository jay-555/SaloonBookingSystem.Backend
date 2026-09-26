using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Salon.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomerCrmProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Customers_SalonId_Phone",
                table: "Customers",
                columns: new[] { "SalonId", "Phone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_SalonId_Phone",
                table: "Customers");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserHolidayRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolidayCountryCode",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HolidaySubdivisionCode",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolidayCountryCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HolidaySubdivisionCode",
                table: "Users");
        }
    }
}

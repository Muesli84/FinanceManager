using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactCategoryIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavingsPlanCategoryIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavingsPlanIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityCategoryIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityIdsCsv",
                table: "ReportFavorites",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountIdsCsv",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "ContactCategoryIdsCsv",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "ContactIdsCsv",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "SavingsPlanCategoryIdsCsv",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "SavingsPlanIdsCsv",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "SecurityCategoryIdsCsv",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "SecurityIdsCsv",
                table: "ReportFavorites");
        }
    }
}

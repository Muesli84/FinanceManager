using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportFavoriteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Take",
                table: "ReportFavorites",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeDividendRelated",
                table: "ReportFavorites",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseValutaDate",
                table: "ReportFavorites",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Take",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "IncludeDividendRelated",
                table: "ReportFavorites");

            migrationBuilder.DropColumn(
                name: "UseValutaDate",
                table: "ReportFavorites");
        }
    }
}

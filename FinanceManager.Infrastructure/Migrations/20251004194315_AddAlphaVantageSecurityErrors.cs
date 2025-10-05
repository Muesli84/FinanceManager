using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlphaVantageSecurityErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasPriceError",
                table: "Securities",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PriceErrorMessage",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PriceErrorSinceUtc",
                table: "Securities",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasPriceError",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "PriceErrorMessage",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "PriceErrorSinceUtc",
                table: "Securities");
        }
    }
}

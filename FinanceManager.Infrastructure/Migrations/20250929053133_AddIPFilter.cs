using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIPFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedLoginUtc",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IpBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsBlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BlockReason = table.Column<string>(type: "TEXT", nullable: true),
                    UnknownUserFailedAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    UnknownUserLastFailedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpBlocks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IpBlocks_IpAddress",
                table: "IpBlocks",
                column: "IpAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IpBlocks");

            migrationBuilder.DropColumn(
                name: "LastFailedLoginUtc",
                table: "Users");
        }
    }
}

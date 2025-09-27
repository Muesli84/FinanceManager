using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeKPISettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HomeKpis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportFavoriteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DisplayMode = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeKpis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeKpis_ReportFavorites_ReportFavoriteId",
                        column: x => x.ReportFavoriteId,
                        principalTable: "ReportFavorites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HomeKpis_OwnerUserId_SortOrder",
                table: "HomeKpis",
                columns: new[] { "OwnerUserId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_HomeKpis_ReportFavoriteId",
                table: "HomeKpis",
                column: "ReportFavoriteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeKpis");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportFavorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PostingKind = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityGroup = table.Column<int>(type: "INTEGER", nullable: false),
                    IncludeCategory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    ComparePrevious = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompareYear = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowChart = table.Column<bool>(type: "INTEGER", nullable: false),
                    Expandable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFavorites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportFavorites_OwnerUserId_Name",
                table: "ReportFavorites",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportFavorites");
        }
    }
}

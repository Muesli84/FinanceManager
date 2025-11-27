using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefactoringStatementDraftService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostingAggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SavingsPlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SecurityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Period = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingAggregates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "ContactId", "SavingsPlanId", "SecurityId", "Period", "PeriodStart" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostingAggregates");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clear existing aggregates to avoid unique index creation failures due to duplicates
            migrationBuilder.Sql("DELETE FROM PostingAggregates;");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "Period", "PeriodStart" },
                unique: true,
                filter: "[AccountId] IS NOT NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_ContactId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "ContactId", "Period", "PeriodStart" },
                unique: true,
                filter: "[ContactId] IS NOT NULL AND [AccountId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SavingsPlanId", "Period", "PeriodStart" },
                unique: true,
                filter: "[SavingsPlanId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SecurityId", "Period", "PeriodStart" },
                unique: true,
                filter: "[SecurityId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_AccountId_Period_PeriodStart",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_ContactId_Period_PeriodStart",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart",
                table: "PostingAggregates");
        }
    }
}

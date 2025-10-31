using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Data.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddValutaDateAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql(@"
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_AccountId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_ContactId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart;

DROP INDEX IF EXISTS IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_SecuritySubType_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_AccountId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_ContactId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_SecurityId_SecuritySubType_Period_PeriodStart;
");
            }

            migrationBuilder.AddColumn<int>(
                name: "DateKind",
                table: "PostingAggregates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_SecuritySubType_Period_PeriodStart_DateKind",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "ContactId", "SavingsPlanId", "SecurityId", "SecuritySubType", "Period", "PeriodStart", "DateKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_Period_PeriodStart_DateKind",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "Period", "PeriodStart", "DateKind" },
                unique: true,
                filter: "[AccountId] IS NOT NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_ContactId_Period_PeriodStart_DateKind",
                table: "PostingAggregates",
                columns: new[] { "Kind", "ContactId", "Period", "PeriodStart", "DateKind" },
                unique: true,
                filter: "[ContactId] IS NOT NULL AND [AccountId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart_DateKind",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SavingsPlanId", "Period", "PeriodStart", "DateKind" },
                unique: true,
                filter: "[SavingsPlanId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_SecurityId_SecuritySubType_Period_PeriodStart_DateKind",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SecurityId", "SecuritySubType", "Period", "PeriodStart", "DateKind" },
                unique: true,
                filter: "[SecurityId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_SecuritySubType_Period_PeriodStart_DateKind",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_AccountId_Period_PeriodStart_DateKind",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_ContactId_Period_PeriodStart_DateKind",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart_DateKind",
                table: "PostingAggregates");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_SecurityId_SecuritySubType_Period_PeriodStart_DateKind",
                table: "PostingAggregates");

            migrationBuilder.DropColumn(
                name: "DateKind",
                table: "PostingAggregates");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_SecuritySubType_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "ContactId", "SavingsPlanId", "SecurityId", "SecuritySubType", "Period", "PeriodStart" },
                unique: true);

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
                name: "IX_PostingAggregates_Kind_SecurityId_SecuritySubType_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SecurityId", "SecuritySubType", "Period", "PeriodStart" },
                unique: true,
                filter: "[SecurityId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL");
        }
    }
}

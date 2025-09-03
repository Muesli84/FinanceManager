using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _202509022022_AddStatementEntrySavingsPlanAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SavingsPlanId",
                table: "StatementDraftEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementEntries_SavingsPlanId",
                table: "StatementEntries",
                column: "SavingsPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_StatementEntries_SavingsPlans_SavingsPlanId",
                table: "StatementEntries",
                column: "SavingsPlanId",
                principalTable: "SavingsPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatementEntries_SavingsPlans_SavingsPlanId",
                table: "StatementEntries");

            migrationBuilder.DropIndex(
                name: "IX_StatementEntries_SavingsPlanId",
                table: "StatementEntries");

            migrationBuilder.DropColumn(
                name: "SavingsPlanId",
                table: "StatementDraftEntries");
        }
    }
}

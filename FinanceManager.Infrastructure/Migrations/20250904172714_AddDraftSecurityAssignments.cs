using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftSecurityAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SecurityFeeAmount",
                table: "StatementDraftEntries",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityId",
                table: "StatementDraftEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SecurityQuantity",
                table: "StatementDraftEntries",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SecurityTaxAmount",
                table: "StatementDraftEntries",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecurityTransactionType",
                table: "StatementDraftEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementDraftEntries_SecurityId",
                table: "StatementDraftEntries",
                column: "SecurityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StatementDraftEntries_SecurityId",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "SecurityFeeAmount",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "SecurityId",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "SecurityQuantity",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "SecurityTaxAmount",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "SecurityTransactionType",
                table: "StatementDraftEntries");
        }
    }
}

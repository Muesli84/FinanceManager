using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _202509022022_AddSplitDraftId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SplitDraftId",
                table: "StatementDraftEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementDraftEntries_SplitDraftId",
                table: "StatementDraftEntries",
                column: "SplitDraftId",
                unique: true,
                filter: "[SplitDraftId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StatementDraftEntries_StatementDrafts_SplitDraftId",
                table: "StatementDraftEntries",
                column: "SplitDraftId",
                principalTable: "StatementDrafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StatementDraftEntries_StatementDrafts_SplitDraftId",
                table: "StatementDraftEntries");

            migrationBuilder.DropIndex(
                name: "IX_StatementDraftEntries_SplitDraftId",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "SplitDraftId",
                table: "StatementDraftEntries");
        }
    }
}

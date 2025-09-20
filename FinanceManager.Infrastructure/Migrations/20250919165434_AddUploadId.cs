using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UploadGroupId",
                table: "StatementDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementDrafts_UploadGroupId",
                table: "StatementDrafts",
                column: "UploadGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StatementDrafts_UploadGroupId",
                table: "StatementDrafts");

            migrationBuilder.DropColumn(
                name: "UploadGroupId",
                table: "StatementDrafts");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _202509022022_AddDraftFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "OriginalFileContent",
                table: "StatementDrafts",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileContentType",
                table: "StatementDrafts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalFileContent",
                table: "StatementDrafts");

            migrationBuilder.DropColumn(
                name: "OriginalFileContentType",
                table: "StatementDrafts");
        }
    }
}

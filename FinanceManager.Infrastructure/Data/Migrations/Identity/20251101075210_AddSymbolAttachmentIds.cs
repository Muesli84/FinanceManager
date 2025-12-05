using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Data.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddSymbolAttachmentIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "SecurityCategories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "SavingsPlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "SavingsPlanCategories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "Contacts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "ContactCategories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SymbolAttachmentId",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "SecurityCategories");

            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "SavingsPlans");

            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "SavingsPlanCategories");

            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "ContactCategories");

            migrationBuilder.DropColumn(
                name: "SymbolAttachmentId",
                table: "AspNetUsers");
        }
    }
}

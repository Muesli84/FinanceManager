using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Attachments_CategoryId",
                table: "Attachments",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_AttachmentCategories_CategoryId",
                table: "Attachments",
                column: "CategoryId",
                principalTable: "AttachmentCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_AttachmentCategories_CategoryId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_CategoryId",
                table: "Attachments");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppReferenceAttachmentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceAttachmentId",
                table: "Attachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ReferenceAttachmentId",
                table: "Attachments",
                column: "ReferenceAttachmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Attachments_ReferenceAttachmentId",
                table: "Attachments",
                column: "ReferenceAttachmentId",
                principalTable: "Attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Attachments_ReferenceAttachmentId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_ReferenceAttachmentId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "ReferenceAttachmentId",
                table: "Attachments");
        }
    }
}

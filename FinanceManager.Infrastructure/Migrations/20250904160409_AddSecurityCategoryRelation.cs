using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityCategoryRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Securities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Securities_CategoryId",
                table: "Securities",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Securities_SecurityCategories_CategoryId",
                table: "Securities",
                column: "CategoryId",
                principalTable: "SecurityCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Securities_SecurityCategories_CategoryId",
                table: "Securities");

            migrationBuilder.DropIndex(
                name: "IX_Securities_CategoryId",
                table: "Securities");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Securities");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _202509022022_AddSavingsPlanCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "SavingsPlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SavingsPlanCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsPlanCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsPlans_CategoryId",
                table: "SavingsPlans",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_SavingsPlans_SavingsPlanCategories_CategoryId",
                table: "SavingsPlans",
                column: "CategoryId",
                principalTable: "SavingsPlanCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SavingsPlans_SavingsPlanCategories_CategoryId",
                table: "SavingsPlans");

            migrationBuilder.DropTable(
                name: "SavingsPlanCategories");

            migrationBuilder.DropIndex(
                name: "IX_SavingsPlans_CategoryId",
                table: "SavingsPlans");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "SavingsPlans");
        }
    }
}

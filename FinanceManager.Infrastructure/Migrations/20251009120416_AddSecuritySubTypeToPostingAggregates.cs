using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecuritySubTypeToPostingAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add nullable SecuritySubType column to PostingAggregates
            migrationBuilder.AddColumn<int>(
                name: "SecuritySubType",
                table: "PostingAggregates",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecuritySubType",
                table: "PostingAggregates");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _202509011600_ExtendStatementEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingDescription",
                table: "StatementEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "StatementEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsAnnounced",
                table: "StatementEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "StatementEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValutaDate",
                table: "StatementEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookingDescription",
                table: "StatementDraftEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "StatementDraftEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsAnnounced",
                table: "StatementDraftEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "StatementDraftEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValutaDate",
                table: "StatementDraftEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingDescription",
                table: "StatementEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "StatementEntries");

            migrationBuilder.DropColumn(
                name: "IsAnnounced",
                table: "StatementEntries");

            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "StatementEntries");

            migrationBuilder.DropColumn(
                name: "ValutaDate",
                table: "StatementEntries");

            migrationBuilder.DropColumn(
                name: "BookingDescription",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "IsAnnounced",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "RecipientName",
                table: "StatementDraftEntries");

            migrationBuilder.DropColumn(
                name: "ValutaDate",
                table: "StatementDraftEntries");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    public partial class AddSecurityDividendTaxConnectionInReports : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Column 'IncludeDividendRelated' is already added by
            // 20250927090829_AddReportFavoriteSettings. This migration previously
            // duplicated that AddColumn call and caused SQLite 'duplicate column' errors.
            // Intentionally left empty to avoid applying the same column twice.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op because Up is a no-op to keep migrations idempotent.
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <summary>
    /// Safety migration to (re)apply missing user import split settings columns if they were not created previously.
    /// Uses raw SQL with IF NOT EXISTS to avoid duplicate column errors in SQLite.
    /// </summary>
    public partial class FixUserImportSplitSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite: ADD COLUMN IF NOT EXISTS is supported on modern SQLite versions shipped with .NET.
            migrationBuilder.Sql("ALTER TABLE Users ADD COLUMN IF NOT EXISTS ImportSplitMode INTEGER NOT NULL DEFAULT 2;");
            migrationBuilder.Sql("ALTER TABLE Users ADD COLUMN IF NOT EXISTS ImportMaxEntriesPerDraft INTEGER NOT NULL DEFAULT 250;");
            migrationBuilder.Sql("ALTER TABLE Users ADD COLUMN IF NOT EXISTS ImportMonthlySplitThreshold INTEGER NULL;");

            // Initialize threshold to max where still NULL
            migrationBuilder.Sql("UPDATE Users SET ImportMonthlySplitThreshold = ImportMaxEntriesPerDraft WHERE ImportMonthlySplitThreshold IS NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite cannot drop columns easily without table rebuild; leaving columns in place.
            // Intentionally no-op to avoid data loss / complexity.
        }
    }
}

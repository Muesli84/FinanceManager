using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Data.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddReportFavoriteSettings2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration previously added the 'UseValutaDate' column to 'ReportFavorites'.
            // The column is already added by an earlier migration in another migration set
            // (20250927090829_AddReportFavoriteSettings). To avoid duplicate-column errors
            // when running migrations against existing databases (or during testing with
            // EnsureCreated/other migration sets), keep this migration as a no-op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op because Up is a no-op to keep migrations idempotent.
        }
    }
}

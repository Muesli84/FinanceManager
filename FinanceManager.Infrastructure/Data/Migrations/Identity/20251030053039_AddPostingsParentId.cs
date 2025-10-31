using System;
using System.IO;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Data.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddPostingsParentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "Postings",
                type: "TEXT",
                nullable: true);

            // Attempt to speed up the migration on slower machines by setting SQLite pragmas
            // Run pragma statements outside of the EF migration transaction to avoid SQLite error
            migrationBuilder.Sql("PRAGMA journal_mode=WAL;", suppressTransaction: true);
            migrationBuilder.Sql("PRAGMA synchronous=OFF;", suppressTransaction: true);
            migrationBuilder.Sql("PRAGMA temp_store=MEMORY;", suppressTransaction: true);

            // Create temporary indexes to speed up the update queries (IF NOT EXISTS to be robust)
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Postings_CreatedUtc ON Postings(CreatedUtc);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Postings_GroupId ON Postings(GroupId);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Postings_ContactId ON Postings(ContactId);");

            // Ensure ValutaDate set to BookingDate where it wasn't initialized
            migrationBuilder.Sql(@"
                UPDATE Postings
                SET ValutaDate = BookingDate
                WHERE ValutaDate IS NULL OR ValutaDate = '0001-01-01 00:00:00';
            ");

            // Build temporary parent info for groups that contain at least one posting with an intermediary contact
            // Store the group's latest CreatedUtc and the representative posting ids by kind
            // NOTE: previous implementation JOINed Postings to Contacts directly and thus excluded bank postings (ContactId NULL) from aggregation.
            // Use a subquery to find group ids that contain an intermediary posting, then aggregate over ALL postings in those groups so ParentBankId is captured.
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE IF NOT EXISTS parent_info AS
                SELECT
                    p.GroupId AS GroupId,
                    MAX(p.CreatedUtc) AS MaxCreated,
                    MAX(CASE WHEN p.Kind = 0 THEN p.Id END) AS ParentBankId,
                    MAX(CASE WHEN p.Kind = 1 THEN p.Id END) AS ParentContactId,
                    MAX(CASE WHEN p.Kind = 0 THEN p.ValutaDate END) AS ParentValutaDate
                FROM Postings p
                WHERE p.GroupId IN (
                    SELECT DISTINCT p2.GroupId
                    FROM Postings p2
                    JOIN Contacts c ON p2.ContactId = c.Id
                    WHERE c.IsPaymentIntermediary = 1 AND p2.GroupId IS NOT NULL
                )
                GROUP BY p.GroupId;
            ");

            // Assign ParentId for postings that were created later the same day as a parent group and have NULL Description
            // We use a correlated subquery to find the matching parent_info row for the same date where CreatedUtc > group's MaxCreated
            // and where the posting is not part of the same group (avoid self-referencing)
            migrationBuilder.Sql(@"
                UPDATE Postings
                SET ParentId = (
                    SELECT
                        CASE
                            WHEN Postings.Kind = 0 THEN pi.ParentBankId
                            WHEN Postings.Kind = 1 THEN pi.ParentContactId
                            ELSE pi.ParentBankId
                        END
                    FROM parent_info pi
                    WHERE date(Postings.CreatedUtc) = date(pi.MaxCreated)
                      AND Postings.CreatedUtc > pi.MaxCreated
                      AND (pi.GroupId IS NULL OR (Postings.GroupId IS NULL OR Postings.GroupId != pi.GroupId))
                    LIMIT 1
                )
                WHERE ParentId IS NULL
                  AND Postings.Description IS NULL
                  AND EXISTS (
                    SELECT 1 FROM parent_info pi
                    WHERE date(Postings.CreatedUtc) = date(pi.MaxCreated)
                      AND Postings.CreatedUtc > pi.MaxCreated
                      AND (pi.GroupId IS NULL OR (Postings.GroupId IS NULL OR Postings.GroupId != pi.GroupId))
                );
            ");

            // For postings that received a ParentId, adopt parent's ValutaDate to keep consistency
            migrationBuilder.Sql(@"
                UPDATE Postings
                SET ValutaDate = (
                    SELECT p2.ValutaDate FROM Postings p2 WHERE p2.Id = Postings.ParentId LIMIT 1
                )
                WHERE ParentId IS NOT NULL
                  AND (ValutaDate IS NULL OR ValutaDate != (
                        SELECT p2.ValutaDate FROM Postings p2 WHERE p2.Id = Postings.ParentId LIMIT 1
                  ));
            ");

            // Drop temporary parent_info table
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS parent_info;");

            // Drop the temporary indexes we created (best effort)
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Postings_CreatedUtc;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Postings_GroupId;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Postings_ContactId;");

            // Try to restore pragmas to safer defaults (outside transaction)
            migrationBuilder.Sql("PRAGMA synchronous=NORMAL;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Postings");
        }
    }
}

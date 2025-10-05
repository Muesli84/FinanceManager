using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexForSelfContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Temporarily drop unique indexes on PostingAggregates to avoid violations while consolidating
            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql(@"
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart')
    DROP INDEX IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart ON PostingAggregates;
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_PostingAggregates_Kind_AccountId_Period_PeriodStart')
    DROP INDEX IX_PostingAggregates_Kind_AccountId_Period_PeriodStart ON PostingAggregates;
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_PostingAggregates_Kind_ContactId_Period_PeriodStart')
    DROP INDEX IX_PostingAggregates_Kind_ContactId_Period_PeriodStart ON PostingAggregates;
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart')
    DROP INDEX IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart ON PostingAggregates;
IF EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart')
    DROP INDEX IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart ON PostingAggregates;
");
            }
            else if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql(@"
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_AccountId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_ContactId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart;
DROP INDEX IF EXISTS IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart;
");
            }

            // Data fix: merge duplicate Self contacts (Type = 0) per owner
            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.__SelfDups', 'U') IS NOT NULL DROP TABLE dbo.__SelfDups;
CREATE TABLE dbo.__SelfDups(
    OwnerUserId uniqueidentifier NOT NULL,
    KeeperContactId uniqueidentifier NOT NULL,
    DuplicateContactId uniqueidentifier NOT NULL
);
INSERT INTO dbo.__SelfDups(OwnerUserId, KeeperContactId, DuplicateContactId)
SELECT s.OwnerUserId, k.KeeperContactId, s.Id
FROM Contacts s
JOIN (
    SELECT OwnerUserId, MIN(Id) AS KeeperContactId
    FROM Contacts
    WHERE Type = 0
    GROUP BY OwnerUserId
) k ON k.OwnerUserId = s.OwnerUserId
WHERE s.Type = 0 AND s.Id <> k.KeeperContactId;

-- Reassign references in statement draft entries, entries, postings
UPDATE e SET ContactId = d.KeeperContactId
FROM StatementDraftEntries e
JOIN dbo.__SelfDups d ON d.DuplicateContactId = e.ContactId;

UPDATE e SET ContactId = d.KeeperContactId
FROM StatementEntries e
JOIN dbo.__SelfDups d ON d.DuplicateContactId = e.ContactId;

UPDATE p SET ContactId = d.KeeperContactId
FROM Postings p
JOIN dbo.__SelfDups d ON d.DuplicateContactId = p.ContactId;

-- Update aggregates contact ids first
UPDATE a SET ContactId = d.KeeperContactId
FROM PostingAggregates a
JOIN dbo.__SelfDups d ON d.DuplicateContactId = a.ContactId;

-- Collapse duplicate aggregates by grouping and summing amounts
IF OBJECT_ID('dbo.__PostingAggregates_tmp', 'U') IS NOT NULL DROP TABLE dbo.__PostingAggregates_tmp;
SELECT MIN(Id) AS Id,
       Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart,
       SUM(Amount) AS Amount,
       MIN(CreatedUtc) AS CreatedUtc,
       MAX(ModifiedUtc) AS ModifiedUtc
INTO dbo.__PostingAggregates_tmp
FROM PostingAggregates
GROUP BY Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart;

TRUNCATE TABLE PostingAggregates;
INSERT INTO PostingAggregates (Id, Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart, Amount, CreatedUtc, ModifiedUtc)
SELECT Id, Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart, Amount, CreatedUtc, ModifiedUtc
FROM dbo.__PostingAggregates_tmp;
DROP TABLE dbo.__PostingAggregates_tmp;

-- Remove duplicate self contacts
DELETE c
FROM Contacts c
JOIN dbo.__SelfDups d ON d.DuplicateContactId = c.Id;
DROP TABLE dbo.__SelfDups;
");
            }
            else if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql(@"
DROP TABLE IF EXISTS __SelfDups;
CREATE TABLE __SelfDups(
    OwnerUserId TEXT NOT NULL,
    KeeperContactId TEXT NOT NULL,
    DuplicateContactId TEXT NOT NULL
);
-- Fill mapping using window function
INSERT INTO __SelfDups(OwnerUserId, KeeperContactId, DuplicateContactId)
SELECT OwnerUserId,
       MIN(Id) OVER (PARTITION BY OwnerUserId) AS KeeperContactId,
       Id AS DuplicateContactId
FROM Contacts
WHERE Type = 0;
-- Remove non-duplicates (keeper rows)
DELETE FROM __SelfDups WHERE KeeperContactId = DuplicateContactId;

-- Reassign references
UPDATE StatementDraftEntries
SET ContactId = (
    SELECT KeeperContactId FROM __SelfDups d WHERE d.DuplicateContactId = StatementDraftEntries.ContactId
)
WHERE ContactId IN (SELECT DuplicateContactId FROM __SelfDups);

UPDATE StatementEntries
SET ContactId = (
    SELECT KeeperContactId FROM __SelfDups d WHERE d.DuplicateContactId = StatementEntries.ContactId
)
WHERE ContactId IN (SELECT DuplicateContactId FROM __SelfDups);

UPDATE Postings
SET ContactId = (
    SELECT KeeperContactId FROM __SelfDups d WHERE d.DuplicateContactId = Postings.ContactId
)
WHERE ContactId IN (SELECT DuplicateContactId FROM __SelfDups);

-- Update aggregates contact ids first
UPDATE PostingAggregates
SET ContactId = (
    SELECT KeeperContactId FROM __SelfDups d WHERE d.DuplicateContactId = PostingAggregates.ContactId
)
WHERE ContactId IN (SELECT DuplicateContactId FROM __SelfDups);

-- Collapse duplicate aggregates via grouped rebuild
DROP TABLE IF EXISTS __PostingAggregates_tmp;
CREATE TABLE __PostingAggregates_tmp AS
SELECT MIN(Id) AS Id,
       Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart,
       SUM(Amount) AS Amount,
       MIN(CreatedUtc) AS CreatedUtc,
       MAX(ModifiedUtc) AS ModifiedUtc
FROM PostingAggregates
GROUP BY Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart;

DELETE FROM PostingAggregates;
INSERT INTO PostingAggregates (Id, Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart, Amount, CreatedUtc, ModifiedUtc)
SELECT Id, Kind, AccountId, ContactId, SavingsPlanId, SecurityId, Period, PeriodStart, Amount, CreatedUtc, ModifiedUtc
FROM __PostingAggregates_tmp;
DROP TABLE __PostingAggregates_tmp;

-- Remove duplicate self contacts
DELETE FROM Contacts WHERE Type = 0 AND Id IN (SELECT DuplicateContactId FROM __SelfDups);
DROP TABLE __SelfDups;
");
            }

            // Recreate PostingAggregates indexes
            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "Period", "PeriodStart" },
                unique: true,
                filter: "[AccountId] IS NOT NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_ContactId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "ContactId", "Period", "PeriodStart" },
                unique: true,
                filter: "[ContactId] IS NOT NULL AND [AccountId] IS NULL AND [SavingsPlanId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SavingsPlanId", "Period", "PeriodStart" },
                unique: true,
                filter: "[SavingsPlanId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SecurityId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "SecurityId", "Period", "PeriodStart" },
                unique: true,
                filter: "[SecurityId] IS NOT NULL AND [AccountId] IS NULL AND [ContactId] IS NULL AND [SavingsPlanId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart",
                table: "PostingAggregates",
                columns: new[] { "Kind", "AccountId", "ContactId", "SavingsPlanId", "SecurityId", "Period", "PeriodStart" },
                unique: true);

            // Finally, add the unique filtered index for Self contacts
            migrationBuilder.CreateIndex(
                name: "IX_Contacts_OwnerUserId_Type",
                table: "Contacts",
                columns: new[] { "OwnerUserId", "Type" },
                unique: true,
                filter: "[Type] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_OwnerUserId_Type",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_AccountId_ContactId_SavingsPlanId_SecurityId_Period_PeriodStart",
                table: "PostingAggregates");
            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_AccountId_Period_PeriodStart",
                table: "PostingAggregates");
            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_ContactId_Period_PeriodStart",
                table: "PostingAggregates");
            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_SavingsPlanId_Period_PeriodStart",
                table: "PostingAggregates");
            migrationBuilder.DropIndex(
                name: "IX_PostingAggregates_Kind_SecurityId_Period_PeriodStart",
                table: "PostingAggregates");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore; // for Database facade
using Microsoft.EntityFrameworkCore.Infrastructure; // extension metadata

namespace FinanceManager.Infrastructure.Setup;

/// <summary>
/// Runtime safety patcher that ensures the new user preference columns for import split settings exist.
/// This is a fallback in case earlier migrations were partially applied or a database was created before the migration existed.
/// Only executed for SQLite provider.
/// </summary>
public static class SchemaPatcher
{
    public static void EnsureUserImportSplitSettingsColumns(AppDbContext db, ILogger logger)
    {
        var connection = db.Database.GetDbConnection();
        if (connection is not SqliteConnection sqlite)
        {
            // Only needed for SQLite fallback
            return;
        }

        try
        {
            if (sqlite.State != ConnectionState.Open)
            {
                sqlite.Open();
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = sqlite.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Users');";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetString(1); // cid|name|type|notnull|dflt_value|pk
                    existing.Add(name);
                }
            }

            bool changed = false;
            void AddColumn(string name, string sql)
            {
                if (!existing.Contains(name))
                {
                    using var alter = sqlite.CreateCommand();
                    alter.CommandText = sql;
                    alter.ExecuteNonQuery();
                    logger.LogInformation("SchemaPatcher: Added missing column {Column} to Users table.", name);
                    changed = true;
                }
            }

            AddColumn("ImportSplitMode", "ALTER TABLE Users ADD COLUMN ImportSplitMode INTEGER NOT NULL DEFAULT 2");
            AddColumn("ImportMaxEntriesPerDraft", "ALTER TABLE Users ADD COLUMN ImportMaxEntriesPerDraft INTEGER NOT NULL DEFAULT 250");
            AddColumn("ImportMonthlySplitThreshold", "ALTER TABLE Users ADD COLUMN ImportMonthlySplitThreshold INTEGER NULL");

            if (changed)
            {
                using var upd = sqlite.CreateCommand();
                upd.CommandText = "UPDATE Users SET ImportMonthlySplitThreshold = ImportMaxEntriesPerDraft WHERE ImportMonthlySplitThreshold IS NULL";
                upd.ExecuteNonQuery();
                logger.LogInformation("SchemaPatcher: Initialized ImportMonthlySplitThreshold where NULL.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SchemaPatcher: Failed to ensure user import split settings columns.");
            // Do not rethrow to avoid blocking app start; login will still fail if mapping mismatch persists.
        }
    }
}

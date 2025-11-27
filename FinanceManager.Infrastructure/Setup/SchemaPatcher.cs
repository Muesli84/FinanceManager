using FinanceManager.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Setup;

/// <summary>
/// Runtime safety patcher that ensures schema/runtime consistency after applying migrations.
/// Also triggers long-running background work that cannot/should not be executed inside migrations
/// (for example: rebuilding aggregates).
/// </summary>
public static class SchemaPatcher
{
    /// <summary>
    /// Apply runtime fixes after migrations have been applied. This is executed from ProgramExtensions after
    /// calling <c>db.Database.Migrate()</c>. It may enqueue background tasks to rebuild aggregates if needed.
    /// </summary>
    public static void RunPostMigrationPatches(IServiceProvider serviceProvider, AppDbContext db, ILogger logger)
    {
        // Run synchronously but perform async DB operations internally
        RunPostMigrationPatchesAsync(serviceProvider, db, logger).GetAwaiter().GetResult();
    }

    private static async Task RunPostMigrationPatchesAsync(IServiceProvider serviceProvider, AppDbContext db, ILogger logger)
    {
        if (serviceProvider == null) return;
        try
        {
            var taskManager = serviceProvider.GetService(typeof(IBackgroundTaskManager)) as IBackgroundTaskManager;
            if (taskManager == null)
            {
                logger.LogInformation("BackgroundTaskManager not available; skipping post-migration enqueue.");
                return;
            }

            // Check whether PostingAggregates has any Valuta aggregates already. If the column is not present
            // querying may throw (older DB without migration). In that case skip.
            bool hasAnyValuta = false;
            try
            {
                // If table empty or column absent this may throw for SQLite; catch below
                hasAnyValuta = await db.PostingAggregates.AsNoTracking().AnyAsync(a => a.DateKind != 0);
            }
            catch (SqliteException ex)
            {
                logger.LogInformation(ex, "PostingAggregates.DateKind column not present or query failed; skipping enqueue.");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to query PostingAggregates for DateKind; skipping enqueue.");
                return;
            }

            if (hasAnyValuta)
            {
                logger.LogInformation("Valuta aggregates appear to exist already; no rebuild enqueued.");
                return;
            }

            // Enqueue a rebuild task per user so background runner will rebuild aggregates including Valuta
            var userIds = await db.Users.AsNoTracking().Select(u => u.Id).ToListAsync();
            if (userIds.Count == 0)
            {
                logger.LogInformation("No users found; skipping aggregate rebuild enqueue.");
                return;
            }

            foreach (var uid in userIds)
            {
                try
                {
                    var info = taskManager.Enqueue(BackgroundTaskType.RebuildAggregates, uid, null, allowDuplicate: false);
                    logger.LogInformation("Enqueued RebuildAggregates task {TaskId} for user {UserId}", info.Id, uid);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enqueue rebuild aggregates for user {UserId}", uid);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Post-migration patching failed");
        }
    }
}

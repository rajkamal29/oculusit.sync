using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using oculusit.sync.orchestration;

namespace oculusit.sync;

public sealed class Worker(
    ILogger<Worker> logger,
    IHostApplicationLifetime lifetime,
    ICompanyOrchestrationService orchestration,
    ISyncStateService syncStateService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Worker started. Beginning ConnectWise to Keka sync.");
            // Capture start time before any data is fetched so mid-run changes are
            // included in the next run's window.
            var syncStartedAt = DateTime.UtcNow;
           
            var syncState = await syncStateService.GetAsync("Company", stoppingToken);
            if (syncState is null)
            {
                logger.LogInformation("No previous sync state found in DynamoDB. This is a fresh run.");
                
                var fullSyncState = await orchestration.SyncCompaniesToKekaAsync(stoppingToken);

                await syncStateService.SaveAsync(fullSyncState, stoppingToken);

                logger.LogInformation("Sync state saved. {Count} company mappings recorded. {syncedCompanies} companied synced in Keka. {failedCompanies} companies failed to sync in keka.", fullSyncState.Companies.Count + fullSyncState.FailedCompanies.Count, fullSyncState.Companies.Count, fullSyncState.FailedCompanies.Count);
            }
            else
            {
                logger.LogInformation("Incremental run. Last sync was at {LastUpdatedAt}.", syncState.LastUpdatedAt);

                var incrementalSyncState = await orchestration.SyncCompaniesIncrementalAsync(syncState, stoppingToken);
                await syncStateService.AppendSyncStateAsync("Company", incrementalSyncState, syncStartedAt, stoppingToken);
                logger.LogInformation("Incremental sync state updated. {Count} new company mappings appended. {updatedCompanies} companies updated successfully. {failedCompanies} companies failed to sync in keka.", incrementalSyncState.Companies.Count + incrementalSyncState.FailedCompanies.Count, incrementalSyncState.Companies.Count, incrementalSyncState.FailedCompanies.Count);

                logger.LogInformation("Retry Synchronizing of Failed Companies.");
                var retrySyncedEntries = await orchestration.RetryFailedCompaniesAsync(syncState, stoppingToken);
                if (retrySyncedEntries.Count > 0)
                {
                    await syncStateService.AppendSyncStateAsync("Company", new SyncState
                    {
                        SyncType = "Company",
                        Companies = retrySyncedEntries,
                        FailedCompanies = []
                    }, syncStartedAt, stoppingToken);

                    var retriedIds = retrySyncedEntries.Select(x => x.Id).ToList();
                    await syncStateService.RemoveFailedCompaniesAsync("Company", retriedIds, stoppingToken);
                    logger.LogInformation("Removed {Count} successfully retried companies from the failed list in DynamoDB.", retriedIds.Count);
                }

                logger.LogInformation("Failed companies sync retry completed. {Count} new company mappings appended. {retried} companies synced in keka. {failedCompanies} companies still failed to sync in keka.", retrySyncedEntries.Count, retrySyncedEntries.Count, syncState.FailedCompanies.Count);
            }

            logger.LogInformation("Sync complete. Worker shutting down.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Worker was cancelled before sync completed.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unhandled exception terminated the sync worker.");
            // Stop the host so the process exits with a non-zero code,
            // which signals ECS/container orchestrators to restart the task.
            lifetime.StopApplication();
        }
    }
}

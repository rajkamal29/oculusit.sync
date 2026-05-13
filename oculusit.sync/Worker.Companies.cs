using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncCompaniesAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        var syncState = await syncStateService.GetAsync(SyncTypes.Company, stoppingToken);

        if (syncState is null)
        {
            logger.LogInformation("No previous sync state found in DynamoDB. Running full company sync.");

            var syncedEntries = await companyOrchestration.SyncCompaniesToKekaAsync(stoppingToken);

            await syncStateService.SaveAsync(new SyncState
            {
                SyncType      = SyncTypes.Company,
                Companies     = syncedEntries.SyncedEntries,
                FailedCompanies = syncedEntries.FailedEntries,
                LastUpdatedAt = syncStartedAt
            }, stoppingToken);

            logger.LogInformation("Full company sync complete. {Count} companies entries saved, {Failed} failed.", syncedEntries.SyncedEntries, syncedEntries.FailedEntries);
        }
        else
        {
            logger.LogInformation("Incremental company sync. Last sync was at {LastUpdatedAt}.", syncState.LastUpdatedAt);

            var newEntries = await companyOrchestration.SyncCompaniesIncrementalAsync(syncState, stoppingToken);

            var updatedSyncState = new SyncState
            {
                Companies = newEntries.SyncedEntries,
                FailedCompanies = newEntries.FailedEntries,
                LastUpdatedAt = syncStartedAt
            };
            await syncStateService.AppendCompanySyncStateAsync(SyncTypes.Company, updatedSyncState, syncStartedAt, stoppingToken);

            logger.LogInformation("Incremental company sync complete. {Count} companies entries saved, {Failed} failed.", newEntries.SyncedEntries.Count, newEntries.FailedEntries.Count);

            logger.LogInformation("Failed Companies Sync.");

            var retrySyncedEntries = await companyOrchestration.RetryFailedCompaniesAsync(syncState, stoppingToken);
            if (retrySyncedEntries.Count > 0)
            {
                await syncStateService.AppendCompanySyncStateAsync("Company", new SyncState
                {
                    SyncType = "Company",
                    Companies = retrySyncedEntries,
                    FailedCompanies = []
                }, syncStartedAt, stoppingToken);

                var retriedIds = retrySyncedEntries.Select(x => x.Id).ToList();
                await syncStateService.RemoveFailedCompaniesAsync("Company", retriedIds, stoppingToken);
                logger.LogInformation("Removed {Count} successfully retried companies from the failed list in DynamoDB.", retriedIds.Count);
            }

            logger.LogInformation("Failed companies sync completed. {retried} companies synced in keka. {failedCompanies} companies still failed to sync in keka.", retrySyncedEntries.Count, syncState.FailedCompanies.Count - retrySyncedEntries.Count);
        }
    }
}

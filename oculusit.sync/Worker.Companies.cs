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
            var lastUpdatedAt  = syncedEntries.LastRecordUpdatedAt ?? syncStartedAt;

            await syncStateService.SaveAsync(new SyncState
            {
                SyncType      = SyncTypes.Company,
                Companies     = syncedEntries.SyncedEntries,
                LastUpdatedAt = lastUpdatedAt
            }, stoppingToken);

            await syncStateService.SaveFailedCompaniesAsync(syncedEntries.FailedEntries, lastUpdatedAt, stoppingToken);
            await syncStateService.SaveRetryCompaniesAsync(syncedEntries.RetryEntries, lastUpdatedAt, stoppingToken);

            await syncStateService.SaveCompanySummaryAsync(
                new CompanySyncSummary
                {
                    Total     = syncedEntries.Total,
                    Succeeded = syncedEntries.Succeeded,
                    Failed    = syncedEntries.Failed
                }, lastUpdatedAt, stoppingToken);

            logger.LogInformation(
                "Full company sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
                syncedEntries.Total, syncedEntries.Succeeded, syncedEntries.Failed, lastUpdatedAt);
        }
        else
        {
            logger.LogInformation("Incremental company sync. Last sync was at {LastUpdatedAt}.", syncState.LastUpdatedAt);

            var newEntries    = await companyOrchestration.SyncCompaniesIncrementalAsync(syncState, stoppingToken);
            var lastUpdatedAt  = newEntries.LastRecordUpdatedAt ?? syncStartedAt;

            await syncStateService.AppendCompaniesAsync(SyncTypes.Company, newEntries.SyncedEntries, lastUpdatedAt, stoppingToken);

            await syncStateService.SaveFailedCompaniesAsync(newEntries.FailedEntries, lastUpdatedAt, stoppingToken);
            await syncStateService.SaveRetryCompaniesAsync(newEntries.RetryEntries, lastUpdatedAt, stoppingToken);

            await syncStateService.SaveCompanySummaryAsync(
                new CompanySyncSummary
                {
                    Total     = newEntries.Total,
                    Succeeded = newEntries.Succeeded,
                    Failed    = newEntries.Failed
                }, lastUpdatedAt, stoppingToken);

            logger.LogInformation(
                "Incremental company sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
                newEntries.Total, newEntries.Succeeded, newEntries.Failed, lastUpdatedAt);
        }
    }
}

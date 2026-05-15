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
                LastUpdatedAt = syncStartedAt
            }, stoppingToken);

            await syncStateService.SaveFailedCompaniesAsync(syncedEntries.FailedEntries, syncStartedAt, stoppingToken);

            await syncStateService.SaveCompanySummaryAsync(
                new CompanySyncSummary
                {
                    Total     = syncedEntries.Total,
                    Succeeded = syncedEntries.Succeeded,
                    Failed    = syncedEntries.Failed
                }, syncStartedAt, stoppingToken);

            logger.LogInformation(
                "Full company sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}.",
                syncedEntries.Total, syncedEntries.Succeeded, syncedEntries.Failed);
        }
        else
        {
            logger.LogInformation("Incremental company sync. Last sync was at {LastUpdatedAt}.", syncState.LastUpdatedAt);

            var newEntries = await companyOrchestration.SyncCompaniesIncrementalAsync(syncState, stoppingToken);

            await syncStateService.AppendCompaniesAsync(SyncTypes.Company, newEntries.SyncedEntries, syncStartedAt, stoppingToken);

            await syncStateService.SaveFailedCompaniesAsync(newEntries.FailedEntries, syncStartedAt, stoppingToken);

            await syncStateService.SaveCompanySummaryAsync(
                new CompanySyncSummary
                {
                    Total     = newEntries.Total,
                    Succeeded = newEntries.Succeeded,
                    Failed    = newEntries.Failed
                }, syncStartedAt, stoppingToken);

            logger.LogInformation(
                "Incremental company sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}.",
                newEntries.Total, newEntries.Succeeded, newEntries.Failed);
        }
    }
}

using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task RetryCompaniesFromSyncStateAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        var retryCompanies = await syncStateService.GetRetryCompaniesAsync(stoppingToken);

        var candidateCompanyIds = retryCompanies
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.ErrorMessage))
            .Select(e => e.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Found {Count} retry companies in RetryCompanies SyncState.", candidateCompanyIds.Count);

        if (candidateCompanyIds.Count == 0)
            return;

        var retryResult = await companyOrchestration.RetryCompaniesAsync(candidateCompanyIds, stoppingToken);
        var lastUpdatedAt = retryResult.LastRecordUpdatedAt ?? syncStartedAt;

        await syncStateService.AppendCompaniesAsync(SyncTypes.Company, retryResult.SyncedEntries, lastUpdatedAt, stoppingToken);
        await syncStateService.SaveFailedCompaniesAsync(retryResult.FailedEntries, lastUpdatedAt, stoppingToken);
        await syncStateService.SaveRetryCompaniesAsync(retryResult.RetryEntries, lastUpdatedAt, stoppingToken);
        await syncStateService.SaveDefaultProjectRetriesAsync(retryResult.DefaultProjectRetryEntries, lastUpdatedAt, stoppingToken);

        logger.LogInformation(
            "Pre-sync retry complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}.",
            retryResult.Total, retryResult.Succeeded, retryResult.Failed);
    }

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
            await syncStateService.SaveDefaultProjectRetriesAsync(syncedEntries.DefaultProjectRetryEntries, lastUpdatedAt, stoppingToken);

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
            await syncStateService.SaveDefaultProjectRetriesAsync(newEntries.DefaultProjectRetryEntries, lastUpdatedAt, stoppingToken);

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

    private async Task SyncInitialCompaniesSnapshotAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        var initialCompanySyncState = await syncStateService.GetAsync(SyncTypes.InitialCompany, stoppingToken);
        if (initialCompanySyncState is not null)
        {
            logger.LogInformation("InitialCompany sync state already exists. Skipping InitialCompany snapshot sync.");
            return;
        }

        var initialSnapshot = await companyOrchestration.BuildInitialCompanySnapshotAsync(stoppingToken);

        await syncStateService.SaveAsync(new SyncState
        {
            SyncType         = SyncTypes.InitialCompany,
            InitialCompanies = initialSnapshot,
            LastUpdatedAt    = syncStartedAt
        }, stoppingToken);

        logger.LogInformation(
            "Saved InitialCompany snapshot with {Count} rows before full company sync.",
            initialSnapshot.Count);
    }
}

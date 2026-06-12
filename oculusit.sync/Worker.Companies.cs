using oculusit.sync.core.models;
using oculusit.sync.orchestration;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncCompaniesAsync(
        DateTime syncStartedAt,
        IReadOnlyList<string> retryCompanyIds,
        CancellationToken stoppingToken)
    {
        var syncState = await syncStateService.GetAsync(SyncTypes.Company, stoppingToken);

        if (syncState is null)
        {
            logger.LogInformation("No previous sync state found in DynamoDB. Running full company sync.");

            var syncedEntries = await companyOrchestration.SyncCompaniesToKekaAsync(stoppingToken);
            var lastUpdatedAt = await PersistCompanySyncResultAsync(
                syncedEntries,
                syncStartedAt,
                saveSummary: true,
                stoppingToken);

            logger.LogInformation(
                "Full company sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
                syncedEntries.Total, syncedEntries.Succeeded, syncedEntries.Failed, lastUpdatedAt);

            return;
        }

        logger.LogInformation("Incremental company sync. Last sync was at {LastUpdatedAt}.", syncState.LastUpdatedAt);

        var incrementalResult = await companyOrchestration.SyncCompaniesIncrementalAsync(syncState, retryCompanyIds, stoppingToken);

        var lastUpdatedAtIncremental = await PersistCompanySyncResultAsync(
            incrementalResult,
            syncStartedAt,
            saveSummary: true,
            stoppingToken);

        logger.LogInformation(
            "Incremental company sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
            incrementalResult.Total, incrementalResult.Succeeded, incrementalResult.Failed, lastUpdatedAtIncremental);
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

    private async Task<IReadOnlyList<string>> GetRetryCompanyIdsFromSyncStateAsync(CancellationToken stoppingToken)
    {
        var retryCompanies = await syncStateService.GetRetryCompaniesAsync(stoppingToken);

        var candidateCompanyIds = retryCompanies
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.ErrorMessage))
            .Select(e => e.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Found {Count} retry companies in RetryCompanies SyncState.", candidateCompanyIds.Count);
        return candidateCompanyIds;
    }

    private async Task<IReadOnlyList<FailedCompanyEntry>> GetAllFailedCompaniesAsync(
        IReadOnlyList<SyncedCompanyEntry> syncedEntries,
        IReadOnlyList<FailedCompanyEntry> failedEntries,
        CancellationToken stoppingToken)
    {
        var failedState = await syncStateService.GetAsync(SyncTypes.FailedCompanies, stoppingToken);
        var failedCompaniesFromDb = failedState?.FailedCompanies ?? [];

        if (failedCompaniesFromDb.Count == 0)
            return failedEntries;

        var failedCompanies = new List<FailedCompanyEntry>();

        foreach (var dbFailedCompany in failedCompaniesFromDb)
        {
            if (string.IsNullOrWhiteSpace(dbFailedCompany.Id))
                continue;

            if (syncedEntries.Any(e =>
                !string.IsNullOrWhiteSpace(e.Id)
                && string.Equals(e.Id, dbFailedCompany.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (failedEntries.Any(e =>
                !string.IsNullOrWhiteSpace(e.Id)
                && string.Equals(e.Id, dbFailedCompany.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            //If failedCompanies id doesnot exist in syncedEntries and failedEntries then that failed company is added to failed company list
            failedCompanies.Add(dbFailedCompany);
        }

        foreach (var failedEntry in failedEntries)
            failedCompanies.Add(failedEntry);

        return failedCompanies;
    }

    private async Task<DateTime> PersistCompanySyncResultAsync(
        CompanySyncResult result,
        DateTime syncStartedAt,
        bool saveSummary,
        CancellationToken stoppingToken)
    {
        var lastUpdatedAt = result.LastRecordUpdatedAt ?? syncStartedAt;

        await syncStateService.UpsertCompaniesAsync(SyncTypes.Company, result.SyncedEntries, lastUpdatedAt, stoppingToken);

        var failedCompanies = await GetAllFailedCompaniesAsync(result.SyncedEntries, result.FailedEntries, stoppingToken);

        await syncStateService.SaveFailedCompaniesAsync(failedCompanies, lastUpdatedAt, stoppingToken);
        await syncStateService.SaveRetryCompaniesAsync(result.RetryEntries, lastUpdatedAt, stoppingToken);

        if (saveSummary)
        {
            await syncStateService.SaveCompanySummaryAsync(
                new CompanySyncSummary
                {
                    Total     = result.Total,
                    Succeeded = result.Succeeded,
                    Failed    = result.Failed
                }, lastUpdatedAt, stoppingToken);
        }

        return lastUpdatedAt;
    }
}

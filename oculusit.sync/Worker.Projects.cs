using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncInitialProjectsSnapshotAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        var initialProjectSyncState = await syncStateService.GetAsync(SyncTypes.InitialProject, stoppingToken);
        if (initialProjectSyncState is not null)
        {
            logger.LogInformation("InitialProject sync state already exists. Skipping InitialProject snapshot sync.");
            return;
        }

        var initialSnapshot = await projectOrchestration.BuildInitialProjectSnapshotAsync(stoppingToken);

        await syncStateService.SaveAsync(new SyncState
        {
            SyncType        = SyncTypes.InitialProject,
            InitialProjects = initialSnapshot,
            LastUpdatedAt   = syncStartedAt
        }, stoppingToken);

        logger.LogInformation(
            "Saved InitialProject snapshot with {Count} rows before full project sync.",
            initialSnapshot.Count);
    }

    private async Task<IReadOnlyList<FailedProjectEntry>> GetAllFailedProjectsAsync(
        IReadOnlyList<SyncedProjectEntry> syncedEntries,
        IReadOnlyList<FailedProjectEntry> failedEntries,
        CancellationToken stoppingToken)
    {
        var failedState = await syncStateService.GetAsync(SyncTypes.FailedProjects, stoppingToken);
        var failedProjectsFromDb = failedState?.FailedProjects ?? [];

        if (failedProjectsFromDb.Count == 0)
            return failedEntries;

        var failedProjects = new List<FailedProjectEntry>();

        foreach (var dbFailedProject in failedProjectsFromDb)
        {
            if (string.IsNullOrWhiteSpace(dbFailedProject.Id))
                continue;

            var existsInSyncedEntries = syncedEntries.Any(e =>
                !string.IsNullOrWhiteSpace(e.Id)
                && string.Equals(e.Id, dbFailedProject.Id, StringComparison.OrdinalIgnoreCase));

            var existsInFailedEntries = failedEntries.Any(e =>
                !string.IsNullOrWhiteSpace(e.Id)
                && string.Equals(e.Id, dbFailedProject.Id, StringComparison.OrdinalIgnoreCase));

            if (existsInSyncedEntries || existsInFailedEntries)
                continue;

            failedProjects.Add(dbFailedProject);
        }

        foreach (var failedEntry in failedEntries)
            failedProjects.Add(failedEntry);

        return failedProjects;
    }

    private async Task SyncProjectsAsync(
        DateTime syncStartedAt,
        IReadOnlyList<string> retryProjectIds,
        CancellationToken stoppingToken)
    {
        // Company sync state is required to resolve Keka client IDs from ConnectWise company IDs.
        var companySyncState = await syncStateService.GetAsync(SyncTypes.Company, stoppingToken);
        if (companySyncState is null)
        {
            logger.LogWarning("Company sync state not found. Skipping project sync — run company sync first.");
            return;
        }

        // Project Status sync state provides the project status mappings (value → numeric mappedValue).
        var projectStatusSyncState = await syncStateService.GetAsync(SyncTypes.ProjectStatus, stoppingToken);

        var projectSyncState = await syncStateService.GetAsync(SyncTypes.Project, stoppingToken);

        if (projectSyncState is null)
        {
            logger.LogInformation("No previous project sync state found. Running full project sync.");

            var result        = await projectOrchestration.SyncProjectsAsync(companySyncState, projectStatusSyncState, stoppingToken);
            var lastUpdatedAt  = result.LastRecordUpdatedAt ?? syncStartedAt;

            await syncStateService.SaveAsync(new SyncState
            {
                SyncType      = SyncTypes.Project,
                Projects      = result.SyncedEntries,
                LastUpdatedAt = lastUpdatedAt
            }, stoppingToken);

            await syncStateService.SaveFailedProjectsAsync(result.FailedEntries, lastUpdatedAt, stoppingToken);
            await syncStateService.SaveRetryProjectsAsync(result.RetryEntries, lastUpdatedAt, stoppingToken);

            await syncStateService.SaveProjectSummaryAsync(
                new ProjectSyncSummary
                {
                    Total     = result.Total,
                    Succeeded = result.Succeeded,
                    Failed    = result.Failed
                }, lastUpdatedAt, stoppingToken);

            logger.LogInformation(
                "Full project sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
                result.Total, result.Succeeded, result.Failed, lastUpdatedAt);
        }
        else
        {
            logger.LogInformation("Incremental project sync. Last sync was at {LastUpdatedAt}.", projectSyncState.LastUpdatedAt);

            var result        = await projectOrchestration.SyncProjectsIncrementalAsync(projectSyncState, companySyncState, projectStatusSyncState, retryProjectIds, stoppingToken);
            var lastUpdatedAt  = result.LastRecordUpdatedAt ?? syncStartedAt;

            await syncStateService.UpsertProjectsAsync(SyncTypes.Project, result.SyncedEntries, lastUpdatedAt, stoppingToken);

            var failedProjects = await GetAllFailedProjectsAsync(result.SyncedEntries, result.FailedEntries, stoppingToken);
            await syncStateService.SaveFailedProjectsAsync(failedProjects, lastUpdatedAt, stoppingToken);
            await syncStateService.SaveRetryProjectsAsync(result.RetryEntries, lastUpdatedAt, stoppingToken);

            await syncStateService.SaveProjectSummaryAsync(
                new ProjectSyncSummary
                {
                    Total     = result.Total,
                    Succeeded = result.Succeeded,
                    Failed    = result.Failed
                }, lastUpdatedAt, stoppingToken);

            logger.LogInformation(
                "Incremental project sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
                result.Total, result.Succeeded, result.Failed, lastUpdatedAt);
        }
    }

    private async Task<IReadOnlyList<string>> GetRetryProjectIdsFromSyncStateAsync(CancellationToken stoppingToken)
    {
        var retryProjectsSyncState = await syncStateService.GetAsync(SyncTypes.RetryProjects, stoppingToken);

        var candidateProjectIds = retryProjectsSyncState?.Projects
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Select(e => e.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        logger.LogInformation("Found {Count} retry projects in RetryProjects SyncState.", candidateProjectIds.Count);
        return candidateProjectIds;
    }
}

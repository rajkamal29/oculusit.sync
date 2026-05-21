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

    private async Task SyncProjectsAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        // Company sync state is required to resolve Keka client IDs from ConnectWise company IDs.
        var companySyncState = await syncStateService.GetAsync(SyncTypes.Company, stoppingToken);
        if (companySyncState is null)
        {
            logger.LogWarning("Company sync state not found. Skipping project sync — run company sync first.");
            return;
        }

        // Metadata sync state provides the project status mappings (value → numeric mappedValue).
        var metadataSyncState = await syncStateService.GetAsync(SyncTypes.Metadata, stoppingToken);

        var projectSyncState = await syncStateService.GetAsync(SyncTypes.Project, stoppingToken);

        if (projectSyncState is null)
        {
            logger.LogInformation("No previous project sync state found. Running full project sync.");

            var result        = await projectOrchestration.SyncProjectsAsync(companySyncState, metadataSyncState, stoppingToken);
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

            var result        = await projectOrchestration.SyncProjectsIncrementalAsync(projectSyncState, companySyncState, metadataSyncState, stoppingToken);
            var lastUpdatedAt  = result.LastRecordUpdatedAt ?? syncStartedAt;

            await syncStateService.AppendProjectsAsync(SyncTypes.Project, result.SyncedEntries, lastUpdatedAt, stoppingToken);

            // Always overwrite failed projects so stale failures from previous runs are cleared.
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
                "Incremental project sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}. LastRecordUpdatedAt: {LastRecordUpdatedAt}.",
                result.Total, result.Succeeded, result.Failed, lastUpdatedAt);
        }
    }
}

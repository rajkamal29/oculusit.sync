using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
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

            var result = await projectOrchestration.SyncProjectsAsync(companySyncState, metadataSyncState, stoppingToken);

            await syncStateService.SaveAsync(new SyncState
            {
                SyncType      = SyncTypes.Project,
                Projects      = result.SyncedEntries,
                LastUpdatedAt = syncStartedAt
            }, stoppingToken);

            await syncStateService.SaveFailedProjectsAsync(result.FailedEntries, syncStartedAt, stoppingToken);

            await syncStateService.SaveProjectSummaryAsync(
                new ProjectSyncSummary
                {
                    Total     = result.Total,
                    Succeeded = result.Succeeded,
                    Failed    = result.Failed
                }, syncStartedAt, stoppingToken);

            logger.LogInformation(
                "Full project sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}.",
                result.Total, result.Succeeded, result.Failed);
        }
        else
        {
            logger.LogInformation("Incremental project sync. Last sync was at {LastUpdatedAt}.", projectSyncState.LastUpdatedAt);

            var result = await projectOrchestration.SyncProjectsIncrementalAsync(projectSyncState, companySyncState, metadataSyncState, stoppingToken);

            await syncStateService.AppendProjectsAsync(SyncTypes.Project, result.SyncedEntries, syncStartedAt, stoppingToken);

            // Always overwrite failed projects so stale failures from previous runs are cleared.
            await syncStateService.SaveFailedProjectsAsync(result.FailedEntries, syncStartedAt, stoppingToken);

            await syncStateService.SaveProjectSummaryAsync(
                new ProjectSyncSummary
                {
                    Total     = result.Total,
                    Succeeded = result.Succeeded,
                    Failed    = result.Failed
                }, syncStartedAt, stoppingToken);

            logger.LogInformation(
                "Incremental project sync complete. Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}.",
                result.Total, result.Succeeded, result.Failed);
        }
    }
}

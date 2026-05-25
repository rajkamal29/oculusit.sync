using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncProjectStatusAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting project status sync.");

        try
        {
            // Load existing project status entries so MappedValues can be preserved across runs.
            var existing = await syncStateService.GetAsync(SyncTypes.ProjectStatus, stoppingToken);
            var existingStatuses = existing?.ProjectStatuses ?? [];

            var result = await projectStatusOrchestration.SyncProjectStatusesAsync(existingStatuses, stoppingToken);

            // Always save so that LastUpdatedAt reflects when the job last checked.
            await syncStateService.SaveProjectStatusAsync(result.Entries, syncStartedAt, stoppingToken);

            // Clear any failure recorded from a previous run now that this run succeeded.
            await syncStateService.SaveFailedProjectStatusAsync(null, syncStartedAt, stoppingToken);

            if (result.HasChanges)
                logger.LogInformation(
                    "Project status sync complete — changes detected. Added={Added}, Updated={Updated}, Deleted={Deleted}. {Total} project status entries saved.",
                    result.Added, result.Updated, result.Deleted, result.Entries.Count);
            else
                logger.LogInformation(
                    "Project status sync complete — no changes detected. {Count} project status entries unchanged. LastUpdatedAt refreshed.",
                    result.Entries.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Project status sync failed. The failure will be recorded in DynamoDB.");

            try
            {
                var failure = new FailedProjectStatusEntry { ErrorMessage = ex.Message };
                await syncStateService.SaveFailedProjectStatusAsync(failure, syncStartedAt, stoppingToken);
            }
            catch (Exception innerEx)
            {
                // Swallow — we must not let a logging failure crash the worker.
                logger.LogError(innerEx, "Failed to persist project status sync failure to DynamoDB.");
            }
        }
    }
}

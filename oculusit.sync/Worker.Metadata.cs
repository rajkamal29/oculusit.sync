using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncMetadataAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting metadata sync.");

        try
        {
            // Load existing metadata so MappedValues can be preserved across runs.
            var existing = await syncStateService.GetAsync(SyncTypes.Metadata, stoppingToken);
            var existingStatuses = existing?.ProjectStatuses ?? [];

            var result = await metadataOrchestration.SyncProjectStatusesAsync(existingStatuses, stoppingToken);

            // Always save so that LastUpdatedAt reflects when the job last checked.
            await syncStateService.SaveMetadataAsync(result.Entries, syncStartedAt, stoppingToken);

            // Clear any failure recorded from a previous run now that this run succeeded.
            await syncStateService.SaveFailedMetadataAsync(null, syncStartedAt, stoppingToken);

            if (result.HasChanges)
                logger.LogInformation(
                    "Metadata sync complete — changes detected. Added={Added}, Updated={Updated}, Deleted={Deleted}. {Total} project status entries saved.",
                    result.Added, result.Updated, result.Deleted, result.Entries.Count);
            else
                logger.LogInformation(
                    "Metadata sync complete — no changes detected. {Count} project status entries unchanged. LastUpdatedAt refreshed.",
                    result.Entries.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metadata sync failed. The failure will be recorded in DynamoDB.");

            try
            {
                var failure = new FailedMetadataEntry { ErrorMessage = ex.Message };
                await syncStateService.SaveFailedMetadataAsync(failure, syncStartedAt, stoppingToken);
            }
            catch (Exception innerEx)
            {
                // Swallow — we must not let a logging failure crash the worker.
                logger.LogError(innerEx, "Failed to persist metadata sync failure to DynamoDB.");
            }
        }
    }
}

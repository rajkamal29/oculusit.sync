using oculusit.sync.core.models;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncMetadataAsync(DateTime syncStartedAt, CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting metadata sync.");

        // Load existing metadata so MappedValues can be preserved across runs.
        var existing = await syncStateService.GetAsync(SyncTypes.Metadata, stoppingToken);
        var existingStatuses = existing?.ProjectStatuses ?? [];

        var entries = await metadataOrchestration.SyncProjectStatusesAsync(existingStatuses, stoppingToken);

        await syncStateService.SaveMetadataAsync(entries, syncStartedAt, stoppingToken);

        logger.LogInformation("Metadata sync complete. {Count} project status entries saved.", entries.Count);
    }
}

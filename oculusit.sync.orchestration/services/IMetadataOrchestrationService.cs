namespace oculusit.sync.orchestration;

public interface IMetadataOrchestrationService
{
    /// <summary>
    /// Fetches all project statuses from ConnectWise, merges with any existing MappedValues,
    /// and returns the resolved list ready to persist.
    /// </summary>
    Task<IReadOnlyList<oculusit.sync.core.models.ProjectStatusEntry>> SyncProjectStatusesAsync(
        IReadOnlyList<oculusit.sync.core.models.ProjectStatusEntry> existing,
        CancellationToken cancellationToken = default);
}

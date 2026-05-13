using oculusit.sync.core.models;

namespace oculusit.sync.orchestration;

public interface IMetadataOrchestrationService
{
    /// <summary>
    /// Fetches all project statuses from ConnectWise, compares against the existing DynamoDB entries,
    /// and returns a <see cref="MetadataSyncResult"/> indicating whether any changes were detected
    /// and what the fully merged list looks like.
    /// The caller should only persist when <see cref="MetadataSyncResult.HasChanges"/> is true.
    /// </summary>
    Task<MetadataSyncResult> SyncProjectStatusesAsync(
        IReadOnlyList<ProjectStatusEntry> existing,
        CancellationToken cancellationToken = default);
}

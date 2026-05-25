using oculusit.sync.core.models;

namespace oculusit.sync.orchestration;

public interface IProjectStatusOrchestrationService
{
    /// <summary>
    /// Fetches all project statuses from ConnectWise, compares against the existing DynamoDB entries,
    /// and returns a <see cref="ProjectStatusSyncResult"/> indicating whether any changes were detected
    /// and what the fully merged list looks like.
    /// The caller should only persist when <see cref="ProjectStatusSyncResult.HasChanges"/> is true.
    /// </summary>
    Task<ProjectStatusSyncResult> SyncProjectStatusesAsync(
        IReadOnlyList<ProjectStatusEntry> existing,
        CancellationToken cancellationToken = default);
}

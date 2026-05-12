using oculusit.sync.core.models;

namespace oculusit.sync.orchestration;

public interface IProjectOrchestrationService
{
    /// <summary>
    /// Full sync — fetches all ConnectWise projects and records them.
    /// <paramref name="companySyncState"/> is used to resolve the Keka client ID.
    /// <paramref name="metadataSyncState"/> provides the project status mapping from DynamoDB metadata.
    /// </summary>
    Task<ProjectSyncResult> SyncProjectsAsync(
        SyncState companySyncState,
        SyncState? metadataSyncState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Incremental sync — fetches only projects updated since <paramref name="projectSyncState"/>.LastUpdatedAt.
    /// <paramref name="companySyncState"/> is used to resolve the Keka client ID.
    /// <paramref name="metadataSyncState"/> provides the project status mapping from DynamoDB metadata.
    /// Returns newly created entries and any failures.
    /// </summary>
    Task<ProjectSyncResult> SyncProjectsIncrementalAsync(
        SyncState projectSyncState,
        SyncState companySyncState,
        SyncState? metadataSyncState,
        CancellationToken cancellationToken = default);
}

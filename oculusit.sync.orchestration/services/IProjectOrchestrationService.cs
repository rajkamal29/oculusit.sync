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

/// <summary>Result returned by project sync operations.</summary>
public sealed class ProjectSyncResult
{
    public IReadOnlyList<SyncedProjectEntry> SyncedEntries { get; init; } = [];
    public IReadOnlyList<FailedProjectEntry> FailedEntries { get; init; } = [];

    /// <summary>Total projects processed in this run.</summary>
    public int Total { get; init; }

    /// <summary>Number of projects successfully synced (created or updated).</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of projects that failed to sync.</summary>
    public int Failed { get; init; }
}

namespace oculusit.sync.core.models;

/// <summary>Carries the outcome of a project sync run: successfully synced entries and failed entries.</summary>
public sealed class ProjectSyncResult
{
    /// <summary>Projects that were successfully created or updated in Keka.</summary>
    public IReadOnlyList<SyncedProjectEntry> SyncedEntries { get; init; } = [];

    /// <summary>Projects that failed to sync to Keka.</summary>
    public IReadOnlyList<FailedProjectEntry> FailedEntries { get; init; } = [];
}

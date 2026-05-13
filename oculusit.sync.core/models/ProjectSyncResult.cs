namespace oculusit.sync.core.models;

/// <summary>Carries the outcome of a project sync run: successfully synced entries and failed entries.</summary>
public sealed class ProjectSyncResult
{
    /// <summary>Projects that were successfully created or updated in Keka.</summary>
    public IReadOnlyList<SyncedProjectEntry> SyncedEntries { get; init; } = [];

    /// <summary>Projects that failed to sync to Keka.</summary>
    public IReadOnlyList<FailedProjectEntry> FailedEntries { get; init; } = [];
}

/// <summary>Carries the outcome of a metadata sync comparison between ConnectWise and the existing DynamoDB state.</summary>
public sealed class MetadataSyncResult
{
    /// <summary>The fully merged list of project status entries to persist (if HasChanges is true).</summary>
    public IReadOnlyList<ProjectStatusEntry> Entries { get; init; } = [];

    /// <summary>True when at least one entry was added, updated, or deleted compared to the existing state.</summary>
    public bool HasChanges { get; init; }

    /// <summary>Number of new statuses added from ConnectWise.</summary>
    public int Added { get; init; }

    /// <summary>Number of existing statuses whose name (Value) changed.</summary>
    public int Updated { get; init; }

    /// <summary>Number of statuses removed because they no longer exist in ConnectWise.</summary>
    public int Deleted { get; init; }
}

using oculusit.sync.core.models;

namespace oculusit.sync.core.interfaces;

public interface ISyncStateService
{
    /// <summary>Returns the sync state for the given sync type, or null if none exists.</summary>
    Task<SyncState?> GetAsync(string syncType, CancellationToken cancellationToken = default);

    /// <summary>Persists the sync state for the given sync type.</summary>
    Task SaveAsync(SyncState state, CancellationToken cancellationToken = default);

    /// <summary>Appends new project entries to the existing Projects list and updates LastUpdatedAt.</summary>
    Task AppendProjectsAsync(string syncType, IReadOnlyList<SyncedProjectEntry> newEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>Replaces the FailedProjects list with the provided entries so the latest failure set is always current.</summary>
    Task SaveFailedProjectsAsync(string syncType, IReadOnlyList<FailedProjectEntry> failedEntries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends incremental company and failed-company entries and updates LastUpdatedAt.
    /// </summary>
    Task AppendCompanySyncStateAsync(string syncType, SyncState incrementalState, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes failed companies by their IDs from the sync state in DynamoDB.
    /// </summary>
    Task RemoveFailedCompaniesAsync(string syncType, IReadOnlyList<string> failedCompanyIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully replaces the ProjectStatuses metadata list each run.
    /// New entries have an empty MappedValue; existing MappedValues are preserved on update.
    /// </summary>
    Task SaveMetadataAsync(IReadOnlyList<ProjectStatusEntry> entries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);
}

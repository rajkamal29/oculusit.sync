using oculusit.sync.core.models;

namespace oculusit.sync.core.interfaces;

public interface ISyncStateService
{
    /// <summary>Returns the sync state for the given sync type, or null if none exists.</summary>
    Task<SyncState?> GetAsync(string syncType, CancellationToken cancellationToken = default);

    /// <summary>Persists the sync state for the given sync type.</summary>
    Task SaveAsync(SyncState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends incremental company and failed-company entries and updates LastUpdatedAt.
    /// </summary>
    Task AppendSyncStateAsync(string syncType, SyncState incrementalState, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes failed companies by their IDs from the sync state in DynamoDB.
    /// </summary>
    Task RemoveFailedCompaniesAsync(string syncType, IReadOnlyList<string> failedCompanyIds, CancellationToken cancellationToken = default);
}

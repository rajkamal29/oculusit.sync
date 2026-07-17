using oculusit.sync.core.models;
using oculusit.sync.keka.modules;

namespace oculusit.sync.orchestration;

public interface IProjectOrchestrationService
{
    /// <summary>
    /// Full sync — fetches all ConnectWise projects and records them.
    /// <paramref name="companySyncState"/> is used to resolve the Keka client ID.
    /// <paramref name="projectStatusSyncState"/> provides the project status mapping from DynamoDB.
    /// <paramref name="allEmployeesState"/> provides all employees deduplication state from DynamoDB.
    /// </summary>
    Task<ProjectSyncResult> SyncProjectsAsync(
        SyncState companySyncState,
        SyncState? projectStatusSyncState,
        string defaultBillingType,
        IReadOnlyList<TimeEntryEmployeeDedupeState> allEmployeesState,
        KekaEmployee? defaultProjectManager,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Incremental sync — fetches projects updated since <paramref name="projectSyncState"/>.LastUpdatedAt,
    /// merges with retry projects from RetryProjects sync state, and processes unique project IDs.
    /// <paramref name="companySyncState"/> is used to resolve the Keka client ID.
    /// <paramref name="projectStatusSyncState"/> provides the project status mapping from DynamoDB.
    /// Returns newly created entries and any failures.
    /// </summary>
    Task<ProjectSyncResult> SyncProjectsIncrementalAsync(
        SyncState projectSyncState,
        SyncState companySyncState,
        SyncState? projectStatusSyncState,
        string defaultBillingType,
        IReadOnlyList<TimeEntryEmployeeDedupeState> allEmployeesState,
        IReadOnlyList<string> retryProjectIds,
        KekaEmployee? defaultProjectManager,
        CancellationToken cancellationToken = default);
}

/// <summary>Result returned by project sync operations.</summary>
public sealed class ProjectSyncResult
{
    public IReadOnlyList<SyncedProjectEntry> SyncedEntries { get; init; } = [];
    public IReadOnlyList<FailedProjectEntry> FailedEntries { get; init; } = [];

    /// <summary>Projects that timed out — stored under the Retry syncType for next-run retry.</summary>
    public IReadOnlyList<RetryProjectEntry> RetryEntries { get; init; } = [];

    /// <summary>
    /// The <c>lastUpdated</c> value of the last record fetched from ConnectWise, ordered ascending.
    /// Used as <c>LastUpdatedAt</c> in DynamoDB so the next incremental run starts exactly from here.
    /// Falls back to the worker's <c>syncStartedAt</c> when no records were fetched.
    /// </summary>
    public DateTime? LastRecordUpdatedAt { get; init; }

    /// <summary>Total projects processed in this run.</summary>
    public int Total { get; init; }

    /// <summary>Number of projects successfully synced (created or updated).</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of projects that failed to sync.</summary>
    public int Failed { get; init; }
}

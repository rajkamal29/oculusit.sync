using oculusit.sync.core.models;

namespace oculusit.sync.core.interfaces;

public interface ISyncStateService
{
    /// <summary>Returns the sync state for the given sync type, or null if none exists.</summary>
    Task<SyncState?> GetAsync(string syncType, CancellationToken cancellationToken = default);

    /// <summary>Persists the sync state for the given sync type.</summary>
    Task SaveAsync(SyncState state, CancellationToken cancellationToken = default);

    /// <summary>Appends new company entries to the existing Companies list and updates LastUpdatedAt.</summary>
    Task AppendCompaniesAsync(string syncType, IReadOnlyList<SyncedCompanyEntry> newEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>Appends new project entries to the existing Projects list and updates LastUpdatedAt.</summary>
    Task AppendProjectsAsync(string syncType, IReadOnlyList<SyncedProjectEntry> newEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>projects</c> attribute on the <c>Failures</c> record with the latest failed project entries.
    /// Pass an empty list to clear all failures after a clean run.
    /// </summary>
    Task SaveFailedProjectsAsync(IReadOnlyList<FailedProjectEntry> failedEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the companies and lastUpdatedAt on the <c>Failures</c> record with the latest failed company entries.
    /// Pass an empty list to clear all failures after a clean run.
    /// </summary>
    Task SaveFailedCompaniesAsync(IReadOnlyList<FailedCompanyEntry> failedEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>companies</c> attribute on the <c>Retry</c> record with companies that timed out this run.
    /// Pass an empty list to clear after a clean run.
    /// </summary>
    Task SaveRetryCompaniesAsync(IReadOnlyList<RetryCompanyEntry> retryEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>projects</c> attribute on the <c>Retry</c> record with projects that timed out this run.
    /// Pass an empty list to clear after a clean run.
    /// </summary>
    Task SaveRetryProjectsAsync(IReadOnlyList<RetryProjectEntry> retryEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>summary</c> attribute on the <c>Company</c> record with the latest run counts.
    /// </summary>
    Task SaveCompanySummaryAsync(CompanySyncSummary summary, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>summary</c> attribute on the <c>Project</c> record with the latest run counts.
    /// </summary>
    Task SaveProjectSummaryAsync(ProjectSyncSummary summary, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fully replaces the Project Statuses list each run.
    /// New entries have an empty MappedValue; existing MappedValues are preserved on update.
    /// </summary>
    Task SaveProjectStatusAsync(IReadOnlyList<ProjectStatusEntry> entries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a Project Status sync failure against the Project Status record.
    /// Pass null to clear the failure (i.e. reset after a successful run).
    /// </summary>
    Task SaveFailedProjectStatusAsync(FailedProjectStatusEntry? failure, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);
}

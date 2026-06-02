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
    /// Overwrites the <c>failedProjects</c> attribute on the <c>FailedProject</c> record with the latest failed project entries.
    /// Pass an empty list to clear all failures after a clean run.
    /// </summary>
    Task SaveFailedProjectsAsync(IReadOnlyList<FailedProjectEntry> failedEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>failedCompanies</c> attribute on the <c>FailedCompany</c> record with the latest failed company entries.
    /// Pass an empty list to clear all failures after a clean run.
    /// </summary>
    Task SaveFailedCompaniesAsync(IReadOnlyList<FailedCompanyEntry> failedEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>companies</c> attribute on the <c>RetryCompanies</c> record with companies that timed out this run.
    /// Pass an empty list to clear after a clean run.
    /// </summary>
    Task SaveRetryCompaniesAsync(IReadOnlyList<RetryCompanyEntry> retryEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads retry companies from the <c>RetryCompanies</c> record.
    /// Each entry includes company id/name and error message.
    /// </summary>
    Task<IReadOnlyList<RetryCompanyEntry>> GetRetryCompaniesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the <c>projects</c> attribute on the <c>RetryProjects</c> record with projects that timed out this run.
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

    /// <summary>
    /// Returns all per-employee time-entry checkpoint records where syncType starts with TimeEntries#.
    /// </summary>
    Task<IReadOnlyList<TimeEntryEmployeeDedupeState>> GetTimeEntryEmployeeDedupeStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns employee checkpoint records that still need processing for the provided previous-week dedupe key.
    /// </summary>
    Task<IReadOnlyList<TimeEntryEmployeeDedupeState>> GetTimeEntryEmployeeDedupeStatesToSyncAsync(string previousWeekDedupeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the successful-sync checkpoint for a single employee where syncType pattern is TimeEntries#{employeeId}.
    /// Returns null when no record exists.
    /// </summary>
    Task<TimeEntryEmployeeDedupeState?> GetTimeEntryEmployeeDedupeStateAsync(string employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the successful-sync checkpoint for a single employee using syncType pattern TimeEntries#{employeeId}.
    /// DedupeKey should represent the latest successfully synced UTC week start key (for example yyyyMMdd).
    /// </summary>
    Task UpsertTimeEntryEmployeeDedupeStateAsync(TimeEntryEmployeeDedupeState state, CancellationToken cancellationToken = default);
}

using oculusit.sync.core.models;

namespace oculusit.sync.core.interfaces;

public interface ISyncStateService
{
    /// <summary>Returns the sync state for the given sync type, or null if none exists.</summary>
    Task<SyncState?> GetAsync(string syncType, CancellationToken cancellationToken = default);

    /// <summary>Persists the sync state for the given sync type.</summary>
    Task SaveAsync(SyncState state, CancellationToken cancellationToken = default);

    /// <summary>Merges company entries into the existing Companies list by ID (update if exists, add if new) and updates LastUpdatedAt.</summary>
    Task UpsertCompaniesAsync(string syncType, IReadOnlyList<SyncedCompanyEntry> newEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>Merges project entries into the existing Projects list by ID (update if exists, add if new) and updates LastUpdatedAt.</summary>
    Task UpsertProjectsAsync(string syncType, IReadOnlyList<SyncedProjectEntry> newEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

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
    /// Overwrites the <c>timeSheets</c> attribute on the <c>RetryTimeSheets</c> record with timesheets that timed out this run.
    /// Pass an empty list to clear after a clean run.
    /// </summary>
    Task SaveRetryTimeSheetsAsync(IReadOnlyList<RetryTimeSheetEntry> retryEntries, DateTime lastUpdatedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads retry timesheets from the <c>RetryTimeSheets</c> record.
    /// Each entry includes timesheet id and context for retry processing.
    /// </summary>
    Task<IReadOnlyList<RetryTimeSheetEntry>> GetRetryTimeSheetsAsync(CancellationToken cancellationToken = default);

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
    /// Returns employee checkpoint records that still need processing for the given year and period.
    /// Includes employees with no period set, or whose last synced year/period is before the given values.
    /// </summary>
    Task<IReadOnlyList<TimeEntryEmployeeDedupeState>> GetTimeEntryEmployeeDedupeStatesToSyncAsync(int year, int period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the successful-sync checkpoint for a single employee where syncType pattern is TimeEntries#{employeeId}.
    /// Returns null when no record exists.
    /// </summary>
    Task<TimeEntryEmployeeDedupeState?> GetTimeEntryEmployeeDedupeStateAsync(string employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the checkpoint for a single employee using syncType pattern TimeEntries#{employeeId}.
    /// SyncedPeriods is the full updated map of year → synced period set and is written in full each call.
    /// </summary>
    Task UpsertTimeEntryEmployeeDedupeStateAsync(TimeEntryEmployeeDedupeState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the DefaultProject sync type exists in the database.
    /// If it doesn't exist, creates it with default project manager information (Jason William).
    /// </summary>
    Task EnsureDefaultProjectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the Time off sync type exists in the database.
    /// If it doesn't exist, creates it with default work type (Personal,PTO,Sick,Vacation,Holiday).
    /// </summary>
    Task EnsureTimeOffSyncTypeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads Time Off sync type from the <c>TimeOff</c> record.
    /// Returns the time off work type to ignore syncing the time entries.
    /// </summary>
    Task<string> GetTimeOffSyncTypeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the BillingType sync type exists in the database.
    /// If it doesn't exist, creates it with default billing type as 1 (Fixed Fee).
    /// </summary>
    Task EnsureBillingTypeAsync(CancellationToken cancellationToken = default);
}

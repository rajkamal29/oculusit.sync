namespace oculusit.sync.core.models;

public sealed class SyncState
{
    /// <summary>Partition key — identifies the type of sync (e.g. "Company", "Project", "Project Staus").</summary>
    public string SyncType { get; init; } = string.Empty;

    /// <summary>Company-to-Keka client mappings captured during the sync run.</summary>
    public IReadOnlyList<SyncedCompanyEntry> Companies { get; init; } = [];

    /// <summary>
    /// Initial full-outer-join snapshot between ConnectWise companies and Keka clients.
    /// Stored under <c>SyncType = "InitialCompany"</c>.
    /// </summary>
    public IReadOnlyList<InitialCompanyEntry> InitialCompanies { get; init; } = [];

    /// <summary>Project mappings captured during the sync run.</summary>
    public IReadOnlyList<SyncedProjectEntry> Projects { get; init; } = [];

    /// <summary>
    /// Initial full-outer-join snapshot between ConnectWise projects and Keka projects.
    /// Stored under <c>SyncType = "InitialProject"</c>.
    /// </summary>
    public IReadOnlyList<InitialProjectEntry> InitialProjects { get; init; } = [];

    /// <summary>Companies that failed to sync during the most recent run.</summary>
    public IReadOnlyList<FailedCompanyEntry> FailedCompanies { get; init; } = [];

    /// <summary>Project status entries — full replace on every run.</summary>
    public IReadOnlyList<ProjectStatusEntry> ProjectStatuses { get; init; } = [];

    /// <summary>Failure record from the most recent project status sync run. Empty when the last run succeeded.</summary>
    public FailedProjectStatusEntry? FailedProjectStatuses { get; init; }

    /// <summary>Run-level summary for the Company sync — total processed, succeeded, and failed.</summary>
    public CompanySyncSummary? Summary { get; init; }

    /// <summary>Run-level summary for the Project sync — total processed, succeeded, and failed.</summary>
    public ProjectSyncSummary? ProjectSummary { get; init; }

    /// <summary>UTC timestamp of the last successful sync completion.</summary>
    public DateTime? LastUpdatedAt { get; init; }
}

/// <summary>Aggregated counts for a single company sync run.</summary>
public sealed class CompanySyncSummary
{
    /// <summary>Total number of companies processed in this run.</summary>
    public int Total { get; init; }

    /// <summary>Number of companies successfully synced (created or updated).</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of companies that failed to sync.</summary>
    public int Failed { get; init; }
}

/// <summary>Aggregated counts for a single project sync run.</summary>
public sealed class ProjectSyncSummary
{
    /// <summary>Total number of projects processed in this run.</summary>
    public int Total { get; init; }

    /// <summary>Number of projects successfully synced (created or updated).</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of projects that failed to sync.</summary>
    public int Failed { get; init; }
}

/// <summary>Records the mapping between a ConnectWise company ID and its Keka client ID.</summary>
public sealed class SyncedCompanyEntry
{
    /// <summary>ConnectWise company ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Keka client ID.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>UTC date the company was created in ConnectWise (_info.dateEntered).</summary>
    public DateTime? DateEntered { get; init; }
}

/// <summary>Records a synced ConnectWise project.</summary>
public sealed class InitialCompanyEntry
{
    /// <summary>ConnectWise company ID.</summary>
    public string CompanyId { get; init; } = string.Empty;

    /// <summary>ConnectWise company name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>Keka client ID.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Keka client code.</summary>
    public string ClientCode { get; init; } = string.Empty;

    /// <summary>Keka client name.</summary>
    public string ClientName { get; init; } = string.Empty;
}

/// <summary>Records an initial full-outer-join row between ConnectWise and Keka projects.</summary>
public sealed class InitialProjectEntry
{
    /// <summary>ConnectWise project ID.</summary>
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>ConnectWise project name.</summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>Keka project ID.</summary>
    public string KekaProjectId { get; init; } = string.Empty;

    /// <summary>Keka project code.</summary>
    public string KekaProjectCode { get; init; } = string.Empty;

    /// <summary>Keka project name.</summary>
    public string KekaProjectName { get; init; } = string.Empty;
}

/// <summary>Records a synced ConnectWise project.</summary>
public sealed class SyncedProjectEntry
{
    /// <summary>ConnectWise project ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Keka client ID resolved from the company sync state via ConnectWise company ID.</summary>
    public string? KekaClientId { get; init; }

    /// <summary>Keka project ID.</summary>
    public string? KekaProjectId { get; init; }

    /// <summary>
    /// Short-code keys of tasks that failed to be created on the last run.
    /// These will be retried on the next update pass via the Keka API.
    /// Empty means all 6 tasks were successfully provisioned.
    /// </summary>
    public List<string> FailedTaskKeys { get; init; } = [];
}

/// <summary>Records a ConnectWise project that failed to sync to Keka.</summary>
public sealed class FailedProjectEntry
{
    /// <summary>ConnectWise project ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ConnectWise project name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Exception message that caused the failure.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>Records a ConnectWise company that failed to sync to Keka.</summary>
public sealed class FailedCompanyEntry
{
    /// <summary>ConnectWise company ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ConnectWise company name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Exception message that caused the failure.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Records a ConnectWise company that timed out during sync.
/// Stored under the <c>Retry</c> syncType so it can be retried on the next run.
/// </summary>
public sealed class RetryCompanyEntry
{
    /// <summary>ConnectWise company ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ConnectWise company name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Timeout message captured from the exception.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Records a ConnectWise project that timed out during sync.
/// Stored under the <c>Retry</c> syncType so it can be retried on the next run.
/// </summary>
public sealed class RetryProjectEntry
{
    /// <summary>ConnectWise project ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ConnectWise project name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Timeout message captured from the exception.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>Records a project status sync failure from the most recent run.</summary>
public sealed class FailedProjectStatusEntry
{
    /// <summary>Exception message that caused the project status sync to fail.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>Entry for a ConnectWise project status and its optional Keka mapped value.</summary>
public sealed class ProjectStatusEntry
{
    /// <summary>ConnectWise project status ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ConnectWise project status name.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Mapped Keka value — set manually after initial insert; preserved on updates.</summary>
    public string MappedValue { get; init; } = string.Empty;
}


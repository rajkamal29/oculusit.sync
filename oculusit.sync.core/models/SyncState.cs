namespace oculusit.sync.core.models;

public sealed class SyncState
{
    /// <summary>Partition key — identifies the type of sync (e.g. "Company", "Project", "Metadata").</summary>
    public string SyncType { get; init; } = string.Empty;

    /// <summary>Company-to-Keka client mappings captured during the sync run.</summary>
    public IReadOnlyList<SyncedCompanyEntry> Companies { get; init; } = [];

    /// <summary>Project mappings captured during the sync run.</summary>
    public IReadOnlyList<SyncedProjectEntry> Projects { get; init; } = [];

    /// <summary>Projects that failed to sync during the most recent run.</summary>
    public IReadOnlyList<FailedProjectEntry> FailedProjects { get; init; } = [];

    /// <summary>Project status metadata entries — full replace on every run.</summary>
    public IReadOnlyList<ProjectStatusEntry> ProjectStatuses { get; init; } = [];

    /// <summary>Failure record from the most recent metadata sync run. Empty when the last run succeeded.</summary>
    public FailedMetadataEntry? FailedProjectStatuses { get; init; }

    /// <summary>UTC timestamp of the last successful sync completion.</summary>
    public DateTime? LastUpdatedAt { get; init; }

    /// <summary>Failed company sync attempts with error messages.</summary>
    public IReadOnlyList<FailedCompanyEntry> FailedCompanies { get; init; } = [];
}

public class FailedCompanyEntry
{
    public string Id { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>Records the mapping between a ConnectWise company ID and its Keka client ID.</summary>
public sealed class SyncedCompanyEntry
{
    /// <summary>ConnectWise company ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Keka client ID.</summary>
    public string ClientId { get; init; } = string.Empty;
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

/// <summary>Records a metadata sync failure from the most recent run.</summary>
public sealed class FailedMetadataEntry
{
    /// <summary>Exception message that caused the metadata sync to fail.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}

/// <summary>Metadata entry for a ConnectWise project status and its optional Keka mapped value.</summary>
public sealed class ProjectStatusEntry
{
    /// <summary>ConnectWise project status ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>ConnectWise project status name.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Mapped Keka value — set manually after initial insert; preserved on updates.</summary>
    public string MappedValue { get; init; } = string.Empty;
}


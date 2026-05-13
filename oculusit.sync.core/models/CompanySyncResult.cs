namespace oculusit.sync.core.models;

/// <summary>Carries the outcome of a Company sync run: successfully synced entries and failed entries.</summary>
public sealed class CompanySyncResult
{
    /// <summary>Company that were successfully created or updated in Keka.</summary>
    public IReadOnlyList<SyncedCompanyEntry> SyncedEntries { get; init; } = [];

    /// <summary>Company that failed to sync to Keka.</summary>
    public IReadOnlyList<FailedCompanyEntry> FailedEntries { get; init; } = [];
}

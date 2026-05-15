using oculusit.sync.core.models;

namespace oculusit.sync.orchestration;

public interface ICompanyOrchestrationService
{
    /// <summary>
    /// Full sync — fetches all ConnectWise companies and creates or updates Keka clients.
    /// Returns all synced company-to-client mappings and any failures.
    /// </summary>
    Task<CompanySyncResult> SyncCompaniesToKekaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Incremental sync — fetches only companies updated since <paramref name="syncState"/>.LastUpdatedAt
    /// and creates or updates Keka clients.
    /// Returns only newly created company-to-client mappings and any failures.
    /// </summary>
    Task<CompanySyncResult> SyncCompaniesIncrementalAsync(SyncState syncState, CancellationToken cancellationToken = default);
}

/// <summary>Result returned by company sync operations.</summary>
public sealed class CompanySyncResult
{
    public IReadOnlyList<SyncedCompanyEntry> SyncedEntries { get; init; } = [];
    public IReadOnlyList<FailedCompanyEntry> FailedEntries { get; init; } = [];

    /// <summary>Total companies processed in this run.</summary>
    public int Total { get; init; }

    /// <summary>Number of companies successfully synced (created or updated).</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of companies that failed to sync.</summary>
    public int Failed { get; init; }
}

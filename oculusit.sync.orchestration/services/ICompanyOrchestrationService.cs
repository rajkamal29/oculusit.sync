using oculusit.sync.core.models;

namespace oculusit.sync.orchestration;

public interface ICompanyOrchestrationService
{
    /// <summary>
    /// Builds an initial full-outer-join snapshot between ConnectWise companies and Keka clients.
    /// Join key: ConnectWise company Id == Keka client Code.
    /// </summary>
    Task<IReadOnlyList<InitialCompanyEntry>> BuildInitialCompanySnapshotAsync(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Retries a specific set of ConnectWise company IDs before regular company sync.
    /// </summary>
    Task<CompanySyncResult> RetryCompaniesAsync(IReadOnlyList<string> companyIds, CancellationToken cancellationToken = default);
}

/// <summary>Result returned by company sync operations.</summary>
public sealed class CompanySyncResult
{
    public IReadOnlyList<SyncedCompanyEntry> SyncedEntries { get; init; } = [];
    public IReadOnlyList<FailedCompanyEntry> FailedEntries { get; init; } = [];

    /// <summary>Companies that timed out — stored under the Retry syncType for next-run retry.</summary>
    public IReadOnlyList<RetryCompanyEntry> RetryEntries { get; init; } = [];

    /// <summary>
    /// Default project creation timeout records keyed by company/client.
    /// Persisted under syncType DefaultProjectRetry for later retry.
    /// </summary>
    public IReadOnlyList<DefaultProjectRetryEntry> DefaultProjectRetryEntries { get; init; } = [];

    /// <summary>
    /// The <c>lastUpdated</c> value of the last record fetched from ConnectWise, ordered ascending.
    /// Used as <c>LastUpdatedAt</c> in DynamoDB so the next incremental run starts exactly from here.
    /// Falls back to the worker's <c>syncStartedAt</c> when no records were fetched.
    /// </summary>
    public DateTime? LastRecordUpdatedAt { get; init; }

    /// <summary>Total companies processed in this run.</summary>
    public int Total { get; init; }

    /// <summary>Number of companies successfully synced (created or updated).</summary>
    public int Succeeded { get; init; }

    /// <summary>Number of companies that failed to sync.</summary>
    public int Failed { get; init; }
}

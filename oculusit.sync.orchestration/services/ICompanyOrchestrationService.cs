using oculusit.sync.core.models;

namespace oculusit.sync.orchestration;

public interface ICompanyOrchestrationService
{
    /// <summary>
    /// Full sync — fetches all ConnectWise companies and creates or updates Keka clients.
    /// Returns all company-to-client mappings.
    /// </summary>
    Task<CompanySyncResult> SyncCompaniesToKekaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Incremental sync — fetches only companies updated since <paramref name="syncState"/>.LastUpdatedAt
    /// and creates or updates Keka clients.
    /// Existing mappings are resolved from <paramref name="syncState"/>.Companies (no Keka list fetch).
    /// Returns incremental sync including newly created mappings and failed companies.
    /// </summary>
    Task<CompanySyncResult> SyncCompaniesIncrementalAsync(SyncState syncState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries previously failed companies from <paramref name="syncState"/>.FailedCompanies.
    /// Returns synced company entries.
    /// </summary>
    Task<IReadOnlyList<SyncedCompanyEntry>> RetryFailedCompaniesAsync(
        SyncState syncState,
        CancellationToken cancellationToken = default);
}

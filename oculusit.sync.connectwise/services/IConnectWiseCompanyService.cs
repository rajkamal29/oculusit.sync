using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public interface IConnectWiseCompanyService
{
    /// <summary>
    /// Fetches all companies from ConnectWise by paginating through all pages
    /// and returns the complete list stored in memory.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseCompany>> GetAllCompaniesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches only companies updated at or after <paramref name="since"/> (UTC),
    /// ordered by lastUpdated ascending.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesSinceAsync(DateTime since, CancellationToken cancellationToken = default);


    /// <summary>
    /// Fetches specific companies by their IDs from ConnectWise.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesByIdsAsync(IReadOnlyList<int> companyIds, CancellationToken cancellationToken = default);
}

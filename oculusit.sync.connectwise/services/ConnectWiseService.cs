using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

/// <summary>
/// Facade that satisfies <see cref="IConnectWiseService"/> by delegating to
/// <see cref="ConnectWiseCompanyService"/> and <see cref="ConnectWiseProjectService"/>.
/// </summary>
public sealed class ConnectWiseService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseService
{
    private readonly ConnectWiseCompanyService _companies = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseCompanyService>.Instance);

    private readonly ConnectWiseProjectService _projects = new(httpClientFactory, config,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectWiseProjectService>.Instance);

    public Task<IReadOnlyList<ConnectWiseCompany>> GetAllCompaniesAsync(CancellationToken cancellationToken = default)
        => _companies.GetAllCompaniesAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesSinceAsync(DateTime since, CancellationToken cancellationToken = default)
        => _companies.GetCompaniesSinceAsync(since, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesByIdsAsync(IReadOnlyList<int> companyIds, CancellationToken cancellationToken = default)
        => _companies.GetCompaniesByIdsAsync(companyIds, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProject>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
        => _projects.GetAllProjectsAsync(cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProject>> GetProjectsSinceAsync(DateTime since, CancellationToken cancellationToken = default)
        => _projects.GetProjectsSinceAsync(since, cancellationToken);

    public Task<IReadOnlyList<ConnectWiseProjectStatus>> GetAllProjectStatusesAsync(CancellationToken cancellationToken = default)
        => _projects.GetAllProjectStatusesAsync(cancellationToken);
}

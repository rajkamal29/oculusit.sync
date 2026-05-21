using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseCompanyService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseCompanyService> logger)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseCompanyService
{
    private const string Fields =
        "id,identifier,name,status,type,addressLine1,addressLine2,city,state,zip,country,phoneNumber,faxNumber,website,invoiceCCEmailAddress,_info,dateAcquired";

    public async Task<IReadOnlyList<ConnectWiseCompany>> GetAllCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting full ConnectWise company fetch with page size {PageSize}", Config.PageSize);

        var results = await FetchPagedAsync<ConnectWiseCompany>(
            relativeUrlBase: "/company/companies",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: null,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Completed ConnectWise company fetch. Total companies loaded: {Total}", results.Count);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesSinceAsync(
        DateTime since, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting incremental ConnectWise company fetch since {Since}", since);

        var condition = $"lastUpdated >= '{since:yyyy-MM-ddTHH:mm:ssZ}'";

        var results = await FetchPagedAsync<ConnectWiseCompany>(
            relativeUrlBase: "/company/companies",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Completed incremental ConnectWise company fetch. Total companies loaded: {Total}", results.Count);

        return results;
    }
}

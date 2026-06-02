using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseMemberService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseMemberService> logger)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseMemberService
{
    private const string Fields = "id,identifier,firstName,lastName,officeEmail,defaultEmail,inactiveFlag";

    public async Task<IReadOnlyList<ConnectWiseMember>> GetAllMembersAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting ConnectWise member fetch with page size {PageSize}.", Config.PageSize);

        var results = await FetchPagedAsync<ConnectWiseMember>(
            relativeUrlBase: "/system/members",
            fields: Fields,
            orderBy: "identifier asc",
            conditions: "inactiveFlag = false",
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Completed ConnectWise member fetch. Total active members loaded: {Total}.", results.Count);
        return results;
    }
}

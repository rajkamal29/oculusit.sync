using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseProjectService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseProjectService> logger)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseProjectService
{
    private const string Fields =
        "id,name,status,company,contact,type,manager," +
        "estimatedStart,estimatedEnd,actualStart,actualEnd," +
        "description,notes,_info";

    public async Task<IReadOnlyList<ConnectWiseProject>> GetAllProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting full ConnectWise project fetch with page size {PageSize}", Config.PageSize);

        var results = await FetchPagedAsync<ConnectWiseProject>(
            relativeUrlBase: "/project/projects",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: null,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Completed ConnectWise project fetch. Total projects loaded: {Total}", results.Count);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseProject>> GetProjectsSinceAsync(
        DateTime since, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting incremental ConnectWise project fetch since {Since}", since);

        var condition = $"lastUpdated >= '{since:yyyy-MM-ddTHH:mm:ssZ}'";

        var results = await FetchPagedAsync<ConnectWiseProject>(
            relativeUrlBase: "/project/projects",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Completed incremental ConnectWise project fetch. Total projects loaded: {Total}", results.Count);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseProject>> GetProjectsByIdsAsync(
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
            return [];

        var ids = projectIds
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var condition = string.Join(" OR ", ids.Select(id => $"id = {id}"));

        logger.LogInformation("Fetching {Count} specific ConnectWise projects by id list.", ids.Count);

        var results = await FetchPagedAsync<ConnectWiseProject>(
            relativeUrlBase: "/project/projects",
            fields: Fields,
            orderBy: "id asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Fetched {Count} ConnectWise projects by id list.", results.Count);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseProjectStatus>> GetAllProjectStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching all ConnectWise project statuses.");

        var results = await FetchPagedAsync<ConnectWiseProjectStatus>(
            relativeUrlBase: "/project/statuses",
            fields: "id,name",
            orderBy: "id asc",
            conditions: null,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation("Fetched {Count} ConnectWise project statuses.", results.Count);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseProjectTeamMember>> GetProjectMembersAsync(int projectId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fetching team members for ConnectWise project {ProjectId}", projectId);

        var relativeUrl = $"/project/projects/{projectId}/teamMembers";

        using var request = CreateRequest(HttpMethod.Get, relativeUrl);
        using var response = await HttpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"ConnectWise API error fetching project members for {projectId}: {response.StatusCode} - {error}");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var members = await System.Text.Json.JsonSerializer.DeserializeAsync<List<ConnectWiseProjectTeamMember>>(stream, JsonOptions, cancellationToken);

        var result = members ?? new List<ConnectWiseProjectTeamMember>();

        logger.LogInformation("Fetched {Count} team members for ConnectWise project {ProjectId}", result.Count, projectId);

        return result;
    }
}

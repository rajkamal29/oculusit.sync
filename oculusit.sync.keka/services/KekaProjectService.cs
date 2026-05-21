using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.modules;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class KekaProjectService(
    IHttpClientFactory httpClientFactory,
    IOptions<KekaConfiguration> config,
    IKekaTokenService tokenService,
    ILogger<KekaProjectService> logger) : IKekaProjectService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(KekaProjectService));
    private readonly KekaConfiguration _config = config.Value;
    private readonly ILogger<KekaProjectService> _logger = logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task SetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task RefreshAuthHeaderAsync(CancellationToken cancellationToken)
    {
        var token = await tokenService.RefreshAccessTokenAsync(cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private Uri BuildUri(string relativePath) =>
        new(new Uri(_config.ApiBaseUrl), $"/api/v1{relativePath}");

    public async Task<IReadOnlyList<KekaProject>> GetAllProjectsAsync(CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var allProjects = new List<KekaProject>();
        var pageNumber = 1;
        bool hasMoreItems;

        do
        {
            var uri = new Uri(BuildUri("/psa/projects"), $"?pageNumber={pageNumber}");
            _logger.LogDebug("Fetching Keka projects page {PageNumber}.", pageNumber);

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Received 401 fetching Keka projects page {PageNumber}. Refreshing token.", pageNumber);
                await RefreshAuthHeaderAsync(cancellationToken);
                response = await _httpClient.GetAsync(uri, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to fetch Keka projects page {PageNumber}. StatusCode: {StatusCode}, Body: {Body}",
                    pageNumber, response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka GET /psa/projects?pageNumber={pageNumber} failed ({(int)response.StatusCode}): {errorBody}",
                    null, response.StatusCode);
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<KekaDataListResponse<KekaProject>>(_jsonOptions, cancellationToken);

            if (envelope?.Data is { Count: > 0 } page)
                allProjects.AddRange(page);

            hasMoreItems = pageNumber < envelope?.TotalPages;
            pageNumber++;
        }
        while (hasMoreItems);

        _logger.LogInformation("Fetched {Count} Keka projects total.", allProjects.Count);
        return allProjects;
    }

    public async Task<string> CreateProjectAsync(KekaProjectRequest request, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri("/psa/projects");
        _logger.LogDebug("Creating Keka project with name {Name}.", request.Name);

        var response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 creating Keka project. Refreshing token.");
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create Keka project. StatusCode: {StatusCode}, Body: {Body}",
                response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka POST /psa/projects failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaCreateProjectResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded || string.IsNullOrEmpty(envelope.Data))
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka create project for '{request.Name}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully created Keka project {KekaProjectId} for name {Name}.",
            envelope.Data, request.Name);
        return envelope.Data;
    }

    public async Task UpdateProjectAsync(string projectId, KekaProjectUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri($"/psa/projects/{projectId}");
        _logger.LogDebug("Updating Keka project {ProjectId}.", projectId);

        var response = await _httpClient.PutAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 updating Keka project {ProjectId}. Refreshing token.", projectId);
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PutAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to update Keka project {ProjectId}. StatusCode: {StatusCode}, Body: {Body}",
                projectId, response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka PUT /psa/projects/{projectId} failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaUpdateProjectResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded)
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka update project '{projectId}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully updated Keka project {ProjectId}.", projectId);
    }

    public async Task<string> CreateTaskAsync(string projectId, KekaTaskRequest request, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri($"/psa/projects/{projectId}/tasks");
        _logger.LogDebug("Creating Keka task '{Name}' for project {ProjectId}.", request.Name, projectId);

        var response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 creating Keka task '{Name}'. Refreshing token.", request.Name);
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create Keka task '{Name}' for project {ProjectId}. StatusCode: {StatusCode}, Body: {Body}",
                request.Name, projectId, response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka POST /psa/projects/{projectId}/tasks failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaCreateTaskResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded || string.IsNullOrEmpty(envelope.Data))
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka create task '{request.Name}' for project '{projectId}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully created Keka task {TaskId} ('{Name}') for project {ProjectId}.",
            envelope.Data, request.Name, projectId);
        return envelope.Data;
    }

    public async Task<IReadOnlyList<KekaTask>> GetTasksByProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri($"/psa/projects/{projectId}/tasks");
        _logger.LogDebug("Fetching tasks for Keka project {ProjectId}.", projectId);

        var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 fetching tasks for Keka project {ProjectId}. Refreshing token.", projectId);
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.GetAsync(uri, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to fetch tasks for Keka project {ProjectId}. StatusCode: {StatusCode}, Body: {Body}",
                projectId, response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka GET /psa/projects/{projectId}/tasks failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaDataListResponse<KekaTask>>(_jsonOptions, cancellationToken);

        var tasks = envelope?.Data ?? [];
        _logger.LogInformation("Fetched {Count} existing tasks for Keka project {ProjectId}.", tasks.Count, projectId);
        return tasks;
    }

    public async Task<string> CreateProjectAllocationAsync(
        string projectId,
        KekaProjectAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri($"/psa/projects/{projectId}/allocations");
        _logger.LogDebug("Creating Keka project allocation for project {ProjectId}, employee {EmployeeId}.",
            projectId, request.EmployeeId);

        var response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 creating Keka project allocation for project {ProjectId}. Refreshing token.", projectId);
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create Keka project allocation for project {ProjectId}. StatusCode: {StatusCode}, Body: {Body}",
                projectId, response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka POST /psa/projects/{projectId}/allocations failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaCreateProjectAllocationResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded || string.IsNullOrEmpty(envelope.Data))
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka create project allocation for project '{projectId}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully created Keka project allocation {AllocationId} for project {ProjectId}.",
            envelope.Data, projectId);
        return envelope.Data;
    }

    public async Task<IReadOnlyList<KekaProjectAllocation> GetProjectAllocationsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var allAllocations = new List<KekaProjectAllocation>();
        var pageNumber = 1;

        while (true)
        {
            var uri = new Uri(BuildUri($"/psa/projects/{projectId}/allocations"), $"?pageNumber={pageNumber}");
            _logger.LogDebug("Fetching allocations for Keka project {ProjectId}, page {PageNumber}.", projectId, pageNumber);

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Received 401 fetching allocations for Keka project {ProjectId}, page {PageNumber}. Refreshing token.", projectId, pageNumber);
                await RefreshAuthHeaderAsync(cancellationToken);
                response = await _httpClient.GetAsync(uri, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to fetch allocations for Keka project {ProjectId}, page {PageNumber}. StatusCode: {StatusCode}, Body: {Body}",
                    projectId, pageNumber, response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka GET /psa/projects/{projectId}/allocations?pageNumber={pageNumber} failed ({(int)response.StatusCode}): {errorBody}",
                    null, response.StatusCode);
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<KekaProjectAllocationListResponse>(_jsonOptions, cancellationToken);

            if (envelope?.Data is { Count: > 0 })
                allAllocations.AddRange(envelope.Data);

            if (pageNumber >= envelope?.TotalPages)
                break;

            pageNumber++;
        }

        _logger.LogInformation("Fetched {Count} allocations for Keka project {ProjectId}.", allAllocations.Count, projectId);
        return allAllocations;
    }
}

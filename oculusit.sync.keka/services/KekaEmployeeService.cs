using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.modules;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class KekaEmployeeService(
    IHttpClientFactory httpClientFactory,
    IOptions<KekaConfiguration> config,
    IKekaTokenService tokenService,
    ILogger<KekaEmployeeService> logger,
    ISyncStateService syncStateService) : IKekaEmployeeService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();
    private readonly KekaConfiguration _config = config.Value;
    private readonly ILogger<KekaEmployeeService> _logger = logger;

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

    public async Task<KekaEmployee?> SearchEmployeeByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri("/hris/employees/search");
        _logger.LogDebug("Searching Keka employee by email {Email}.", email);

        var request = new KekaEmployeeSearchRequest
        {
            WorkEmail = email,
            WorkPhone = null
        };

        var response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 searching Keka employee by email {Email}. Refreshing token.", email);
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Keka employee not found for email {Email}.", email);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Keka employee search by email {Email} did not succeed. StatusCode: {StatusCode}, Body: {Body}",
                email, response.StatusCode, errorBody);
            return null;
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaGetEmployeeResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded)
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            _logger.LogWarning(
                "Keka search employee by email {Email} did not return a valid employee. Message: {Message}. Errors: {Errors}",
                email,
                envelope?.Message,
                errors);
            return null;
        }

        if (envelope.Data is null || string.IsNullOrWhiteSpace(envelope.Data.Id))
        {
            _logger.LogWarning("Keka search employee by email {Email} returned no employee data.", email);
            return null;
        }

        _logger.LogInformation("Successfully searched Keka employee by email {Email}.", email);
        return envelope.Data;
    }

    public async Task<IReadOnlyList<KekaEmployee>> GetAllEmployeeAsync(CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var allEmployees = new List<KekaEmployee>();
        var pageNumber = 1;
        bool hasMoreItems;

        do
        {
            var uri = new Uri(BuildUri("/hris/employees"), $"?pageNumber={pageNumber}");
            _logger.LogDebug("Fetching Keka employees page {PageNumber}.", pageNumber);

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Received 401 fetching Keka employees page {PageNumber}. Refreshing token.", pageNumber);
                await RefreshAuthHeaderAsync(cancellationToken);
                response = await _httpClient.GetAsync(uri, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to fetch Keka employees page {PageNumber}. StatusCode: {StatusCode}, Body: {Body}",
                    pageNumber, response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka GET /hris/employees?pageNumber={pageNumber} failed ({(int)response.StatusCode}): {errorBody}",
                    null, response.StatusCode);
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<KekaDataListResponse<KekaEmployee>>(_jsonOptions, cancellationToken);

            if (envelope?.Data is { Count: > 0 } page)
                allEmployees.AddRange(page);

            hasMoreItems = pageNumber < envelope?.TotalPages;
            pageNumber++;
        }
        while (hasMoreItems);

        _logger.LogInformation("Fetched {Count} Keka employees total.", allEmployees.Count);
        return allEmployees;
    }

    public async Task<string> CreateEmployeeAsync(KekaEmployeeRequest request, CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri("/hris/employees");
        _logger.LogDebug("Creating Keka employee with name {Name}.", request.DisplayName);

        var response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 creating Keka employee. Refreshing token.");
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create Keka employee. StatusCode: {StatusCode}, Body: {Body}",
                response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka POST /hris/employees failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaCreateEmployeeResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded || string.IsNullOrEmpty(envelope.Data))
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka create employee for '{request.DisplayName}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully created Keka employee {KekaEmployeeId} for name {Name}.",
            envelope.Data, request.DisplayName);
        return envelope.Data;
    }

    public async Task<KekaEmployee?> GetDefaultProjectManagerEmployeeAsync(CancellationToken cancellationToken = default)
    {
        var defaultProjectSyncState = await syncStateService.GetAsync(SyncTypes.DefaultProject, cancellationToken);

        if (defaultProjectSyncState is null)
        {
            _logger.LogWarning("Default Project syncType not found in database");
            return null;
        }

        var email = defaultProjectSyncState.DefaultProject?.ProjectManager.Email ?? string.Empty;

        var defaultProjectManagerDetails = await SearchEmployeeByEmailAsync(email, cancellationToken);

        if (defaultProjectManagerDetails is null)
        {
            _logger.LogWarning("Keka search default project manager employee by email {Email} returned no employee data.", email);
            return null;
        }

        _logger.LogInformation("Successfully searched Keka default project manager employee by email {Email}.", email);
        return defaultProjectManagerDetails;
    }
}
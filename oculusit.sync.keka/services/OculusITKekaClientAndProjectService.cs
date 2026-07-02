using Microsoft.Extensions.Logging;
using oculusit.sync.keka.modules;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class OculusITKekaClientAndProjectService(
    IHttpClientFactory httpClientFactory,
    ILogger<OculusITKekaClientAndProjectService> logger) : IOculusITKekaClientAndProjectService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(OculusITKekaClientAndProjectService));
    private readonly ILogger<OculusITKekaClientAndProjectService> _logger = logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task SetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        var token = string.Empty;
        try
        {
            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "2607138d-c9eb-4377-ab4f-d7a51f402b30",
                ["client_secret"] = "S6pz8Ghueb6rYsOGy0Nj",
                ["grant_type"] = "kekaapi",
                ["scope"] = "kekaapi",
                ["api_key"] = "SxGhjyOtK2mMn7J4qukULTvnHm7u8iJIgnNpjB9OSto="
            });

            var requestUri = new Uri(new Uri("https://login.keka.com"), "/connect/token");
            var response = await _httpClient.PostAsync(requestUri, formContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Keka token request failed. StatusCode: {StatusCode}, Body: {Body}",
                    response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka token request failed with status {response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseBody)
                ?? throw new InvalidOperationException("Keka token response could not be deserialized.");

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Keka token response returned an empty access token.");
            }

            token = tokenResponse.AccessToken;

            _logger.LogInformation(
                "Keka access token fetched successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error while fetching oculusIT Access Token. ErrorMessage: {errorMessage}", ex);
        }
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private Uri BuildUri(string relativePath) =>
        new(new Uri("https://oculusit.keka.com"), $"/api/v1{relativePath}");

    public async Task<IReadOnlyList<KekaClient>> GetAllOculusITKekaClientsAsync(CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var allClients = new List<KekaClient>();
        var pageNumber = 1;
        bool hasMoreItems;

        do
        {
            var uri = new Uri(BuildUri("/psa/clients"), $"?pageNumber={pageNumber}");
            _logger.LogDebug("Fetching Keka clients page {PageNumber}.", pageNumber);

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Received 401 fetching Keka clients page {PageNumber}. Refreshing token.", pageNumber);
                await SetAuthHeaderAsync(cancellationToken);
                response = await _httpClient.GetAsync(uri, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to fetch Keka clients page {PageNumber}. StatusCode: {StatusCode}, Body: {Body}",
                    pageNumber, response.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Keka GET /psa/clients?pageNumber={pageNumber} failed ({(int)response.StatusCode}): {errorBody}",
                    null, response.StatusCode);
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<KekaDataListResponse<KekaClient>>(_jsonOptions, cancellationToken);

            if (envelope?.Data is { Count: > 0 } page)
                allClients.AddRange(page);

            hasMoreItems = pageNumber < envelope?.TotalPages;
            pageNumber++;
        }
        while (hasMoreItems);

        _logger.LogInformation("Fetched {Count} OculusIT Keka clients total.", allClients.Count);
        return allClients;
    }

    public async Task<IReadOnlyList<KekaProject>> GetAllOculusITKekaProjectsAsync(CancellationToken cancellationToken = default)
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
                await SetAuthHeaderAsync(cancellationToken);
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

        _logger.LogInformation("Fetched {Count} OculusIT Keka projects total.", allProjects.Count);
        return allProjects;
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    ILogger<KekaEmployeeService> logger) : IKekaEmployeeService
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
            _logger.LogError("Failed to search Keka employee by email {Email}. StatusCode: {StatusCode}, Body: {Body}",
                email, response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka POST /hris/employees/search failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaGetEmployeeResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded)
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka search employee for '{email}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully searched Keka employee by email {Email}.", email);
        return envelope.Data;
    }
}

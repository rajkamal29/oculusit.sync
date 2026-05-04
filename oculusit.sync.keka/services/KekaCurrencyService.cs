using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.modules;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class KekaCurrencyService(
    IHttpClientFactory httpClientFactory,
    IOptions<KekaConfiguration> config,
    IKekaTokenService tokenService,
    ILogger<KekaCurrencyService> logger) : IKekaCurrencyService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(KekaCurrencyService));
    private readonly KekaConfiguration _config = config.Value;
    private readonly ILogger<KekaCurrencyService> _logger = logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Uri BuildUri(string relativePath) =>
        new(new Uri(_config.ApiBaseUrl), $"/api/v1{relativePath}");

    public async Task<string?> GetUsdCurrencyIdAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var uri = BuildUri("/hris/currencies");
        _logger.LogDebug("Fetching currencies from Keka to resolve USD currency ID");

        var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 fetching Keka currencies. Refreshing token.");
            var refreshedToken = await tokenService.RefreshAccessTokenAsync(cancellationToken);
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", refreshedToken);
            response = await _httpClient.GetAsync(uri, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to fetch Keka currencies. StatusCode: {StatusCode}, Body: {Body}",
                response.StatusCode, errorBody);
            return null;
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaDataListResponse<KekaCurrency>>(_jsonOptions, cancellationToken);

        var usd = envelope?.Data?.FirstOrDefault(c =>
            string.Equals(c.Code, "USD", StringComparison.OrdinalIgnoreCase));

        if (usd is null)
            _logger.LogWarning("USD currency not found in Keka currency list.");
        else
            _logger.LogInformation("Resolved USD currency ID: {CurrencyId}", usd.Id);

        return usd?.Id;
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.modules;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class KekaFinanceService(
    IHttpClientFactory httpClientFactory,
    IOptions<KekaConfiguration> config,
    IKekaTokenService tokenService,
    ILogger<KekaFinanceService> logger) : IKekaFinanceService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(KekaFinanceService));
    private readonly KekaConfiguration _config = config.Value;
    private readonly ILogger<KekaFinanceService> _logger = logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Uri BuildUri(string relativePath) =>
        new(new Uri(_config.ApiBaseUrl), $"/api/v1{relativePath}");

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

    public async Task<string?> GetBillingRoleIdAsync(string departmentName, CancellationToken cancellationToken = default)
    {
        string? billingRoleId = null;
        var pageNumber = 1;

        while (true)
        {
            await SetAuthHeaderAsync(cancellationToken);

            var uri = BuildUri("/psa/finances/ratecards");
            _logger.LogDebug("Fetching all Keka rate cards.");

            var response = await _httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Received 401 fetching Keka rate cards. Refreshing token.");
                await RefreshAuthHeaderAsync(cancellationToken);
                response = await _httpClient.GetAsync(uri, cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to fetch Keka rate cards. StatusCode: {StatusCode}, Body: {Body}",
                    response.StatusCode, errorBody);
                return billingRoleId;
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<KekaDataListResponse<KekaRateCard>>(_jsonOptions, cancellationToken);

            if (envelope?.Data is { Count: > 0 })
            {
                billingRoleId = envelope.Data.FirstOrDefault(d => string.Equals(d.RoleName, departmentName, StringComparison.OrdinalIgnoreCase))?.BillingRoleId;

                if (!string.IsNullOrEmpty(billingRoleId))
                {
                    _logger.LogInformation("Found billing role ID {BillingRoleId} for department {DepartmentName}.", billingRoleId, departmentName);
                    break;
                }
            }

            if (pageNumber >= envelope?.TotalPages)
            {
                _logger.LogInformation("No billing role found for department {DepartmentName} after searching all available rate cards.", departmentName);
                break;
            }

            pageNumber++;
        }

        return billingRoleId;
    }
}

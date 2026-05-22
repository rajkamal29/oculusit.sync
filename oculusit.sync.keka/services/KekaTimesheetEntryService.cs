using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.modules;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class KekaTimesheetEntryService(
    IHttpClientFactory httpClientFactory,
    IOptions<KekaConfiguration> config,
    IKekaTokenService tokenService,
    ILogger<KekaTimesheetEntryService> logger) : IKekaTimesheetEntryService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(KekaTimesheetEntryService));
    private readonly KekaConfiguration _config = config.Value;
    private readonly ILogger<KekaTimesheetEntryService> _logger = logger;

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

    public async Task<bool> CreateTimesheetEntryAsync(
        string employeeId,
        KekaTimesheetEntryBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        await SetAuthHeaderAsync(cancellationToken);

        var uri = BuildUri($"/psa/employees/{employeeId}/timeentries");
        _logger.LogDebug("Creating Keka timesheet entry for employee {EmployeeId}.",
            employeeId);

        var response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 creating Keka timesheet entry for employee {EmployeeId}. Refreshing token.", employeeId);
            await RefreshAuthHeaderAsync(cancellationToken);
            response = await _httpClient.PostAsJsonAsync(uri, request, _jsonOptions, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create Keka timesheet entry for employee {EmployeeId}. StatusCode: {StatusCode}, Body: {Body}",
                employeeId, response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Keka POST /psa/employees/{employeeId}/timeentries failed ({(int)response.StatusCode}): {errorBody}",
                null, response.StatusCode);
        }

        var envelope = await response.Content
            .ReadFromJsonAsync<KekaAddTimesheetEntryResponse>(_jsonOptions, cancellationToken);

        if (envelope is null || !envelope.Succeeded)
        {
            var errors = envelope?.Errors is { Count: > 0 } e ? string.Join(", ", e) : "none";
            throw new InvalidOperationException(
                $"Keka create timesheet entry for employee '{employeeId}' failed. Message: {envelope?.Message}. Errors: {errors}");
        }

        _logger.LogInformation("Successfully created Keka timesheet entry for employee {EmployeeId}.", employeeId);
        return envelope.Data;
    }
}

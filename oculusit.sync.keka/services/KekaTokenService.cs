using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.modules;
using System.Text.Json;

namespace oculusit.sync.keka.services;

public sealed class KekaTokenService(
    IHttpClientFactory httpClientFactory,
    IOptions<KekaConfiguration> config,
    ILogger<KekaTokenService> logger) : IKekaTokenService, IDisposable
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(KekaTokenService));
    private readonly KekaConfiguration _config = config.Value;
    private readonly ILogger<KekaTokenService> _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsTokenValid())
        {
            _logger.LogDebug("Returning cached Keka access token. Expires at {ExpiresAt}", _tokenExpiresAt);
            return _cachedToken!;
        }

        return await FetchTokenAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Forcing Keka access token refresh.");

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Invalidate the current cache
            _cachedToken = null;
            _tokenExpiresAt = DateTime.MinValue;
        }
        finally
        {
            _semaphore.Release();
        }

        return await FetchTokenAsync(cancellationToken);
    }

    private async Task<string> FetchTokenAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Double-check inside the lock in case another thread already refreshed
            if (IsTokenValid())
            {
                return _cachedToken!;
            }

            _logger.LogInformation("Fetching new Keka access token from {IdentityUrl}", _config.IdentityUrl);

            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["grant_type"]    = _config.GrantType,
                ["scope"]         = _config.Scope
            });

            var requestUri = new Uri(new Uri(_config.IdentityUrl), _config.TokenEndpoint);
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

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiresAt = DateTime.UtcNow
                .AddSeconds(tokenResponse.ExpiresIn)
                .AddSeconds(-_config.TokenExpiryBufferSeconds);

            _logger.LogInformation(
                "Keka access token fetched successfully. Effective expiry at {ExpiresAt}",
                _tokenExpiresAt);

            return _cachedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool IsTokenValid()
        => !string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow < _tokenExpiresAt;

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace oculusit.sync.connectwise.services;

/// <summary>
/// Provides shared HTTP request building and JSON deserialization helpers
/// for all ConnectWise service implementations.
/// </summary>
public abstract class ConnectWiseBaseService
{
    protected readonly HttpClient HttpClient;
    protected readonly ConnectWiseConfiguration Config;

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    protected ConnectWiseBaseService(
        IHttpClientFactory httpClientFactory,
        IOptions<ConnectWiseConfiguration> config)
    {
        HttpClient = httpClientFactory.CreateClient(nameof(ConnectWiseService));
        Config = config.Value;
    }

    // ConnectWise uses Basic Auth: Base64("companyId+publicKey:privateKey")
    private string BuildBasicAuthHeader()
    {
        var credentials = $"{Config.CompanyId}+{Config.PublicKey}:{Config.PrivateKey}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    protected HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method,
            new Uri(new Uri(Config.BaseUrl), Config.ApiVersion + relativeUrl));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", BuildBasicAuthHeader());

        // ClientId header is mandatory for all CW API calls
        request.Headers.Add("clientId", Config.ClientId);
        request.Headers.Add("Accept", "application/json");

        return request;
    }

    protected async Task<List<T>> FetchPagedAsync<T>(
        string relativeUrlBase,
        string fields,
        string orderBy,
        string? conditions,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var all = new List<T>();
        var page = 1;

        while (true)
        {
            var url = $"{relativeUrlBase}?pageSize={pageSize}&page={page}&fields={fields}&orderBy={orderBy}";

            if (!string.IsNullOrEmpty(conditions))
                url += $"&conditions={Uri.EscapeDataString(conditions)}";

            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"ConnectWise API error on {relativeUrlBase} page {page} — {response.StatusCode}: {errorBody}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var pageResults = await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken);

            if (pageResults is null || pageResults.Count == 0)
                break;

            all.AddRange(pageResults);

            if (pageResults.Count < pageSize)
                break;

            page++;
        }

        return all;
    }
}

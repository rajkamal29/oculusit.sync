using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseService> logger) : IConnectWiseService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient(nameof(ConnectWiseService));
    private readonly ConnectWiseConfiguration _config = config.Value;
    private readonly ILogger<ConnectWiseService> _logger = logger;


    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ConnectWise uses Basic Auth: Base64("companyId+publicKey:privateKey")
    private string BuildBasicAuthHeader()
    {
        var credentials = $"{_config.CompanyId}+{_config.PublicKey}:{_config.PrivateKey}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method,
            new Uri(new Uri(_config.BaseUrl), _config.ApiVersion + relativeUrl));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", BuildBasicAuthHeader());

        // ClientId header is mandatory for all CW API calls
        request.Headers.Add("clientId", _config.ClientId);
        request.Headers.Add("Accept", "application/json");

        return request;
    }

    public async Task<IReadOnlyList<ConnectWiseCompany>> GetAllCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        var allCompanies = new List<ConnectWiseCompany>();
        var page = 1;

        _logger.LogInformation("Starting full ConnectWise company fetch with page size {PageSize}", _config.PageSize);

        while (true)
        {
            var relativeUrl = $"/company/companies" +
                              $"?pageSize={_config.PageSize}&page={page}" +
                              $"&fields=id,identifier,name,status,type,addressLine1,addressLine2,city,state,zip,country,phoneNumber,faxNumber,website,invoiceCCEmailAddress,_info" +
                              $"&orderBy=id asc";

            _logger.LogDebug("Fetching ConnectWise companies page {Page}", page);

            using var request = CreateRequest(HttpMethod.Get, relativeUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("ConnectWise API error on page {Page} — {StatusCode}: {Body}",
                    page, response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var pageResults = await JsonSerializer.DeserializeAsync<List<ConnectWiseCompany>>(
                stream, _jsonOptions, cancellationToken);

            if (pageResults is null || pageResults.Count == 0)
                break;

            allCompanies.AddRange(pageResults);

            _logger.LogDebug("Fetched {Count} companies on page {Page}. Total so far: {Total}",
                pageResults.Count, page, allCompanies.Count);

            // If the page returned fewer records than the page size, we've reached the last page
            if (pageResults.Count < _config.PageSize)
                break;

            page++;
        }

        _logger.LogInformation("Completed ConnectWise company fetch. Total companies loaded: {Total}", allCompanies.Count);

        return allCompanies;
    }

    public async Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesSinceAsync(
        DateTime since, CancellationToken cancellationToken = default)
    {
        var allCompanies = new List<ConnectWiseCompany>();
        var page = 1;
        var condition = $"lastUpdated >= '{since:yyyy-MM-ddTHH:mm:ssZ}'";

        _logger.LogInformation("Starting incremental ConnectWise company fetch since {Since}", since);

        while (true)
        {
            var relativeUrl = $"/company/companies" +
                              $"?pageSize={_config.PageSize}&page={page}" +
                              $"&conditions={Uri.EscapeDataString(condition)}" +
                              $"&fields=id,identifier,name,status,type,addressLine1,addressLine2,city,state,zip,country,phoneNumber,faxNumber,website,invoiceCCEmailAddress,_info" +
                              $"&orderBy=lastUpdated asc";

            _logger.LogDebug("Fetching incremental ConnectWise companies page {Page}", page);

            using var request = CreateRequest(HttpMethod.Get, relativeUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("ConnectWise API error on incremental page {Page} — {StatusCode}: {Body}",
                    page, response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var pageResults = await JsonSerializer.DeserializeAsync<List<ConnectWiseCompany>>(
                stream, _jsonOptions, cancellationToken);

            if (pageResults is null || pageResults.Count == 0)
                break;

            allCompanies.AddRange(pageResults);

            _logger.LogDebug("Fetched {Count} companies on incremental page {Page}. Total so far: {Total}",
                pageResults.Count, page, allCompanies.Count);

            if (pageResults.Count < _config.PageSize)
                break;

            page++;
        }

        _logger.LogInformation("Completed incremental ConnectWise company fetch. Total companies loaded: {Total}", allCompanies.Count);

        return allCompanies;
    }

    public async Task<IReadOnlyList<ConnectWiseCompany>> GetCompaniesByIdsAsync(
        IReadOnlyList<int> companyIds, CancellationToken cancellationToken = default)
    {
        if (companyIds.Count == 0)
        {
            _logger.LogInformation("No company IDs provided for retrieval.");
            return [];
        }

        var allCompanies = new List<ConnectWiseCompany>();
        var page = 1;
        var idsCondition = string.Join(" OR ", companyIds.Select(id => $"id = {id}"));
        var condition = $"({idsCondition})";

        _logger.LogInformation("Starting ConnectWise company fetch for {Count} specific company IDs", companyIds.Count);

        while (true)
        {
            var relativeUrl = $"/company/companies" +
                              $"?pageSize={_config.PageSize}&page={page}" +
                              $"&conditions={Uri.EscapeDataString(condition)}" +
                              $"&fields=id,identifier,name,status,type,addressLine1,addressLine2,city,state,zip,country,phoneNumber,faxNumber,website,invoiceCCEmailAddress,_info" +
                              $"&orderBy=id asc";

            _logger.LogDebug("Fetching ConnectWise companies by IDs page {Page}", page);

            using var request = CreateRequest(HttpMethod.Get, relativeUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("ConnectWise API error on company IDs page {Page} — {StatusCode}: {Body}",
                    page, response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var pageResults = await JsonSerializer.DeserializeAsync<List<ConnectWiseCompany>>(
                stream, _jsonOptions, cancellationToken);

            if (pageResults is null || pageResults.Count == 0)
                break;

            allCompanies.AddRange(pageResults);

            _logger.LogDebug("Fetched {Count} companies on IDs page {Page}. Total so far: {Total}",
                pageResults.Count, page, allCompanies.Count);

            if (pageResults.Count < _config.PageSize)
                break;

            page++;
        }

        _logger.LogInformation("Completed ConnectWise company fetch by IDs. Total companies loaded: {Total}", allCompanies.Count);

        return allCompanies;
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseTimeEntryService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseTimeEntryService> logger)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseTimeEntryService
{
    private const string Fields =
        "id,member,company,project,chargeToType,chargeToId,timeStart,timeEnd,hoursActual,billableOption,notes,_info";

    public async Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        logger.LogInformation(
            "Fetching ConnectWise time entries for UTC day {Date} ({StartUtc:o} to {EndUtc:o}).",
            date, startUtc, endUtc);

        var condition =
            $"timeStart >= [{startUtc:yyyy-MM-ddTHH:mm:ssZ}] AND timeStart <= [{endUtc:yyyy-MM-ddTHH:mm:ssZ}]";

        var results = await FetchPagedAsync<ConnectWiseTimeEntry>(
            relativeUrlBase: "/time/entries",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        await EnrichMemberEmailsAsync(results, cancellationToken);

        logger.LogInformation(
            "Fetched {Count} ConnectWise time entries for UTC day {Date}.",
            results.Count, date);

        return results;
    }

    private async Task EnrichMemberEmailsAsync(
        IReadOnlyList<ConnectWiseTimeEntry> entries,
        CancellationToken cancellationToken)
    {
        var memberIds = entries
            .Select(e => e.Member?.Id)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (memberIds.Count == 0)
            return;

        logger.LogInformation("Enriching member emails for {Count} distinct ConnectWise members.", memberIds.Count);

        var emailByMemberId = new Dictionary<int, string>();

        foreach (var memberId in memberIds)
        {
            try
            {
                using var request = CreateRequest(
                    HttpMethod.Get,
                    $"/system/members/{memberId}?fields=id,identifier,officeEmail,defaultEmail");

                using var response = await HttpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogWarning(
                        "Failed to fetch ConnectWise member {MemberId} email. Status={StatusCode}, Body={Body}",
                        memberId, response.StatusCode, errorBody);
                    continue;
                }

                var member = await response.Content.ReadFromJsonAsync<ConnectWiseMemberEmailResponse>(JsonOptions, cancellationToken);

                var email = member?.OfficeEmail ?? member?.DefaultEmail;
                if (!string.IsNullOrWhiteSpace(email))
                    emailByMemberId[memberId] = email;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enrich email for ConnectWise member {MemberId}.", memberId);
            }
        }

        foreach (var entry in entries)
        {
            if (entry.Member is null)
                continue;

            if (emailByMemberId.TryGetValue(entry.Member.Id, out var email))
                entry.MemberEmail = email;
        }

        logger.LogInformation("Member email enrichment complete. Resolved emails for {Count} members.", emailByMemberId.Count);
    }

    private sealed class ConnectWiseMemberEmailResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("identifier")]
        public string Identifier { get; init; } = string.Empty;

        [JsonPropertyName("officeEmail")]
        public string? OfficeEmail { get; init; }

        [JsonPropertyName("defaultEmail")]
        public string? DefaultEmail { get; init; }
    }

}

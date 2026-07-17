using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseTimeEntryService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseTimeEntryService> logger)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseTimeEntryService
{
    private const string Fields =
        "id,member,company,project,workRole,workType,chargeToType,chargeToId," +
        "timeStart,timeEnd,hoursActual,hoursBilled,hoursDeduct,agreement,ticket,phase," +
        "billableOption,taxable,invoiceId,enteredBy,enteredDate," +
        "notes,internalNotes,emailResourceFlag,emailContactFlag,emailCcFlag," +
        "hourlyRate,mobileGuid,_info,status";

    public async Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForDayAsync(
        DateOnly date,
        IReadOnlyList<int>? memberIds = null,
        CancellationToken cancellationToken = default)
    {
        var (startUtc, endUtc) = GetWeekBoundsUtc(date);

        logger.LogInformation(
            "Fetching ConnectWise time entries for UTC week containing {Date} ({StartUtc:o} to {EndUtc:o}) with member filter count {MemberCount}.",
            date, startUtc, endUtc, memberIds?.Count ?? 0);

        var condition =
            $"timeStart >= [{startUtc:yyyy-MM-ddTHH:mm:ssZ}] AND timeStart <= [{endUtc:yyyy-MM-ddTHH:mm:ssZ}]";

        condition = AppendMemberFilterCondition(condition, memberIds);

        var results = await FetchPagedAsync<ConnectWiseTimeEntry>(
            relativeUrlBase: "/time/entries",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Fetched {Count} ConnectWise time entries for UTC week containing {Date}.",
            results.Count, date);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForCompanyAndDayAsync(
        int companyId,
        DateOnly date,
        IReadOnlyList<int>? memberIds = null,
        CancellationToken cancellationToken = default)
    {
        var (startUtc, endUtc) = GetWeekBoundsUtc(date);

        logger.LogInformation(
            "Fetching ConnectWise time entries for company {CompanyId} and UTC week containing {Date} ({StartUtc:o} to {EndUtc:o}) with member filter count {MemberCount}.",
            companyId, date, startUtc, endUtc, memberIds?.Count ?? 0);

        var condition =
            $"company/id = {companyId} AND timeStart >= [{startUtc:yyyy-MM-ddTHH:mm:ssZ}] AND timeStart <= [{endUtc:yyyy-MM-ddTHH:mm:ssZ}]";

        condition = AppendMemberFilterCondition(condition, memberIds);

        var results = await FetchPagedAsync<ConnectWiseTimeEntry>(
            relativeUrlBase: "/time/entries",
            fields: Fields,
            orderBy: "lastUpdated asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Fetched {Count} ConnectWise time entries for company {CompanyId} and UTC week containing {Date}.",
            results.Count, companyId, date);

        return results;
    }

    public async Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesByTimesheetIdAsync(
        int timesheetId,
        string timeOffWorkType,
        CancellationToken cancellationToken = default)
    {
        if (timesheetId <= 0)
        {
            logger.LogWarning("Invalid timesheet ID: {TimesheetId}", timesheetId);
            return [];
        }

        logger.LogInformation(
            "Fetching ConnectWise time entries for timesheet ID {TimesheetId}.",
            timesheetId);

        var condition = $"timesheet/id = {timesheetId} AND workType/name NOT IN ({timeOffWorkType})";

        var results = await FetchPagedAsync<ConnectWiseTimeEntry>(
            relativeUrlBase: "/time/entries",
            fields: Fields,
            orderBy: "timeStart asc",
            conditions: condition,
            pageSize: Config.PageSize,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Fetched {Count} time entries for timesheet ID {TimesheetId}.",
            results.Count, timesheetId);

        return results;
    }

    private static string AppendMemberFilterCondition(string baseCondition, IReadOnlyList<int>? memberIds)
    {
        if (memberIds is null || memberIds.Count == 0)
            return baseCondition;

        var filteredIds = memberIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (filteredIds.Count == 0)
            return baseCondition;

        var memberCondition = string.Join(" OR ", filteredIds.Select(id => $"member/id = {id}"));
        return $"{baseCondition} AND ({memberCondition})";
    }

    private static (DateTime StartUtc, DateTime EndUtc) GetWeekBoundsUtc(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = date.AddDays(-daysSinceMonday);
        var sunday = monday.AddDays(6);

        var startUtc = monday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = sunday.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        return (startUtc, endUtc);
    }

}

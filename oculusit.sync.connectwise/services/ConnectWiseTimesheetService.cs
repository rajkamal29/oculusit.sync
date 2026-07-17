using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.modules;
using System.Globalization;

namespace oculusit.sync.connectwise.services;

public sealed class ConnectWiseTimesheetService(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectWiseConfiguration> config,
    ILogger<ConnectWiseTimesheetService> logger)
    : ConnectWiseBaseService(httpClientFactory, config), IConnectWiseTimesheetService
{
    private const string TimesheetFields =
        "id,member,year,period,dateStart,dateEnd,status,hours,deadline,_info";

    private const int TimesheetPageSize = 200;

    private static readonly string[] BillableStatuses =
    [
        "PendingApproval",
        "ErrorsCorrected",
        "PendingProjectApproval",
        "ApprovedByTierOne",
        "ApprovedByTierTwo",
        "ReadyToBill",
        "Billed",
        "BilledAgreement"
    ];

    private const string AuditTrailFields =
        "id,member,source,type,message,oldValue,newValue,value,_info";

    public async Task<ConnectWiseTimesheet?> GetTimesheetByIdAsync(
        int timesheetId,
        CancellationToken cancellationToken = default)
    {
        if (timesheetId <= 0)
        {
            logger.LogWarning("Invalid timesheet ID: {TimesheetId}", timesheetId);
            return null;
        }

        logger.LogDebug("Fetching ConnectWise timesheet with ID {TimesheetId}.", timesheetId);

        try
        {
            using var request = CreateRequest(HttpMethod.Get,
                $"/time/sheets/{timesheetId}?fields={TimesheetFields}");
            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Failed to fetch ConnectWise timesheet {TimesheetId}. StatusCode: {StatusCode}, Body: {Body}",
                    timesheetId, response.StatusCode, errorBody);
                return null;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var timesheet = await System.Text.Json.JsonSerializer.DeserializeAsync<ConnectWiseTimesheet>(
                stream, JsonOptions, cancellationToken);

            if (timesheet is null)
            {
                logger.LogWarning("Timesheet {TimesheetId} deserialization returned null.", timesheetId);
                return null;
            }

            logger.LogInformation(
                "Successfully fetched ConnectWise timesheet {TimesheetId} for employee {EmployeeId}, week {Week}/{Year}.",
                timesheet.Id, timesheet.Member?.Id, timesheet.Period, timesheet.Year);

            return timesheet;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception occurred while fetching ConnectWise timesheet {TimesheetId}.",
                timesheetId);
            return null;
        }
    }

    public async Task<IReadOnlyList<ConnectWiseTimesheet>> GetTimesheetsByEmployeeAndDateRangeAsync(
        int employeeId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0)
        {
            logger.LogWarning("Invalid employee ID: {EmployeeId}", employeeId);
            return [];
        }

        var normalizedStart = startDate.Kind == DateTimeKind.Utc
            ? startDate
            : startDate.ToUniversalTime();

        var normalizedEnd = endDate.Kind == DateTimeKind.Utc
            ? endDate
            : endDate.ToUniversalTime();

        if (normalizedStart > normalizedEnd)
        {
            logger.LogWarning(
                "Invalid date range for employee {EmployeeId}: start={StartDate:o} > end={EndDate:o}",
                employeeId, normalizedStart, normalizedEnd);
            return [];
        }

        logger.LogInformation(
            "Fetching ConnectWise timesheets for employee {EmployeeId} from {StartDate:o} to {EndDate:o}.",
            employeeId, normalizedStart, normalizedEnd);

        var condition =
            $"member/id = {employeeId} AND " +
            $"dateSubmitted >= [{normalizedStart:yyyy-MM-ddTHH:mm:ssZ}] AND " +
            $"dateSubmitted <= [{normalizedEnd:yyyy-MM-ddTHH:mm:ssZ}]";

        try
        {
            var results = await FetchPagedAsync<ConnectWiseTimesheet>(
                relativeUrlBase: "/timesheets",
                fields: TimesheetFields,
                orderBy: "dateStart desc",
                conditions: condition,
                pageSize: Config.PageSize,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Fetched {Count} ConnectWise timesheets for employee {EmployeeId} in date range.",
                results.Count, employeeId);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception occurred while fetching timesheets for employee {EmployeeId}.",
                employeeId);
            return [];
        }
    }

    public async Task<IReadOnlyList<ConnectWiseTimesheet>> GetTimesheetsByWeekAsync(
        int week,
        int year,
        CancellationToken cancellationToken = default)
    {
        if (week < 1 || week > 53)
        {
            logger.LogWarning("Invalid week number: {Week} (must be 1-53)", week);
            return [];
        }

        if (year < 1900 || year > 2100)
        {
            logger.LogWarning("Invalid year: {Year}", year);
            return [];
        }

        logger.LogInformation(
            "Fetching ConnectWise timesheets for week {Week} of year {Year}.",
            week, year);

        var condition = $"period = {week} AND year = {year}";

        try
        {
            var results = await FetchPagedAsync<ConnectWiseTimesheet>(
                relativeUrlBase: "/timesheets",
                fields: TimesheetFields,
                orderBy: "dateStart desc",
                conditions: condition,
                pageSize: Config.PageSize,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Fetched {Count} ConnectWise timesheets for week {Week}/{Year}.",
                results.Count, week, year);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception occurred while fetching timesheets for week {Week}/{Year}.",
                week, year);
            return [];
        }
    }

    public async Task<IReadOnlyList<ConnectWiseTimesheet>> GetTimesheetsSinceAsync(
        DateTime lastUpdatedSince,
        CancellationToken cancellationToken = default)
    {
        var normalizedSince = lastUpdatedSince.Kind == DateTimeKind.Utc
            ? lastUpdatedSince
            : lastUpdatedSince.ToUniversalTime();

        var statusList = string.Join(",", BillableStatuses.Select(s => $"\"{s}\""));
        var condition =
            $"status in ({statusList}) AND " +
            $"lastUpdated>[{normalizedSince:yyyy-MM-ddTHH:mm:ssZ}]";

        logger.LogInformation(
            "Fetching ConnectWise timesheets with billable statuses updated after {LastUpdatedSince:o}.",
            normalizedSince);

        try
        {
            var results = await FetchPagedAsync<ConnectWiseTimesheet>(
                relativeUrlBase: "/time/sheets",
                fields: TimesheetFields,
                orderBy: "lastUpdated asc",
                conditions: condition,
                pageSize: TimesheetPageSize,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Fetched {Count} ConnectWise timesheets updated after {LastUpdatedSince:o}.",
                results.Count, normalizedSince);

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception occurred while fetching timesheets updated after {LastUpdatedSince:o}.",
                normalizedSince);
            return [];
        }
    }

    public async Task<IReadOnlyList<ConnectWiseTimesheetAuditTrail>> GetTimesheetAuditTrailAsync(
        int timesheetId,
        CancellationToken cancellationToken = default)
    {
        if (timesheetId <= 0)
        {
            logger.LogWarning("Invalid timesheet ID for audit trail: {TimesheetId}", timesheetId);
            return [];
        }

        logger.LogInformation(
            "Fetching ConnectWise timesheet audit trail for timesheet ID {TimesheetId}.",
            timesheetId);

        try
        {
            using var request = CreateRequest(HttpMethod.Get,
                $"/time/sheets/{timesheetId}/audits?pageSize={Config.PageSize}&fields={AuditTrailFields}");
            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "Failed to fetch audit trail for timesheet {TimesheetId}. StatusCode: {StatusCode}, Body: {Body}",
                    timesheetId, response.StatusCode, errorBody);
                return [];
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var auditTrails = await System.Text.Json.JsonSerializer.DeserializeAsync<List<ConnectWiseTimesheetAuditTrail>>(
                stream, JsonOptions, cancellationToken);

            var result = (IReadOnlyList<ConnectWiseTimesheetAuditTrail>?)auditTrails ?? [];
            logger.LogInformation(
                "Fetched {Count} audit trail entries for timesheet {TimesheetId}.",
                result.Count, timesheetId);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Exception occurred while fetching audit trail for timesheet {TimesheetId}.",
                timesheetId);
            return [];
        }
    }
}

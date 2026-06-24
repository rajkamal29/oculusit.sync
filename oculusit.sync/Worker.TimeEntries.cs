using oculusit.sync.connectwise.modules;
using oculusit.sync.core.models;
using Polly.Timeout;
using System.Globalization;

namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncTimeSheetAsync(IReadOnlyList<string> retryTimeSheetIds, CancellationToken stoppingToken)
    {
        var timeSheetState = await syncStateService.GetAsync(SyncTypes.TimeSheets, stoppingToken);

        DateTime lastUpdatedSince;

        if (timeSheetState is not null)
        {
            lastUpdatedSince = timeSheetState.LastUpdatedAt ?? GetWeekBoundsUtc(DateTime.UtcNow).WeekStartUtc;

            logger.LogInformation(
                "TimeSheets sync state found. LastUpdatedAt={LastUpdatedAt:o}.",
                lastUpdatedSince);
        }
        else
        {
            lastUpdatedSince = GetWeekBoundsUtc(DateTime.UtcNow).WeekStartUtc;
            var newState = new SyncState
            {
                SyncType = SyncTypes.TimeSheets,
                LastUpdatedAt = lastUpdatedSince
            };
            await syncStateService.SaveAsync(newState, stoppingToken);

            logger.LogInformation(
                "TimeSheets sync state not found. Created new record with LastUpdatedAt={LastUpdatedAt:o}.",
                lastUpdatedSince);
        }

        var timesheets = await connectWiseTimesheetService.GetTimesheetsSinceAsync(lastUpdatedSince, stoppingToken);
        var retryTimesheets = new List<ConnectWiseTimesheet>();

        foreach (var retryTimeSheetId in retryTimeSheetIds)
        {
            if (!int.TryParse(retryTimeSheetId, out var retryTimesheetId) || retryTimesheetId <= 0)
                continue;

            var retryTimesheet = await connectWiseTimesheetService.GetTimesheetByIdAsync(retryTimesheetId, stoppingToken);
            if (retryTimesheet is not null)
                retryTimesheets.Add(retryTimesheet);
        }

        var allTimesheets = timesheets
            .Concat(retryTimesheets)
            .GroupBy(t => t.Id)
            .Select(g => g.Last())
            .OrderBy(t => t.LastUpdated ?? DateTime.MinValue)
            .Distinct()
            .ToList();

        logger.LogInformation(
            "TimeSheet sync fetched {Count} timesheet(s) updated after {LastUpdatedSince:o} and loaded {RetryCount} retry timesheet(s).",
            timesheets.Count,
            lastUpdatedSince,
            retryTimesheets.Count);

        if (allTimesheets.Count == 0)
            return;

        var succeededTimeSheetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Group timesheets by member — one DB lookup per member, not per timesheet
        var timesheetsByMember = allTimesheets
            .Where(t => t.Member is not null && t.Member.Id > 0)
            .GroupBy(t => t.Member!.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        logger.LogInformation(
            "Processing {TimesheetCount} timesheet(s) across {MemberCount} unique member(s).",
            allTimesheets.Count, timesheetsByMember.Count);

        var totalPosted  = 0;
        var totalSkipped = 0;
        var missingCount = 0;
        var retryTimeSheetEntries = new List<RetryTimeSheetEntry>();

        foreach (var memberGroup in timesheetsByMember)
        {
            var memberId      = memberGroup.Key;
            var employeeState = await syncStateService.GetTimeEntryEmployeeDedupeStateAsync(memberId, stoppingToken);

            if (employeeState is null)
            {
                missingCount++;
                logger.LogWarning(
                    "TimeEntries#{MemberId} not found in DB — skipping {TimesheetCount} timesheet(s). " +
                    "Member exists in ConnectWise but has no employee checkpoint record.",
                    memberId, memberGroup.Count());

                foreach (var timesheet in memberGroup)
                {
                    retryTimeSheetEntries.Add(new RetryTimeSheetEntry
                    {
                        Id = timesheet.Id.ToString(CultureInfo.InvariantCulture),
                        MemberId = memberId,
                        Email = string.Empty,
                        Year = timesheet.Year,
                        Period = timesheet.Period,
                        ErrorMessage = $"TimeEntries#{memberId} not found in DB. Member exists in ConnectWise but has no employee checkpoint record."
                    });
                }

                continue;
            }

            // Build updated SyncedPeriods starting from what's already in DB
            var updatedSyncedPeriods = employeeState.SyncedPeriods
                .ToDictionary(kvp => kvp.Key, kvp => new HashSet<int>(kvp.Value));

            var memberNewPeriods = 0;

            foreach (var timesheet in memberGroup.OrderBy(t => t.Year).ThenBy(t => t.Period))
            {
                try
                {
                    var year   = timesheet.Year;
                    var period = timesheet.Period;

                    // Check if this year/period is already synced for this employee
                    if (employeeState.SyncedPeriods.TryGetValue(year, out var syncedPeriods)
                        && syncedPeriods.Contains(period))
                    {
                        // Already synced — check audit trail for any rejections
                        var auditTrail = await connectWiseTimesheetService.GetTimesheetAuditTrailAsync(
                            timesheet.Id, stoppingToken);

                        var hasRejection = auditTrail.Any(a =>
                            IsResubmissionStatus(a.StatusAfter)  ||
                            IsResubmissionStatus(a.StatusBefore) ||
                            IsResubmissionStatus(a.TransactionType));

                        if (!hasRejection)
                        {
                            totalSkipped++;
                            logger.LogDebug(
                                "Timesheet {TimesheetId} (member={MemberId}, {Year}/{Period}) already synced with no rejections — skipping.",
                                timesheet.Id, memberId, year, period);
                            continue;
                        }

                        // Rejection found — update logic not yet implemented
                        logger.LogWarning(
                            "Timesheet {TimesheetId} (member={MemberId}, year={Year}, period={Period}) has rejection history " +
                            "but re-sync to Keka is not yet supported. Please update this timesheet in Keka manually.",
                            timesheet.Id, memberId, year, period);

                        retryTimeSheetEntries.Add(new RetryTimeSheetEntry
                        {
                            Id = timesheet.Id.ToString(CultureInfo.InvariantCulture),
                            MemberId = memberId,
                            Email = employeeState.Email,
                            Year = year,
                            Period = period,
                            ErrorMessage = "Timesheet has rejection history and cannot be automatically re-synced. Please update this timesheet in Keka manually."
                        });

                        totalSkipped++;
                        continue;
                    }

                    // Period not yet synced — fetch time entries for this timesheet and log to Keka
                    logger.LogInformation(
                        "Timesheet {TimesheetId} (member={MemberId}, {Year}/{Period}) not synced yet — fetching time entries.",
                        timesheet.Id, memberId, year, period);

                    var timeEntries = await connectWiseTimeEntryService.GetTimeEntriesByTimesheetIdAsync(
                        timesheet.Id, stoppingToken);

                    var postedCount = await timeEntryOrchestrationService.LogTimeEntriesBatchAsync(
                        timeEntries,
                        employeeState.Email,
                        stoppingToken);

                    totalPosted += postedCount;
                    succeededTimeSheetIds.Add(timesheet.Id.ToString(CultureInfo.InvariantCulture));
                    memberNewPeriods++;

                    // Mark this year/period as synced in the local map
                    if (!updatedSyncedPeriods.ContainsKey(year))
                        updatedSyncedPeriods[year] = [];
                    updatedSyncedPeriods[year].Add(period);

                    logger.LogInformation(
                        "Timesheet {TimesheetId} (member={MemberId}, {Year}/{Period}): fetched {EntryCount} entries, posted {PostedCount} to Keka in a single batch.",
                        timesheet.Id, memberId, year, period, timeEntries.Count, timeEntries.Count);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        "Error processing timesheet {TimesheetId} (member={MemberId}, {Year}/{Period}). Exception: {ExceptionMessage}",
                        timesheet.Id, memberId, timesheet.Year, timesheet.Period, ex.Message);

                    retryTimeSheetEntries.Add(new RetryTimeSheetEntry
                    {
                        Id = timesheet.Id.ToString(),
                        MemberId = memberId,
                        Email = employeeState.Email,
                        Year = timesheet.Year,
                        Period = timesheet.Period,
                        ErrorMessage = ex.Message
                    });
                }
            }

            // Persist updated SyncedPeriods for this member if any new periods were processed
            if (memberNewPeriods > 0)
            {
                await syncStateService.UpsertTimeEntryEmployeeDedupeStateAsync(new TimeEntryEmployeeDedupeState
                {
                    EmployeeId    = employeeState.EmployeeId,
                    Email         = employeeState.Email,
                    SyncedPeriods = updatedSyncedPeriods
                }, stoppingToken);

                logger.LogInformation(
                    "DB updated for member {MemberId}: {NewPeriods} new period(s) added. Total synced periods across all years: {TotalPeriods}.",
                    memberId, memberNewPeriods,
                    updatedSyncedPeriods.Values.Sum(s => s.Count));
            }
        }

        await syncStateService.SaveRetryTimeSheetsAsync(retryTimeSheetEntries, lastUpdatedSince, stoppingToken);

        // Update the TimeSheets checkpoint with the LastUpdated of the final (most recent) timesheet
        // so the next run only fetches timesheets updated after this point
        var lastTimesheetUpdatedAt = timesheets.Last().LastUpdated;
        if (lastTimesheetUpdatedAt.HasValue)
        {
            await syncStateService.SaveAsync(new SyncState
            {
                SyncType      = SyncTypes.TimeSheets,
                LastUpdatedAt = lastTimesheetUpdatedAt
            }, stoppingToken);

            logger.LogInformation(
                "TimeSheets checkpoint updated. LastUpdatedAt={LastUpdatedAt:o}.",
                lastTimesheetUpdatedAt);
        }
        else
        {
            logger.LogWarning(
                "Last timesheet record has no LastUpdated value — TimeSheets checkpoint was not updated.");
        }

        logger.LogInformation(
            "TimeSheet sync complete. Members={MemberCount}, MissingInDb={MissingCount}, Skipped={SkippedCount}, PostedToKeka={PostedCount}.",
            timesheetsByMember.Count, missingCount, totalSkipped, totalPosted);
    }

    private async Task SyncTimeEntryEmployeesAsync(CancellationToken stoppingToken)
    {
        var connectWiseMembers = await connectWiseMemberService.GetAllMembersAsync(stoppingToken);
        var existingEmployeeStates = await syncStateService.GetTimeEntryEmployeeDedupeStatesAsync(stoppingToken);
        var existingEmployeeIds = existingEmployeeStates
            .Where(state => !string.IsNullOrWhiteSpace(state.EmployeeId))
            .Select(state => state.EmployeeId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var membersToInsert = connectWiseMembers
            .Where(member => member.Id > 0)
            .Where(member => !existingEmployeeIds.Contains(member.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        foreach (var member in membersToInsert)
        {
            await syncStateService.UpsertTimeEntryEmployeeDedupeStateAsync(new TimeEntryEmployeeDedupeState
            {
                EmployeeId    = member.Id.ToString(CultureInfo.InvariantCulture),
                Email         = member.Email,
                SyncedPeriods = []
            }, stoppingToken);
        }

        logger.LogInformation(
            "Time-entry employee sync complete. ConnectWiseMembers={ConnectWiseMemberCount}, ExistingDbEmployees={ExistingDbEmployeeCount}, InsertedEmployees={InsertedEmployeeCount}, SkippedExistingEmployees={SkippedExistingEmployeeCount}.",
            connectWiseMembers.Count,
            existingEmployeeStates.Count,
            membersToInsert.Count,
            connectWiseMembers.Count - membersToInsert.Count);
    }

    /// <summary>
    /// Returns true if the given status value indicates a rejection or resubmission event
    /// that may require the timesheet to be re-synced to Keka.
    /// </summary>
    private static bool IsResubmissionStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return status.Contains("Reject",          StringComparison.OrdinalIgnoreCase) ||
               status.Contains("ErrorsCorrected", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("Written Off",     StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime ResolveStartWeekUtc(Dictionary<int, HashSet<int>> syncedPeriods, DateTime previousWeekStartUtc)
    {
        if (syncedPeriods.Count == 0)
            return previousWeekStartUtc;

        var maxYear   = syncedPeriods.Keys.Max();
        var maxPeriod = syncedPeriods[maxYear].Max();

        var lastSyncedWeekStart = DateTime.SpecifyKind(
            ISOWeek.ToDateTime(maxYear, maxPeriod, DayOfWeek.Monday),
            DateTimeKind.Utc);

        var nextWeekToSyncUtc = lastSyncedWeekStart.AddDays(7);
        return nextWeekToSyncUtc <= previousWeekStartUtc ? nextWeekToSyncUtc : previousWeekStartUtc.AddDays(7);
    }

    private static IReadOnlyList<(DateTime WeekStartUtc, DateTime WeekEndUtc)> GetWeeksInRange(DateTime startWeekUtc, DateTime endWeekUtc)
    {
        var weeks = new List<(DateTime WeekStartUtc, DateTime WeekEndUtc)>();
        var cursor = DateTime.SpecifyKind(startWeekUtc.Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(endWeekUtc.Date, DateTimeKind.Utc);

        while (cursor <= end)
        {
            weeks.Add((cursor, cursor.AddDays(7).AddTicks(-1)));
            cursor = cursor.AddDays(7);
        }

        return weeks;
    }

    private static (DateTime WeekStartUtc, DateTime WeekEndUtc) GetPreviousWeekBoundsUtc(DateTime utcNow)
    {
        var normalizedUtc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        var currentWeekStart = normalizedUtc.Date.AddDays(-((7 + (int)normalizedUtc.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var previousWeekStart = currentWeekStart.AddDays(-7);
        var previousWeekEnd = previousWeekStart.AddDays(7).AddTicks(-1);

        return (DateTime.SpecifyKind(previousWeekStart, DateTimeKind.Utc), DateTime.SpecifyKind(previousWeekEnd, DateTimeKind.Utc));
    }

    private static (DateTime WeekStartUtc, DateTime WeekEndUtc) GetWeekBoundsUtc(DateTime utcDateTime)
    {
        var normalizedUtc = utcDateTime.Kind == DateTimeKind.Utc ? utcDateTime : utcDateTime.ToUniversalTime();
        var weekStart = normalizedUtc.Date.AddDays(-((7 + (int)normalizedUtc.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var weekEnd = weekStart.AddDays(7).AddTicks(-1);

        return (DateTime.SpecifyKind(weekStart, DateTimeKind.Utc), DateTime.SpecifyKind(weekEnd, DateTimeKind.Utc));
    }

    private async Task<IReadOnlyList<string>> GetRetryTimeSheetIdsFromSyncStateAsync(CancellationToken stoppingToken)
    {
        var retryTimeSheets = await syncStateService.GetRetryTimeSheetsAsync(stoppingToken);

        var candidateTimeSheetIds = retryTimeSheets
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.ErrorMessage))
            .Select(e => e.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Found {Count} retry timesheets in RetryTimeSheets SyncState.", candidateTimeSheetIds.Count);
        return candidateTimeSheetIds;
    }
}

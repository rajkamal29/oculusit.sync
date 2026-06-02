using oculusit.sync.connectwise.modules;
using oculusit.sync.core.models;
using System.Globalization;

namespace oculusit.sync;

public sealed partial class Worker
{
    private static readonly IReadOnlySet<string> ExcludedTimeEntryStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Open",
        "Rejected",
        "Rejected - 2nd Tier",
        "Written Off"
    };

    private async Task SyncTimeEntriesSmokeAsync(CancellationToken stoppingToken)
    {
        var runStartedAtUtc = DateTime.UtcNow;
        var previousWeek = GetPreviousWeekBoundsUtc(runStartedAtUtc);
        var previousWeekDedupeKey = previousWeek.WeekStartUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var employeesToSync = (await syncStateService.GetTimeEntryEmployeeDedupeStatesToSyncAsync(previousWeekDedupeKey, stoppingToken))
            .Where(state => !string.IsNullOrWhiteSpace(state.EmployeeId))
            .OrderBy(state => state.EmployeeId, StringComparer.Ordinal)
            .ToList();

        if (employeesToSync.Count == 0)
        {
            logger.LogInformation(
                "All employees are already synced for previous week {PreviousWeekDedupeKey}. Skipping time-entry pull.",
                previousWeekDedupeKey);
            return;
        }

        var totalWeeksPulled = 0;
        var totalEntriesPulled = 0;
        var totalEntriesLoggedToKeka = 0;

        foreach (var employee in employeesToSync)
        {
            if (!int.TryParse(employee.EmployeeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var employeeId))
            {
                logger.LogWarning("Skipping DB employee {EmployeeId} because it is not a valid numeric ConnectWise member id.", employee.EmployeeId);
                continue;
            }

            var startWeekUtc = ResolveStartWeekUtc(employee.DedupeKey, previousWeek.WeekStartUtc);
            var weeksToPull = GetWeeksInRange(startWeekUtc, previousWeek.WeekStartUtc);
            if (weeksToPull.Count == 0)
            {
                logger.LogInformation(
                    "No weeks to pull for employee {EmployeeId}. DedupeKey={DedupeKey}, PreviousWeekDedupeKey={PreviousWeekDedupeKey}.",
                    employee.EmployeeId,
                    employee.DedupeKey,
                    previousWeekDedupeKey);
                continue;
            }

            DateTime? latestProcessedWeekStartUtc = null;

            foreach (var week in weeksToPull)
            {
                var weekEntries = await connectWiseTimeEntryService.GetTimeEntriesForDayAsync(
                    DateOnly.FromDateTime(week.WeekStartUtc),
                    memberIds: [employeeId],
                    cancellationToken: stoppingToken);

                var employeeEntries = weekEntries
                    .Where(entry => entry.Member?.Id == employeeId)
                    .Where(entry => !ExcludedTimeEntryStatuses.Contains(entry.Status))
                    .Where(entry => entry.TimeStart.HasValue)
                    .Where(entry =>
                    {
                        var timeStartUtc = entry.TimeStart!.Value.Kind == DateTimeKind.Utc
                            ? entry.TimeStart.Value
                            : entry.TimeStart.Value.ToUniversalTime();
                        return timeStartUtc >= week.WeekStartUtc && timeStartUtc <= week.WeekEndUtc;
                    })
                    .ToList();

                var postedCount = 0;
                foreach (var employeeEntry in employeeEntries)
                {
                    var posted = await timeEntryOrchestrationService.LogTimeEntryAsync(
                        employeeEntry,
                        employee.Email,
                        stoppingToken);

                    if (posted)
                        postedCount++;
                }

                totalWeeksPulled++;
                totalEntriesPulled += employeeEntries.Count;
                totalEntriesLoggedToKeka += postedCount;
                latestProcessedWeekStartUtc = week.WeekStartUtc;

                logger.LogInformation(
                    "Pulled {EntryCount} and posted {PostedCount} time entries for employee {EmployeeId} in week {WeekStart:yyyy-MM-dd} to {WeekEnd:yyyy-MM-dd} (LastUpdatedAt={LastUpdatedAt:o}).",
                    employeeEntries.Count,
                    postedCount,
                    employee.EmployeeId,
                    week.WeekStartUtc,
                    week.WeekEndUtc,
                    employee.LastUpdatedAt);
            }

            if (latestProcessedWeekStartUtc.HasValue)
            {
                var latestDedupeKey = latestProcessedWeekStartUtc.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                await syncStateService.UpsertTimeEntryEmployeeDedupeStateAsync(new TimeEntryEmployeeDedupeState
                {
                    EmployeeId = employee.EmployeeId,
                    Email = employee.Email,
                    DedupeKey = latestDedupeKey,
                    LastUpdatedAt = employee.LastUpdatedAt
                }, stoppingToken);

                logger.LogInformation(
                    "Processed employee {EmployeeId} through week {DedupeKey}. Employee checkpoint updated.",
                    employee.EmployeeId,
                    latestDedupeKey);
            }
        }

        logger.LogInformation(
            "Time-entry pull complete. EmployeesToSync={EmployeesToSync}, TotalWeeksPulled={TotalWeeksPulled}, TotalEntriesPulled={TotalEntriesPulled}, TotalEntriesLoggedToKeka={TotalEntriesLoggedToKeka}, PreviousWeekStart={PreviousWeekStart:yyyy-MM-dd}, PreviousWeekEnd={PreviousWeekEnd:yyyy-MM-dd}.",
            employeesToSync.Count,
            totalWeeksPulled,
            totalEntriesPulled,
            totalEntriesLoggedToKeka,
            previousWeek.WeekStartUtc,
            previousWeek.WeekEndUtc);
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
                EmployeeId = member.Id.ToString(CultureInfo.InvariantCulture),
                Email = member.Email,
                DedupeKey = string.Empty,
                LastUpdatedAt = null
            }, stoppingToken);
        }

        logger.LogInformation(
            "Time-entry employee sync complete. ConnectWiseMembers={ConnectWiseMemberCount}, ExistingDbEmployees={ExistingDbEmployeeCount}, InsertedEmployees={InsertedEmployeeCount}, SkippedExistingEmployees={SkippedExistingEmployeeCount}.",
            connectWiseMembers.Count,
            existingEmployeeStates.Count,
            membersToInsert.Count,
            connectWiseMembers.Count - membersToInsert.Count);
    }

    private static DateTime ResolveStartWeekUtc(string? dedupeKey, DateTime previousWeekStartUtc)
    {
        if (!TryParseDedupeWeekStartUtc(dedupeKey, out var dedupeWeekStartUtc))
            return previousWeekStartUtc;

        var nextWeekToSyncUtc = dedupeWeekStartUtc.AddDays(7);
        return nextWeekToSyncUtc <= previousWeekStartUtc ? nextWeekToSyncUtc : previousWeekStartUtc.AddDays(7);
    }

    private static bool TryParseDedupeWeekStartUtc(string? dedupeKey, out DateTime weekStartUtc)
    {
        weekStartUtc = default;

        if (string.IsNullOrWhiteSpace(dedupeKey))
            return false;

        if (!DateTime.TryParseExact(
                dedupeKey.Trim(),
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedUtc))
        {
            return false;
        }

        weekStartUtc = DateTime.SpecifyKind(parsedUtc.Date, DateTimeKind.Utc);
        return true;
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
}

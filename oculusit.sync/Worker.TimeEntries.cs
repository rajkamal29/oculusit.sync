namespace oculusit.sync;

public sealed partial class Worker
{
    private async Task SyncTimeEntriesSmokeAsync(CancellationToken stoppingToken)
    {
        // Daily batch should sync only the last completed UTC day.
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));

        logger.LogInformation("Time-entry smoke sync: fetching ConnectWise entries for completed UTC date {Date}.", date);

        var entries = await connectWiseTimeEntryService.GetTimeEntriesForDayAsync(date, stoppingToken);

        var chargeTypes = entries
            .Select(e => e.ChargeToType)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();

        logger.LogInformation(
            "Time-entry smoke sync complete. Retrieved {Count} ConnectWise time entries for {Date}. Distinct ChargeToType values ({ChargeTypeCount}): {ChargeTypes}.",
            entries.Count,
            date,
            chargeTypes.Count,
            chargeTypes.Count == 0 ? "none" : string.Join(", ", chargeTypes));
    }
}

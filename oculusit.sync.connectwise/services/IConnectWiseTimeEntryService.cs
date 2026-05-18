using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public interface IConnectWiseTimeEntryService
{
    /// <summary>
    /// Fetches ConnectWise time entries for a specific UTC day window
    /// (00:00:00 to 23:59:59.999), ordered by lastUpdated ascending.
    /// Also enriches each entry with member email by calling /system/members/{id}.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForDayAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}

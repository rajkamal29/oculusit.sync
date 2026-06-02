using oculusit.sync.connectwise.modules;

namespace oculusit.sync.connectwise.services;

public interface IConnectWiseTimeEntryService
{
    /// <summary>
    /// Fetches ConnectWise time entries for the UTC Monday-Sunday week that contains the provided date.
    /// Results are ordered by lastUpdated ascending and enriched with member email from /system/members/{id}.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForDayAsync(
        DateOnly date,
        IReadOnlyList<int>? memberIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches ConnectWise time entries for a specific company ID within the UTC Monday-Sunday week
    /// that contains the provided date. Results are ordered by lastUpdated ascending and enriched with member email.
    /// </summary>
    Task<IReadOnlyList<ConnectWiseTimeEntry>> GetTimeEntriesForCompanyAndDayAsync(
        int companyId,
        DateOnly date,
        IReadOnlyList<int>? memberIds = null,
        CancellationToken cancellationToken = default);
}

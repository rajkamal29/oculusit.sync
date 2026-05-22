using oculusit.sync.keka.modules;

namespace oculusit.sync.keka.services;

public interface IKekaTimesheetEntryService
{
    /// <summary>
    /// Creates a Keka timesheet entry and returns the API response.
    /// </summary>
    Task<bool> CreateTimesheetEntryAsync(
        string employeeId,
        KekaTimesheetEntryBatchRequest request,
        CancellationToken cancellationToken = default);
}

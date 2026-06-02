using oculusit.sync.connectwise.modules;

namespace oculusit.sync.orchestration.services;

public interface ITimeEntryOrchestrationService
{
    /// <summary>
    /// Resolves Keka project/task for a ConnectWise time entry and logs hours for the given employee email.
    /// Returns true when the entry was successfully posted to Keka; otherwise false.
    /// </summary>
    Task<bool> LogTimeEntryAsync(
        ConnectWiseTimeEntry entry,
        string employeeEmail,
        CancellationToken cancellationToken = default);
}

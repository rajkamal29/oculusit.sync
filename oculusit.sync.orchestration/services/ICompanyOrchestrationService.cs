namespace oculusit.sync.orchestration;

public interface ICompanyOrchestrationService
{
    /// <summary>
    /// Syncs all ConnectWise companies to Keka — creates new clients or updates existing ones.
    /// </summary>
    Task SyncCompaniesToKekaAsync(CancellationToken cancellationToken = default);
}

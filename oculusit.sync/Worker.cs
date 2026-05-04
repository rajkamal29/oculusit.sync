using oculusit.sync.orchestration;

namespace oculusit.sync;

public sealed class Worker(
    ILogger<Worker> logger,
    ICompanyOrchestrationService orchestration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started. Beginning ConnectWise to Keka sync.");

        await orchestration.SyncCompaniesToKekaAsync(stoppingToken);

        logger.LogInformation("Sync complete. Worker shutting down.");
    }
}

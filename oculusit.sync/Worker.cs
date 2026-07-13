using oculusit.sync.connectwise.services;
using oculusit.sync.core.interfaces;
using oculusit.sync.core.models;
using oculusit.sync.keka.services;
using oculusit.sync.orchestration;
using oculusit.sync.orchestration.services;

namespace oculusit.sync;

public sealed partial class Worker(
    ILogger<Worker> logger,
    IHttpClientFactory httpClientFactory,
    IHostApplicationLifetime lifetime,
    ICompanyOrchestrationService companyOrchestration,
    IProjectOrchestrationService projectOrchestration,
    IProjectStatusOrchestrationService projectStatusOrchestration,
    IKekaClientService kekaClientService,
    IKekaCurrencyService kekaCurrencyService,
    IKekaProjectService kekaProjectService,
    IConnectWiseCompanyService connectWiseService,
    IConnectWiseProjectService connectWiseProjectService,
    IOculusITKekaClientAndProjectService oculusITKekaClientAndProjectService,
    IConnectWiseMemberService connectWiseMemberService,
    IConnectWiseTimeEntryService connectWiseTimeEntryService,
    IConnectWiseTimesheetService connectWiseTimesheetService,
    IKekaEmployeeService kekaEmployeeService,
    ITimeEntryOrchestrationService timeEntryOrchestrationService,
    ISyncStateService syncStateService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Worker started. Beginning ConnectWise to Keka sync.");

            var syncStartedAt = DateTime.UtcNow;

            //await SyncCompaniesAndProjectsAsync(syncStartedAt, stoppingToken);

            //await GenerateProjectTeamMembersExcelAsync(stoppingToken);

            //await GenerateClientRateCardExcelAsync(stoppingToken);

            //await SyncProdEmployeeToDemo(stoppingToken);

            //await syncStateService.EnsureDefaultProjectAsync(stoppingToken);
            //var defaultProjectManager = await kekaEmployeeService.GetDefaultProjectManagerEmployeeAsync(stoppingToken);
            //await syncStateService.EnsureBillingTypeAsync(stoppingToken);
            //await SyncProjectStatusAsync(syncStartedAt, stoppingToken);
            //await SyncTimeEntryEmployeesAsync(stoppingToken);

            //var retryCompanyIds = await GetRetryCompanyIdsFromSyncStateAsync(stoppingToken);
            //await SyncCompaniesAsync(syncStartedAt, retryCompanyIds, defaultProjectManager, stoppingToken);
            //var retryProjectIds = await GetRetryProjectIdsFromSyncStateAsync(stoppingToken);
            //await SyncProjectsAsync(syncStartedAt, retryProjectIds, defaultProjectManager, stoppingToken);
            //var retryTimeSheetIds = await GetRetryTimeSheetIdsFromSyncStateAsync(stoppingToken);
            //await SyncTimeSheetAsync(retryTimeSheetIds, stoppingToken);

            logger.LogInformation("Sync complete. Worker shutting down.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Worker was cancelled before sync completed.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An unhandled exception terminated the sync worker.");
        }
        finally
        {
            // Stop the host so the process exits with a non-zero code,
            // which signals ECS/container orchestrators to restart the task.
            lifetime.StopApplication();
        }
    }
}

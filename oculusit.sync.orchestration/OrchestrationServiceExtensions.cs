using Microsoft.Extensions.DependencyInjection;
using oculusit.sync.orchestration.services;

namespace oculusit.sync.orchestration;

public static class OrchestrationServiceExtensions
{
    public static IServiceCollection AddOrchestrationServices(this IServiceCollection services)
    {
        services.AddSingleton<ICompanyOrchestrationService, CompanyOrchestrationService>();
        services.AddSingleton<IProjectOrchestrationService, ProjectOrchestrationService>();
        services.AddSingleton<IProjectStatusOrchestrationService, ProjectStatusOrchestrationService>();
        services.AddSingleton<ITimeEntryOrchestrationService, TimeEntryOrchestrationService>();
        return services;
    }
}

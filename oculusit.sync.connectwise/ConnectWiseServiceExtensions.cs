using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.services;

namespace oculusit.sync.connectwise;

public static class ConnectWiseServiceExtensions
{
    public static IServiceCollection AddConnectWiseServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ConnectWiseConfiguration>(
            configuration.GetSection(ConnectWiseConfiguration.SectionName));

        services.AddHttpClient(nameof(ConnectWiseService))
                .AddStandardResilienceHandler();

        services.AddSingleton<IConnectWiseCompanyService, ConnectWiseCompanyService>();
        services.AddSingleton<IConnectWiseProjectService, ConnectWiseProjectService>();

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.services;

namespace oculusit.sync.keka;

public static class KekaServiceExtensions
{
    public static IServiceCollection AddKekaServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KekaConfiguration>(
            configuration.GetSection(KekaConfiguration.SectionName));

        services.AddHttpClient(nameof(KekaTokenService))
                .AddStandardResilienceHandler();

        // Singleton so the token cache is shared across all usages
        services.AddSingleton<IKekaTokenService, KekaTokenService>();

        return services;
    }
}

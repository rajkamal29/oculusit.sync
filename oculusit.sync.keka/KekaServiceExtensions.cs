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

        services.AddHttpClient(nameof(KekaClientService))
                .AddStandardResilienceHandler();

        services.AddHttpClient(nameof(KekaCurrencyService))
                .AddStandardResilienceHandler();

        services.AddSingleton<IKekaTokenService, KekaTokenService>();
        services.AddSingleton<IKekaClientService, KekaClientService>();
        services.AddSingleton<IKekaCurrencyService, KekaCurrencyService>();

        return services;
    }
}

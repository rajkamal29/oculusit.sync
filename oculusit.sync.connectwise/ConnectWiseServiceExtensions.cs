using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using oculusit.sync.connectwise.configurations;
using oculusit.sync.connectwise.services;

namespace oculusit.sync.connectwise;

public static class ConnectWiseServiceExtensions
{
    // The ConnectWise API can be slow when fetching large paged result sets
    // (companies, projects, time entries). Raise the attempt timeout well above
    // the 10 s default so a single page fetch is not prematurely cancelled.
    private static readonly TimeSpan AttemptTimeout         = TimeSpan.FromMinutes(2);   // 120 s per attempt
    private static readonly TimeSpan CircuitBreakerSampling = TimeSpan.FromMinutes(5);   // must be >= 2× attempt timeout
    private static readonly TimeSpan TotalTimeout           = TimeSpan.FromMinutes(10);  // overall ceiling across all retries

    public static IServiceCollection AddConnectWiseServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ConnectWiseConfiguration>(
            configuration.GetSection(ConnectWiseConfiguration.SectionName));

        services.AddHttpClient(nameof(ConnectWiseService))
                .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout         = AttemptTimeout;
                    options.CircuitBreaker.SamplingDuration = CircuitBreakerSampling;
                    options.TotalRequestTimeout.Timeout    = TotalTimeout;
                });

        services.AddSingleton<IConnectWiseCompanyService, ConnectWiseCompanyService>();
        services.AddSingleton<IConnectWiseMemberService, ConnectWiseMemberService>();
        services.AddSingleton<IConnectWiseProjectService, ConnectWiseProjectService>();
        services.AddSingleton<IConnectWiseTimeEntryService, ConnectWiseTimeEntryService>();
        services.AddSingleton<IConnectWiseTimesheetService, ConnectWiseTimesheetService>();

        return services;
    }
}

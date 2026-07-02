using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using oculusit.sync.keka.configurations;
using oculusit.sync.keka.services;

namespace oculusit.sync.keka;

public static class KekaServiceExtensions
{
    // Total timeout per attempt for Keka project calls — project creation/update
    // can be slow on the Keka side, so we allow longer than the default 30 s.
    private static readonly TimeSpan ProjectAttemptTimeout         = TimeSpan.FromMinutes(2);   // 120 s per attempt
    private static readonly TimeSpan ProjectCircuitBreakerSampling = TimeSpan.FromMinutes(5);   // must be >= 2× attempt timeout (240 s)
    private static readonly TimeSpan ProjectTotalTimeout           = TimeSpan.FromMinutes(10);  // overall ceiling across all retries

    // Total timeout per attempt for Keka client calls — client creation/update
    // can be slow on the Keka side, matching the project service timeouts.
    private static readonly TimeSpan ClientAttemptTimeout         = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ClientCircuitBreakerSampling = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClientTotalTimeout           = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddKekaServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KekaConfiguration>(
            configuration.GetSection(KekaConfiguration.SectionName));

        services.AddHttpClient(nameof(KekaTokenService))
                .AddStandardResilienceHandler();

        services.AddHttpClient(nameof(KekaClientService))
                .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout = ClientAttemptTimeout;
                    options.CircuitBreaker.SamplingDuration = ClientCircuitBreakerSampling;
                    options.TotalRequestTimeout.Timeout = ClientTotalTimeout;
                });

        services.AddHttpClient(nameof(KekaCurrencyService))
                .AddStandardResilienceHandler();

        services.AddHttpClient(nameof(KekaProjectService))
                .AddStandardResilienceHandler(options =>
                {
                    // Increase attempt timeout to handle slow Keka project API responses.
                    options.AttemptTimeout.Timeout = ProjectAttemptTimeout;
                    // SamplingDuration must be >= 2× AttemptTimeout to satisfy Polly's validation.
                    options.CircuitBreaker.SamplingDuration = ProjectCircuitBreakerSampling;
                    options.TotalRequestTimeout.Timeout = ProjectTotalTimeout;
                });

        services.AddHttpClient(nameof(KekaTimesheetEntryService))
                .AddStandardResilienceHandler();

        services.AddSingleton<IKekaTokenService, KekaTokenService>();
        services.AddSingleton<IKekaClientService, KekaClientService>();
        services.AddSingleton<IKekaCurrencyService, KekaCurrencyService>();
        services.AddSingleton<IKekaProjectService, KekaProjectService>();
        services.AddSingleton<IKekaTimesheetEntryService, KekaTimesheetEntryService>();
        services.AddSingleton<IKekaEmployeeService, KekaEmployeeService>();
        services.AddSingleton<IKekaFinanceService, KekaFinanceService>();
        services.AddSingleton<IOculusITKekaClientAndProjectService, OculusITKekaClientAndProjectService>();

        return services;
    }
}

using oculusit.sync.connectwise;
using oculusit.sync.core;
using oculusit.sync.exceptions;
using oculusit.sync.keka;
using oculusit.sync.orchestration;
using Serilog;

namespace oculusit.sync
{
    public class Program
    {
        public static int Main(string[] args)
        {
            // Bootstrap logger captures any startup failures before the host is built.
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                var builder = Host.CreateApplicationBuilder(args);

                // Configuration is supplied via environment variables injected by ECS
                // from SSM Parameter Store at task startup. No AWS SDK calls needed here.
                // Locally, appsettings.json and user secrets are used instead.
                builder.Services.AddCoreServices(builder.Configuration);

                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .CreateLogger();

                builder.Services.AddSerilog();

                // Register global exception handler first so it is running before the worker starts.
                builder.Services.AddHostedService<GlobalExceptionHandler>();

                builder.Services.AddKekaServices(builder.Configuration);
                builder.Services.AddConnectWiseServices(builder.Configuration);
                builder.Services.AddOrchestrationServices();

                builder.Services.AddHostedService<Worker>();

                var host = builder.Build();
                host.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly during startup.");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}

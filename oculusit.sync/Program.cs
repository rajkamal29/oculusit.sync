using oculusit.sync.connectwise;
using oculusit.sync.keka;
using oculusit.sync.orchestration;
using Serilog;

namespace oculusit.sync
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            Log.Logger = new LoggerConfiguration()
               .ReadFrom.Configuration(builder.Configuration)
               .Enrich.FromLogContext()
               .CreateLogger();

            builder.Services.AddSerilog();

            builder.Services.AddKekaServices(builder.Configuration);
            builder.Services.AddConnectWiseServices(builder.Configuration);
            builder.Services.AddOrchestrationServices();

            builder.Services.AddHostedService<Worker>();
            var host = builder.Build();
            host.Run();
        }
    }
}

using MPCore.Hosting;
using MPCore.Observability;
using Shipping.Modules.Sample.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddMPCoreFoundation(new MPCoreObservabilityOptions
{
    ServiceName = "Shipping.Platform",
    ServiceNamespace = "Shipping",
    ServiceVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
    EnableOtlpExporter = builder.Configuration.GetValue("Observability:EnableOtlpExporter", false),
    EnableConsoleLogExporter = builder.Configuration.GetValue("Observability:EnableConsoleLogExporter", true)
});
builder.Services.AddSampleModule();

var app = builder.Build();
app.MapSampleModule();
await app.RunAsync();

public partial class Program;

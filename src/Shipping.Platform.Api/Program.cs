using MPCore.Hosting;
using MPCore.Observability;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
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

// No connection string is committed: user secrets in Development, the environment elsewhere.
var databaseConnection = builder.Configuration.GetConnectionString("PostgreSql");
if (string.IsNullOrWhiteSpace(databaseConnection))
{
    throw new InvalidOperationException(
        "ConnectionStrings:PostgreSql is required. Set it with dotnet user-secrets or the ConnectionStrings__PostgreSql environment variable.");
}

// One database; each module registers its own MP Core context and unit of work in its own schema (ADR-002).
// The host owns no context and binds no global IUnitOfWork. It registers what every module context needs once:
// the time provider (AddMPCoreFoundation) and the aggregate event sink. It never migrates the database; each
// module's migrations are applied as a deployment step (docs/persistence.md).
builder.Services.AddMPCoreAggregateEvents();
builder.Services.AddSampleModule(databaseConnection);

var app = builder.Build();
app.MapSampleModule();
await app.RunAsync();

public partial class Program;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Shipping.Modules.Sample.Application.Ports;
using Shipping.Modules.Sample.Infrastructure.Persistence;

namespace Shipping.Modules.Sample.Infrastructure;

public static class ModuleRegistration
{
    /// <summary>
    /// Registers Sample's own runtime context with MP Core's PostgreSQL provider setup, and Sample's unit of work
    /// as that same scoped instance. It deliberately does not bind the host-wide <c>IUnitOfWork</c>: MP Core's
    /// <c>AddMPCorePostgreSql</c> binds it first-come-first-served, which would hand one module's context to
    /// every module (ADR-002). The host registers the time provider and the aggregate event sink once.
    /// </summary>
    public static IServiceCollection AddSampleInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<SampleDbContext>(options => PostgreSqlDbContextOptions.Apply(options, connectionString));
        services.AddScoped<ISampleUnitOfWork>(static provider => provider.GetRequiredService<SampleDbContext>());
        // The host's GET /health asks this module whether its context reaches the database (SLOS-127). Only the
        // check is added: the host registers the health check service once, as it does the time provider. The
        // timeout keeps a database that does not answer below the container probe's 3 s.
        services.AddSingleton<SampleDatabaseHealthCheck.Attempt>();
        services.Configure<HealthCheckServiceOptions>(static options => options.Registrations.Add(new HealthCheckRegistration(
            $"{SamplePersistence.Schema}-database",
            static provider => ActivatorUtilities.CreateInstance<SampleDatabaseHealthCheck>(provider),
            HealthStatus.Unhealthy, tags: [], timeout: TimeSpan.FromSeconds(2))));
        return services;
    }
}

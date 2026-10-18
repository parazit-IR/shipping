using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MPCore.Domain.Events;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;

namespace Shipping.Modules.Sample.Infrastructure.Persistence;

/// <summary>
/// Builds <see cref="SampleDbContext"/> for <c>dotnet ef</c> and migration bundles, without starting the host.
/// </summary>
/// <remarks>
/// As in MP Core's generated design-time factory, the connection string comes from the environment only,
/// never from a configuration file: a migration names the database it is applied to deliberately.
/// </remarks>
internal sealed class SampleDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SampleDbContext>
{
    /// <summary>The environment variable naming the database the design-time tools work against.</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__PostgreSql";

    public SampleDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"Set {ConnectionStringVariable} to the database this migration is written against or applied to.");

        var options = new DbContextOptionsBuilder<SampleDbContext>();
        PostgreSqlDbContextOptions.Apply(options, connectionString);
        return new SampleDbContext(options.Options, TimeProvider.System, NullAggregateEventSink.Instance);
    }
}

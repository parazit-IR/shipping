using Microsoft.EntityFrameworkCore;

namespace Shipping.Modules.Sample.Infrastructure.Persistence;

/// <summary>What the Sample module owns in the Shipping database: one PostgreSQL schema and its mappings.</summary>
internal static class SamplePersistence
{
    /// <summary>The module's schema, the default of <see cref="SampleDbContext"/>. Every table of the module goes here.</summary>
    public const string Schema = "sample";

    /// <summary>Applies the module's EF mappings: every <c>IEntityTypeConfiguration</c> in this assembly.</summary>
    public static ModelBuilder ApplyModel(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        return modelBuilder.ApplyConfigurationsFromAssembly(typeof(SamplePersistence).Assembly);
    }
}

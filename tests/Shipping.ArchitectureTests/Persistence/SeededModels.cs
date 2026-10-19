using Microsoft.EntityFrameworkCore;
using Shipping.Tests.Shared;

namespace Shipping.ArchitectureTests.Persistence;

// Test fixtures only: small models that each break one rule on purpose, so every rule is seen failing on a
// seeded violation and not only passing on a model that happens to be empty. Two pretend modules, Alpha
// and Beta, own SeedRow and SeedOther.

public sealed class SeedRow
{
    public Guid Id { get; set; }
    public Guid OtherId { get; set; }
}

public sealed class SeedOther
{
    public Guid Id { get; set; }
}

internal static class SeededModels
{
    public static string? ModuleOf(Type type) =>
        type == typeof(SeedRow) ? "Alpha" : type == typeof(SeedOther) ? "Beta" : null;

    public static TContext Create<TContext>(Func<DbContextOptions<TContext>, TContext> create) where TContext : DbContext =>
        create(new DbContextOptionsBuilder<TContext>().UseNpgsql(ModulePersistence.ModelOnlyConnection).Options);
}

/// <summary>Alpha and Beta each in a schema of their own, with no key between them: holds every rule.</summary>
public sealed class ConsistentContext(DbContextOptions<ConsistentContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeedRow>().ToTable("rows", "alpha");
        modelBuilder.Entity<SeedOther>().ToTable("others", "beta");
    }
}

/// <summary>Alpha's table in Beta's schema.</summary>
public sealed class ForeignSchemaContext(DbContextOptions<ForeignSchemaContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeedRow>().ToTable("rows", "beta");
        modelBuilder.Entity<SeedOther>().ToTable("others", "beta");
    }
}

/// <summary>Alpha's table referencing Beta's with a foreign key.</summary>
public sealed class CrossSchemaKeyContext(DbContextOptions<CrossSchemaKeyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeedRow>().ToTable("rows", "alpha").HasOne<SeedOther>().WithMany().HasForeignKey(row => row.OtherId);
        modelBuilder.Entity<SeedOther>().ToTable("others", "beta");
    }
}

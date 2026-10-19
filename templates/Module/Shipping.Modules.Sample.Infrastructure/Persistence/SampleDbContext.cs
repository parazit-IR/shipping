using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using MPCore.Domain.Events;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Shipping.Modules.Sample.Application.Ports;

namespace Shipping.Modules.Sample.Infrastructure.Persistence;

/// <summary>
/// The Sample module's runtime context and unit of work: MP Core's <see cref="MPCoreDbContext"/>, so a save stamps
/// timestamps and drains aggregate events as MP Core does. It maps only Sample's entities, into the
/// <c>sample</c> schema, and records Sample's migrations in <c>sample.__EFMigrationsHistory</c>.
/// </summary>
/// <remarks>
/// Internal: no other module and not the host can reach it. Sample's handlers depend on
/// <see cref="ISampleUnitOfWork"/>. See docs/architecture/ADR-002-module-persistence.md.
/// </remarks>
internal sealed class SampleDbContext(DbContextOptions<SampleDbContext> options, TimeProvider timeProvider, IAggregateEventSink eventSink)
    : MPCoreDbContext(options, timeProvider, eventSink), ISampleUnitOfWork
{
    /// <summary>Applies to whoever builds the options, so the history can never land in another schema.</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, SamplePersistence.Schema));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SamplePersistence.Schema);
        SamplePersistence.ApplyModel(modelBuilder);
    }
}

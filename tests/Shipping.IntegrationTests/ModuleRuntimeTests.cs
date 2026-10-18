using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MPCore.Domain.Events;
using MPCore.Persistence.Abstractions;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Shipping.Tests.Shared;

namespace Shipping.IntegrationTests;

/// <summary>
/// SLS-125 at run time, against a real PostgreSQL: the real host's container hands each module its own MP Core
/// context and unit of work, a write lands in that module's schema only, and MP Core's save behaviour holds per
/// module. Sample has no entities, so writes go through two probe modules registered the way Sample is.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class ModuleRuntimeTests(PostgreSqlFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_hosts_module_contexts_connect_to_the_one_database_and_each_defaults_to_its_own_schema()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        await Sql.MigrateProbesAsync(database);
        await using var host = Host(database, new RecordingEventSink());
        using var scope = host.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<IUnitOfWork>());
        foreach (var module in ModulePersistence.Modules)
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(module.UnitOfWork);
            Assert.IsAssignableFrom<MPCoreDbContext>(context);
            Assert.True(await context.Database.CanConnectAsync());
            Assert.Equal(module.Schema, context.Model.GetDefaultSchema());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            // A module with nothing tracked commits nothing, whatever another module tracks.
            scope.ServiceProvider.GetRequiredService<IAlphaProbeUnitOfWork>().Records().Add(new ProbeRecord(Guid.NewGuid(), "untouched"));
            Assert.Equal(0, await ((IUnitOfWork)context).SaveChangesAsync());
        }

        Assert.Empty(await Sql.ProbeIdsAsync(database, AlphaProbeDbContext.SchemaName));
    }

    [Fact]
    public async Task A_write_through_a_modules_unit_of_work_lands_in_its_schema_with_MP_Cores_save_behaviour()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateProbesAsync(database);
        var sink = new RecordingEventSink();
        await using var host = Host(database, sink);
        var id = Guid.NewGuid();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAlphaProbeUnitOfWork>();
            var record = new ProbeRecord(id, "written");
            unitOfWork.Records().Add(record);

            Assert.Equal(1, await unitOfWork.SaveChangesAsync());
            // MP Core drained the aggregate's events into the sink before the write, and cleared them.
            Assert.Empty(record.DomainEvents);
        }

        Assert.Equal([new ProbeRecorded(id)], sink.DomainEvents.Cast<ProbeRecorded>());
        Assert.Equal([id.ToString()], await Sql.ProbeIdsAsync(database, AlphaProbeDbContext.SchemaName));
        Assert.Empty(await Sql.ProbeIdsAsync(database, BetaProbeDbContext.SchemaName));
        Assert.False(await Sql.TableExistsAsync(database, "public", ProbeDbContext.Table));
        // MP Core stamped the creation instant from the host's TimeProvider.
        Assert.Equal([Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)], await Sql.ListAsync(database,
            $"SELECT extract(epoch FROM \"CreatedOnUtc\")::bigint FROM \"{AlphaProbeDbContext.SchemaName}\".\"{ProbeDbContext.Table}\""));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = await scope.ServiceProvider.GetRequiredService<IAlphaProbeUnitOfWork>().Records().SingleAsync();
            Assert.Equal((id, "written", Now), (read.Id, read.Name, read.CreatedOnUtc));
        }
    }

    [Fact]
    public async Task A_save_that_fails_commits_nothing_of_that_module()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateProbesAsync(database);
        var sink = new RecordingEventSink();
        await using var host = Host(database, sink);
        var existing = Guid.NewGuid();
        await SaveAsync<IAlphaProbeUnitOfWork>(host, new ProbeRecord(existing, "existing"));

        // The event sink fails before anything is written: MP Core drains events first.
        sink.Fail = true;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAlphaProbeUnitOfWork>();
            unitOfWork.Records().Add(new ProbeRecord(Guid.NewGuid(), "lost with the sink"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync());
        }

        // One row of two violates the key: neither commits.
        sink.Fail = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAlphaProbeUnitOfWork>();
            unitOfWork.Records().Add(new ProbeRecord(Guid.NewGuid(), "lost with the duplicate"));
            unitOfWork.Records().Add(new ProbeRecord(existing, "duplicate"));
            await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync());
        }

        Assert.Equal([existing.ToString()], await Sql.ProbeIdsAsync(database, AlphaProbeDbContext.SchemaName));
    }

    [Fact]
    public async Task Two_modules_units_of_work_in_one_scope_do_not_interfere()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateProbesAsync(database);
        await using var host = Host(database, new RecordingEventSink());
        var betaExisting = Guid.NewGuid();
        await SaveAsync<IBetaProbeUnitOfWork>(host, new ProbeRecord(betaExisting, "beta existing"));
        var alphaNew = Guid.NewGuid();
        var betaNew = Guid.NewGuid();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var alpha = scope.ServiceProvider.GetRequiredService<IAlphaProbeUnitOfWork>();
            var beta = scope.ServiceProvider.GetRequiredService<IBetaProbeUnitOfWork>();
            Assert.NotSame(alpha, beta);
            alpha.Records().Add(new ProbeRecord(alphaNew, "alpha new"));
            beta.Records().Add(new ProbeRecord(betaNew, "beta new"));
            beta.Records().Add(new ProbeRecord(betaExisting, "beta duplicate"));

            // Beta's failure is Beta's: Alpha's pending change survives it and commits on its own.
            await Assert.ThrowsAsync<DbUpdateException>(() => beta.SaveChangesAsync());
            Assert.Equal(1, await alpha.SaveChangesAsync());
        }

        Assert.Equal([alphaNew.ToString()], await Sql.ProbeIdsAsync(database, AlphaProbeDbContext.SchemaName));
        Assert.Equal([betaExisting.ToString()], await Sql.ProbeIdsAsync(database, BetaProbeDbContext.SchemaName));
    }

    private static async Task SaveAsync<TUnitOfWork>(WebApplicationFactory<Program> host, ProbeRecord record)
        where TUnitOfWork : class, IUnitOfWork
    {
        await using var scope = host.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<TUnitOfWork>();
        unitOfWork.Records().Add(record);
        Assert.Equal(1, await unitOfWork.SaveChangesAsync());
    }

    /// <summary>The real host, with the probe modules registered beside Sample and a recording event sink and clock.</summary>
    private static WebApplicationFactory<Program> Host(string database, IAggregateEventSink sink) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:PostgreSql", database);
            host.ConfigureTestServices(services =>
            {
                services.AddProbeModule<AlphaProbeDbContext, IAlphaProbeUnitOfWork>(database);
                services.AddProbeModule<BetaProbeDbContext, IBetaProbeUnitOfWork>(database);
                services.RemoveAll<IAggregateEventSink>().AddSingleton(sink);
                services.RemoveAll<TimeProvider>().AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            });
        });
}

internal static class ProbeUnitOfWorkExtensions
{
    public static DbSet<ProbeRecord> Records(this IUnitOfWork unitOfWork) => ((ProbeDbContext)unitOfWork).Records;
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shipping.Tests.Shared;

namespace Shipping.IntegrationTests;

/// <summary>SLS-125 against a real PostgreSQL: each module's migrations, applied the way a deployment applies them.</summary>
[Collection(PostgreSqlCollection.Name)]
public class ModuleMigrationTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task A_fresh_database_gets_each_modules_schema_and_a_migration_history_inside_it()
    {
        var database = await postgres.CreateDatabaseAsync();
        Assert.NotEmpty(ModulePersistence.Modules);

        await Sql.MigrateModulesAsync(database);

        foreach (var module in ModulePersistence.Modules)
        {
            await using var context = ModulePersistence.CreateModuleContext(module, database);
            Assert.Contains(module.Schema, await Sql.SchemasAsync(database));
            Assert.Equal(context.Database.GetMigrations(), await Sql.HistoryAsync(database, module.Schema));
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }

        // No module records its history in the shared default location.
        Assert.False(await Sql.TableExistsAsync(database, "public", HistoryRepository.DefaultTableName));
    }

    [Fact]
    public async Task Applying_the_migrations_again_changes_nothing()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        var before = await Sql.DescribeAsync(database);

        await Sql.MigrateModulesAsync(database);

        Assert.Equal(before, await Sql.DescribeAsync(database));
        foreach (var module in ModulePersistence.Modules)
        {
            await using var context = ModulePersistence.CreateModuleContext(module, database);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }
    }

    [Fact]
    public async Task Each_modules_migrations_apply_on_their_own_and_create_nothing_of_another_module()
    {
        var database = await postgres.CreateDatabaseAsync();
        Assert.NotEmpty(ModulePersistence.Modules);

        // Each migration, run alone, adds exactly its own module's schema and nothing else.
        var steps = ModulePersistence.Modules
            .Select(static module => (module.Schema, Create: (Func<string, DbContext>)(db => ModulePersistence.CreateModuleContext(module, db))))
            .Append((AlphaProbeDbContext.SchemaName, static db => AlphaProbeDbContext.Create(db)))
            .Append((BetaProbeDbContext.SchemaName, static db => BetaProbeDbContext.Create(db)));
        foreach (var (schema, create) in steps)
        {
            var before = await Sql.SchemasAsync(database);
            await using (var context = create(database))
            {
                await context.Database.MigrateAsync();
            }

            Assert.Equal([schema], (await Sql.SchemasAsync(database)).Except(before));
        }
    }

    [Fact]
    public async Task One_modules_migration_history_neither_sees_nor_disturbs_anothers()
    {
        var database = await postgres.CreateDatabaseAsync();

        await using (var probe = AlphaProbeDbContext.Create(database))
        {
            await probe.Database.MigrateAsync();
        }

        var probeHistory = await Sql.HistoryAsync(database, AlphaProbeDbContext.SchemaName);
        await Sql.MigrateModulesAsync(database);

        Assert.Equal(["20261015000000_AlphaProbeBaseline"], await Sql.HistoryAsync(database, AlphaProbeDbContext.SchemaName));
        Assert.Equal(probeHistory, await Sql.HistoryAsync(database, AlphaProbeDbContext.SchemaName));
        foreach (var module in ModulePersistence.Modules)
        {
            await using var context = ModulePersistence.CreateModuleContext(module, database);
            // The module's history holds its own migrations and nothing of the probe's, though both tables share a name.
            Assert.Equal(context.Database.GetMigrations(), await Sql.HistoryAsync(database, module.Schema));
        }

        // Rolling the probe back to nothing touches no module's schema or history.
        var modulesBefore = await Sql.DescribeAsync(database, AlphaProbeDbContext.SchemaName);
        await using (var probe = AlphaProbeDbContext.Create(database))
        {
            await probe.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        }

        Assert.Empty(await Sql.HistoryAsync(database, AlphaProbeDbContext.SchemaName));
        Assert.Equal(modulesBefore, await Sql.DescribeAsync(database, AlphaProbeDbContext.SchemaName));
    }

    [Fact]
    public async Task Every_table_and_column_of_each_modules_runtime_model_exists_in_its_schema_after_its_migrations()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        await Sql.MigrateProbesAsync(database);
        var columns = (await Sql.DescribeAsync(database)).ToHashSet(StringComparer.Ordinal);

        var contexts = ModulePersistence.Modules.Select(module => ModulePersistence.CreateModuleContext(module, database))
            .Append(AlphaProbeDbContext.Create(database)).Append(BetaProbeDbContext.Create(database)).ToList();
        try
        {
            foreach (var context in contexts)
            {
                Assert.True(await context.Database.CanConnectAsync());
                var schema = context.Model.GetDefaultSchema();
                var tables = context.Model.GetRelationalModel().Tables.ToList();
                Assert.All(tables, table => Assert.Equal(schema, table.Schema));
                var missing = tables
                    .SelectMany(static table => table.Columns.Select(column => $"{table.Schema}.{table.Name}.{column.Name}"))
                    .Where(column => !columns.Any(described => described.StartsWith(column + " ", StringComparison.Ordinal)))
                    .ToList();
                Assert.True(missing.Count == 0, $"{context.GetType().Name} writes columns no migration created: " + string.Join(", ", missing));
            }
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }
}

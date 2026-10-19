using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MPCore.Persistence.Abstractions;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using NetArchTest.Rules;
using Shipping.Tests.Shared;

namespace Shipping.ArchitectureTests.Persistence;

/// <summary>
/// SLS-125 / ADR-002: each module owns a runtime MP Core context, its schema, its mappings, its migrations and
/// its migration history; the host owns no context. No database is needed.
/// </summary>
public class PersistenceRulesTests
{
    private static readonly string[] ProviderNamespaces = ["Microsoft.EntityFrameworkCore", "Npgsql"];

    [Fact]
    public void Every_module_owns_exactly_one_runtime_context_named_after_it_and_it_is_an_MP_Core_unit_of_work()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        foreach (var module in ModulePersistence.Modules)
        {
            Assert.True(module.Contexts.Count == 1, $"Module {module} must own exactly one DbContext; found {module.Contexts.Count}.");
            Assert.Equal($"{module.Name}DbContext", module.Context.Name);
            Assert.True(typeof(MPCoreDbContext).IsAssignableFrom(module.Context), $"{module.Context} is not an MPCoreDbContext.");
        }
    }

    [Fact]
    public void Every_module_context_is_a_distinct_type()
    {
        var contexts = ModulePersistence.Modules.Select(static module => module.Context).ToList();
        Assert.Equal(contexts.Count, contexts.Distinct().Count());
        Assert.All(ModulePersistence.Modules, module => Assert.Equal(module.Infrastructure, module.Context.Assembly));
    }

    [Fact]
    public void Each_module_declares_its_own_unit_of_work_port_and_only_its_context_implements_it()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        foreach (var module in ModulePersistence.Modules)
        {
            var port = module.UnitOfWork;
            Assert.True(port.IsPublic, $"{port} must be public: the module's handlers depend on it.");
            Assert.True(typeof(IUnitOfWork).IsAssignableFrom(port), $"{port} must extend IUnitOfWork.");
            Assert.True(port.IsAssignableFrom(module.Context), $"{module.Context} does not implement {port}.");
            foreach (var other in ModulePersistence.Modules.Where(other => other != module))
            {
                Assert.False(port.IsAssignableFrom(other.Context), $"{other.Context} implements {module}'s port {port}.");
            }
        }
    }

    [Fact]
    public void A_module_context_is_not_visible_outside_its_module()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        var host = typeof(Program).Assembly;
        foreach (var module in ModulePersistence.Modules)
        {
            Assert.False(module.Context.IsVisible, $"{module.Context} is public; only its module may write through it.");
            var persistence = module.Context.Namespace!;
            var failures = new List<string>();
            var outsiders = ModulePersistence.Modules
                .Where(other => other != module)
                .SelectMany(static other => new[] { other.Infrastructure, other.Application, other.Api })
                .Append(host)
                .Append(module.Api);
            foreach (var assembly in outsiders)
            {
                var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(persistence).GetResult();
                if (!result.IsSuccessful)
                {
                    failures.Add($"{assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
                }
            }

            Assert.True(failures.Count == 0, $"Code outside {module}'s Infrastructure depends on {persistence}: " + string.Join("; ", failures));
        }
    }

    [Fact]
    public void Each_module_context_uses_its_own_schema_by_default_and_records_its_migrations_there()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        foreach (var module in ModulePersistence.Modules)
        {
            using var context = ModulePersistence.CreateModuleContext(module, ModulePersistence.ModelOnlyConnection);
            Assert.Equal(module.Schema, context.Model.GetDefaultSchema());
            var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
            Assert.Equal(module.Schema, relational.MigrationsHistoryTableSchema);
            Assert.Equal(HistoryRepository.DefaultTableName, relational.MigrationsHistoryTableName ?? HistoryRepository.DefaultTableName);
            Assert.Equal(module.Infrastructure, context.GetService<IMigrationsAssembly>().Assembly);
        }
    }

    [Fact]
    public void Each_module_has_a_baseline_migration_and_no_pending_model_change()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        foreach (var module in ModulePersistence.Modules)
        {
            using var context = ModulePersistence.CreateModuleContext(module, ModulePersistence.ModelOnlyConnection);
            Assert.NotEmpty(context.Database.GetMigrations());
            Assert.False(context.Database.HasPendingModelChanges(),
                $"{module.Context.Name} has model changes without a migration: dotnet ef migrations add <Name> --context {module.Context.Name}");
        }
    }

    [Fact]
    public void Every_migration_belongs_to_a_module_context_in_that_modules_assembly()
    {
        var moduleContexts = ModulePersistence.Modules.Select(static module => module.Context).ToHashSet();
        var assemblies = ModulePersistence.Modules.Select(static module => module.Infrastructure).Append(typeof(Program).Assembly);
        var failures = assemblies
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => typeof(Migration).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(static type => (Migration: type, Context: type.GetCustomAttribute<DbContextAttribute>()?.ContextType))
            .Where(entry => entry.Context is null || !moduleContexts.Contains(entry.Context) || entry.Context.Assembly != entry.Migration.Assembly)
            .Select(static entry => $"{entry.Migration.FullName} -> {entry.Context?.FullName ?? "no context"}")
            .ToList();
        Assert.True(failures.Count == 0, "Only a module's own context owns migrations, in its own assembly: " + string.Join("; ", failures));
    }

    [Fact]
    public void The_host_declares_no_DbContext_of_its_own()
    {
        var contexts = typeof(Program).Assembly.GetTypes().Where(static type => typeof(DbContext).IsAssignableFrom(type)).ToList();
        Assert.True(contexts.Count == 0, "A shared runtime context in the host: " + string.Join(", ", contexts));
    }

    [Fact]
    public void Each_module_context_maps_only_its_own_entities_into_its_own_schema()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        var failures = new List<string>();
        foreach (var module in ModulePersistence.Modules)
        {
            using var context = ModulePersistence.CreateModuleContext(module, ModulePersistence.ModelOnlyConnection);
            failures.AddRange(PersistenceModelRules.SchemaOwnership(context.Model, PersistenceModelRules.ModuleOf));
            failures.AddRange(context.Model.GetEntityTypes()
                .Where(entity => entity.GetTableName() is not null && PersistenceModelRules.ModuleOf(entity.ClrType) != module.Name)
                .Select(entity => $"{module.Context.Name} maps {entity.ClrType.FullName}, which is not {module.Name}'s"));
        }

        Assert.True(failures.Count == 0, "Modules that do not own exactly one schema: " + string.Join("; ", failures));
    }

    [Fact]
    public void No_foreign_key_crosses_a_schema()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        var failures = new List<string>();
        foreach (var module in ModulePersistence.Modules)
        {
            using var context = ModulePersistence.CreateModuleContext(module, ModulePersistence.ModelOnlyConnection);
            failures.AddRange(PersistenceModelRules.ForeignKeysAcrossSchemas(context.Model));
        }

        Assert.True(failures.Count == 0, "Foreign keys that cross a schema: " + string.Join("; ", failures));
    }

    [Fact]
    public void Each_module_registers_its_context_with_MP_Cores_provider_setup_and_never_binds_the_global_unit_of_work()
    {
        // AddMPCorePostgreSql and AddMPCoreUnitOfWork bind IUnitOfWork with TryAddScoped: with two modules the
        // first registered wins and the second module's handlers would save the wrong context
        // (UnitOfWorkCompositionTests pins that MP Core behaviour). Each module therefore registers its own
        // context and port, reusing only MP Core's provider configuration.
        var forbidden = new[] { nameof(PostgreSqlServiceCollectionExtensions.AddMPCorePostgreSql), nameof(PostgreSqlServiceCollectionExtensions.AddMPCoreUnitOfWork) };
        var assemblies = ModulePersistence.Modules
            .SelectMany(static module => new[] { module.Infrastructure, module.Api })
            .Append(typeof(Program).Assembly)
            .ToList();
        var calls = assemblies.SelectMany(IlCalls.In).ToList();
        var failures = calls
            .Where(call => call.Called.DeclaringType == typeof(PostgreSqlServiceCollectionExtensions) && forbidden.Contains(call.Called.Name))
            .Select(static call => $"{call.Caller.DeclaringType?.FullName}.{call.Caller.Name} calls {call.Called.Name}")
            .ToList();
        Assert.True(failures.Count == 0, "The global IUnitOfWork is bound to one context: " + string.Join("; ", failures));

        foreach (var module in ModulePersistence.Modules)
        {
            Assert.Contains(IlCalls.In(module.Infrastructure), static call =>
                call.Called.DeclaringType == typeof(PostgreSqlDbContextOptions) && call.Called.Name == nameof(PostgreSqlDbContextOptions.Apply));
        }
    }

    [Fact]
    public void Nothing_in_the_host_or_a_module_creates_or_migrates_the_database()
    {
        // Migrations are a deployment step (MP Core: "Automatic migration on startup: Not available").
        var forbidden = new[] { "Migrate", "MigrateAsync", "EnsureCreated", "EnsureCreatedAsync", "EnsureDeleted", "EnsureDeletedAsync" };
        var assemblies = ModulePersistence.Modules
            .SelectMany(static module => new[] { module.Infrastructure, module.Api })
            .Append(typeof(Program).Assembly);
        var failures = assemblies.SelectMany(IlCalls.In)
            .Where(call => forbidden.Contains(call.Called.Name)
                && call.Called.DeclaringType?.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
            .Select(static call => $"{call.Caller.DeclaringType?.FullName}.{call.Caller.Name} calls {call.Called.DeclaringType!.Name}.{call.Called.Name}")
            .ToList();
        Assert.True(failures.Count == 0, "Schema changes at run time: " + string.Join("; ", failures));
    }

    [Fact]
    public void Domain_and_Application_never_see_Entity_Framework_or_Npgsql()
    {
        Assert.NotEmpty(ModulePersistence.Modules);
        foreach (var module in ModulePersistence.Modules)
        {
            foreach (var layer in new[] { "Domain", "Application" })
            {
                var result = Types.InAssembly(Assembly.Load($"Shipping.Modules.{module.Name}.{layer}"))
                    .ShouldNot().HaveDependencyOnAny(ProviderNamespaces).GetResult();
                Assert.True(result.IsSuccessful,
                    $"{module}.{layer} depends on a provider: " + string.Join(", ", result.FailingTypeNames ?? []));
            }
        }
    }

    // Each model rule, seen failing on a seeded violation.

    [Fact]
    public void Schema_rule_rejects_a_table_in_another_modules_schema()
    {
        using var consistent = SeededModels.Create<ConsistentContext>(static options => new(options));
        Assert.Empty(PersistenceModelRules.SchemaOwnership(consistent.Model, SeededModels.ModuleOf));

        using var violating = SeededModels.Create<ForeignSchemaContext>(static options => new(options));
        var failures = PersistenceModelRules.SchemaOwnership(violating.Model, SeededModels.ModuleOf);
        Assert.Contains(failures, failure => failure == "Alpha maps into beta, not its own schema alpha");
        Assert.Contains(failures, failure => failure == "Alpha maps into beta, the schema of Beta");
    }

    [Fact]
    public void Foreign_key_rule_rejects_a_key_across_schemas()
    {
        using var consistent = SeededModels.Create<ConsistentContext>(static options => new(options));
        Assert.Empty(PersistenceModelRules.ForeignKeysAcrossSchemas(consistent.Model));

        using var violating = SeededModels.Create<CrossSchemaKeyContext>(static options => new(options));
        Assert.Equal(["SeedRow (alpha) -> SeedOther (beta)"], PersistenceModelRules.ForeignKeysAcrossSchemas(violating.Model));
    }
}

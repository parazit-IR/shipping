using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Shipping.ArchitectureTests.Persistence;

/// <summary>
/// The rules a persistence model must hold, read from the Entity Framework model, which is what the
/// migrations create. The schema and foreign-key rules follow MP Core's generated <c>ModuleRulesTests</c>;
/// here they are applied to each module's own runtime context (ADR-002).
/// </summary>
internal static partial class PersistenceModelRules
{
    /// <summary>The module an entity belongs to, from its assembly: <c>Shipping.Modules.&lt;Module&gt;.*</c>.</summary>
    public static string? ModuleOf(Type type)
    {
        var match = ModuleAssembly().Match(type.Assembly.GetName().Name ?? string.Empty);
        return match.Success ? match.Groups["module"].Value : null;
    }

    /// <summary>
    /// Each module maps every table into one schema, named after the module in lower case, which no other
    /// module and not the host uses.
    /// </summary>
    public static List<string> SchemaOwnership(IModel model, Func<Type, string?> moduleOf)
    {
        var failures = new List<string>();
        var tables = model.GetEntityTypes().Where(static entity => entity.GetTableName() is not null).ToList();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in tables.Where(entity => moduleOf(entity.ClrType) is null))
        {
            owners.TryAdd(SchemaOf(entity), "the host");
        }

        var modules = tables.GroupBy(entity => moduleOf(entity.ClrType)).Where(static group => group.Key is not null).ToList();
        // Each module owns the schema named after it before any table is looked at, so the verdict does not
        // depend on the order in which the model lists its entities.
        foreach (var module in modules)
        {
            owners.TryAdd(module.Key!.ToLowerInvariant(), module.Key!);
        }

        foreach (var module in modules)
        {
            var expected = module.Key!.ToLowerInvariant();
            var schemas = module.Select(SchemaOf).Distinct(StringComparer.Ordinal).ToList();
            if (schemas.Count > 1)
            {
                failures.Add($"{module.Key} maps to {string.Join(" and ", schemas)}");
            }

            foreach (var schema in schemas)
            {
                if (!string.Equals(schema, expected, StringComparison.Ordinal))
                {
                    failures.Add($"{module.Key} maps into {schema}, not its own schema {expected}");
                }

                if (!owners.TryAdd(schema, module.Key!) && owners[schema] != module.Key)
                {
                    failures.Add($"{module.Key} maps into {schema}, the schema of {owners[schema]}");
                }
            }
        }

        return failures;
    }

    /// <summary>A foreign key across schemas ties two modules' tables together where no project reference shows it.</summary>
    public static List<string> ForeignKeysAcrossSchemas(IModel model) =>
        model.GetEntityTypes()
            .SelectMany(static entity => entity.GetForeignKeys())
            .Where(static key => !string.Equals(SchemaOf(key.DeclaringEntityType), SchemaOf(key.PrincipalEntityType), StringComparison.Ordinal))
            .Select(static key => $"{key.DeclaringEntityType.ClrType.Name} ({SchemaOf(key.DeclaringEntityType)}) -> {key.PrincipalEntityType.ClrType.Name} ({SchemaOf(key.PrincipalEntityType)})")
            .ToList();

    private static string SchemaOf(IReadOnlyEntityType entity) => entity.GetSchema() ?? entity.Model.GetDefaultSchema() ?? "public";

    [GeneratedRegex(@"^Shipping\.Modules\.(?<module>[^.]+)\.")]
    private static partial Regex ModuleAssembly();
}

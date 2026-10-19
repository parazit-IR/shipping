using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MPCore.Domain.Events;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;

namespace Shipping.Tests.Shared;

/// <summary>
/// Finds the persistence of every module the host ships, by the convention of ADR-002: a module's
/// Infrastructure assembly holds exactly one runtime context named <c>&lt;Module&gt;DbContext</c>, an MP Core
/// <see cref="MPCoreDbContext"/> whose schema is the module name in lower case, and the module's Application
/// assembly holds its unit-of-work port <c>I&lt;Module&gt;UnitOfWork</c>. Found by reflection, so a new module
/// is checked the day it is referenced, with no list to keep up to date.
/// </summary>
internal static partial class ModulePersistence
{
    /// <summary>A connection string that is never opened; enough to build a model.</summary>
    public const string ModelOnlyConnection = "Host=model-only.invalid;Database=model_only";

    public static IReadOnlyList<ModuleInfo> Modules { get; } = DiscoverModules();

    /// <summary>Builds the module's context outside any container, the way a migration bundle does.</summary>
    public static DbContext CreateModuleContext(ModuleInfo module, string connectionString) =>
        (DbContext)Activator.CreateInstance(
            module.Context, Options(module.Context, connectionString), TimeProvider.System, NullAggregateEventSink.Instance)!;

    /// <summary>Builds options the way MP Core does, with nothing module-specific: the context must add that itself.</summary>
    private static DbContextOptions Options(Type contextType, string connectionString)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        PostgreSqlDbContextOptions.Apply(builder, connectionString);
        return builder.Options;
    }

    private static List<ModuleInfo> DiscoverModules() =>
        Directory.GetFiles(AppContext.BaseDirectory, "Shipping.Modules.*.Infrastructure.dll")
            .Select(static path => Assembly.Load(Path.GetFileNameWithoutExtension(path)))
            .Select(static assembly => new ModuleInfo(
                InfrastructureName().Match(assembly.GetName().Name!).Groups["module"].Value,
                assembly,
                assembly.GetTypes().Where(static type => !type.IsAbstract && typeof(DbContext).IsAssignableFrom(type)).ToArray()))
            .OrderBy(static module => module.Name, StringComparer.Ordinal)
            .ToList();

    [GeneratedRegex(@"^Shipping\.Modules\.(?<module>[^.]+)\.Infrastructure$")]
    private static partial Regex InfrastructureName();
}

internal sealed record ModuleInfo(string Name, Assembly Infrastructure, IReadOnlyList<Type> Contexts)
{
    public string Schema => Name.ToLowerInvariant();

    public Assembly Application => Assembly.Load($"Shipping.Modules.{Name}.Application");

    public Assembly Api => Assembly.Load($"Shipping.Modules.{Name}.Api");

    public Type Context => Contexts.Count == 1
        ? Contexts[0]
        : throw new InvalidOperationException(
            $"Module {Name} must own exactly one DbContext in {Infrastructure.GetName().Name}; found {Contexts.Count}.");

    /// <summary>The module's unit-of-work port, <c>I&lt;Module&gt;UnitOfWork</c>, declared by its Application project.</summary>
    public Type UnitOfWork
    {
        get
        {
            var ports = Application.GetTypes().Where(type => type.IsInterface && type.Name == $"I{Name}UnitOfWork").ToArray();
            return ports.Length == 1
                ? ports[0]
                : throw new InvalidOperationException(
                    $"Module {Name} must declare exactly one I{Name}UnitOfWork in {Application.GetName().Name}; found {ports.Length}.");
        }
    }

    public override string ToString() => Name;
}

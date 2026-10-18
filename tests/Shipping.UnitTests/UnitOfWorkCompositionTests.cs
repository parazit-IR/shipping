using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Domain.Events;
using MPCore.Persistence.Abstractions;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Shipping.Modules.Sample.Api;
using Shipping.Modules.Sample.Application.Ports;
using Shipping.Tests.Shared;

namespace Shipping.UnitTests;

/// <summary>
/// SLS-125 / ADR-002: each module's runtime context and unit-of-work port come from the host's container; there
/// is no shared context and no global <see cref="IUnitOfWork"/> silently bound to one module.
/// </summary>
public class UnitOfWorkCompositionTests
{
    [Fact]
    public void The_host_resolves_each_modules_own_context_and_unit_of_work_as_one_scoped_instance()
    {
        using var factory = HostWithDatabase();
        using var scope = factory.Services.CreateScope();

        Assert.NotEmpty(ModulePersistence.Modules);
        var contexts = new List<object>();
        foreach (var module in ModulePersistence.Modules)
        {
            var context = scope.ServiceProvider.GetRequiredService(module.Context);
            var unitOfWork = scope.ServiceProvider.GetRequiredService(module.UnitOfWork);

            Assert.IsAssignableFrom<MPCoreDbContext>(context);
            // The port and the context are one instance in a scope: what a handler saves is what it tracked.
            Assert.Same(context, unitOfWork);
            Assert.Same(context, scope.ServiceProvider.GetRequiredService(module.UnitOfWork));
            contexts.Add(context);
        }

        Assert.Equal(contexts.Count, contexts.Distinct().Count());
    }

    [Fact]
    public void The_host_binds_no_global_IUnitOfWork()
    {
        // Each module commits through its own port. A global IUnitOfWork would have to pick one module.
        using var factory = HostWithDatabase();
        using var scope = factory.Services.CreateScope();

        Assert.False(factory.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IUnitOfWork)));
        Assert.Null(scope.ServiceProvider.GetService<IUnitOfWork>());
    }

    [Fact]
    public void A_second_module_neither_takes_over_nor_is_shadowed_by_Sample_whatever_the_registration_order()
    {
        foreach (var sampleFirst in new[] { true, false })
        {
            var services = new ServiceCollection().AddSingleton(TimeProvider.System).AddMPCoreAggregateEvents();
            if (sampleFirst)
            {
                services.AddSampleModule(ModulePersistence.ModelOnlyConnection);
                AddSecondModule(services);
            }
            else
            {
                AddSecondModule(services);
                services.AddSampleModule(ModulePersistence.ModelOnlyConnection);
            }

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            using var scope = provider.CreateScope();

            var sample = scope.ServiceProvider.GetRequiredService<ISampleUnitOfWork>();
            var second = scope.ServiceProvider.GetRequiredService<ISecondModuleUnitOfWork>();
            Assert.IsType<SecondModuleContext>(second);
            Assert.IsAssignableFrom<MPCoreDbContext>(sample);
            Assert.NotSame(sample, second);
            Assert.Equal("sample", ((DbContext)sample).Model.GetDefaultSchema());
            Assert.Null(scope.ServiceProvider.GetService<IUnitOfWork>());
        }
    }

    [Fact]
    public void The_host_refuses_to_start_without_a_database_connection()
    {
        // An explicit empty value, so a developer's user secret cannot make the test pass by accident.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(static host => host.UseSetting("ConnectionStrings:PostgreSql", string.Empty));

        var failure = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("ConnectionStrings:PostgreSql", Flatten(failure), StringComparison.Ordinal);
    }

    /// <summary>
    /// Why no module registers through <c>AddMPCorePostgreSql</c> (ADR-002): MP Core 0.10.1 binds
    /// <see cref="IUnitOfWork"/> with TryAdd, so a second <see cref="MPCoreDbContext"/> registered that way is
    /// silently not the unit of work, and a port resolved as <see cref="IUnitOfWork"/> would save the other
    /// module's context. This pins the framework behaviour; if MP Core changes it, ADR-002 must be revisited.
    /// </summary>
    [Fact]
    public void MP_Core_binds_IUnitOfWork_to_the_first_context_registered_and_ignores_a_second()
    {
        var services = new ServiceCollection().AddSingleton(TimeProvider.System);
        services.AddMPCorePostgreSql<FirstModuleContext>(ModulePersistence.ModelOnlyConnection);
        services.AddMPCorePostgreSql<SecondModuleContext>(ModulePersistence.ModelOnlyConnection);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<FirstModuleContext>(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
    }

    /// <summary>A second module, registered the way a Shipping module registers its context (ADR-002).</summary>
    private static void AddSecondModule(IServiceCollection services)
    {
        services.AddDbContext<SecondModuleContext>(static options =>
            PostgreSqlDbContextOptions.Apply(options, ModulePersistence.ModelOnlyConnection));
        services.AddScoped<ISecondModuleUnitOfWork>(static provider => provider.GetRequiredService<SecondModuleContext>());
    }

    private static WebApplicationFactory<Program> HostWithDatabase() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(static host =>
            host.UseSetting("ConnectionStrings:PostgreSql", ModulePersistence.ModelOnlyConnection));

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + " | " + Flatten(exception.InnerException);

    public interface ISecondModuleUnitOfWork : IUnitOfWork;

    public sealed class FirstModuleContext(DbContextOptions<FirstModuleContext> options, TimeProvider time) : MPCoreDbContext(options, time);

    public sealed class SecondModuleContext(DbContextOptions<SecondModuleContext> options, TimeProvider time, IAggregateEventSink events)
        : MPCoreDbContext(options, time, events), ISecondModuleUnitOfWork
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("second");
    }
}

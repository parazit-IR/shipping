using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Persistence.Abstractions;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Shipping.Modules.Sample.Api;
using Shipping.Modules.Sample.Application.Ports;

namespace Shipping.UnitTests;

public class ModuleRegistrationTests
{
    [Fact]
    public void Sample_module_registers_its_own_context_and_unit_of_work_and_nothing_global()
    {
        IServiceCollection services = new ServiceCollection();
        Assert.Same(services, services.AddSampleModule("Host=model-only.invalid;Database=model_only"));

        Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ISampleUnitOfWork));
        Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
        // The time port and the event sink belong to the host, which registers them once for every module.
        Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(TimeProvider));

        services.AddSingleton(TimeProvider.System).AddMPCoreAggregateEvents();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsAssignableFrom<MPCoreDbContext>(scope.ServiceProvider.GetRequiredService<ISampleUnitOfWork>());
    }

    [Fact]
    public void Sample_module_requires_a_connection_string()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddSampleModule(" "));
    }

    [Fact]
    public void Sample_endpoint_mapping_can_be_invoked()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        Assert.Same(app, app.MapSampleModule());
    }
}

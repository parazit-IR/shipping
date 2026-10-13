using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Modules.Sample.Api;

namespace Shipping.UnitTests;

public class ModuleRegistrationTests
{
    [Fact]
    public void Sample_module_intentionally_registers_no_services()
    {
        IServiceCollection services = new ServiceCollection();
        Assert.Empty(services);
        Assert.Same(services, services.AddSampleModule());
        Assert.Empty(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider);
    }

    [Fact]
    public void Sample_endpoint_mapping_can_be_invoked()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();
        Assert.Same(app, app.MapSampleModule());
    }
}

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Modules.Sample.Infrastructure;

namespace Shipping.Modules.Sample.Api;

public static class ModuleRegistration
{
    /// <summary>Registers the module, with its own database context in its own schema of the given database.</summary>
    public static IServiceCollection AddSampleModule(this IServiceCollection services, string connectionString) =>
        services.AddSampleInfrastructure(connectionString);

    public static IEndpointRouteBuilder MapSampleModule(this IEndpointRouteBuilder endpoints) => endpoints;
}

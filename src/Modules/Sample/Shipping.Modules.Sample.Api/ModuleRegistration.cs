using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Modules.Sample.Infrastructure;

namespace Shipping.Modules.Sample.Api;

public static class ModuleRegistration
{
    public static IServiceCollection AddSampleModule(this IServiceCollection services) => services.AddSampleInfrastructure();

    public static IEndpointRouteBuilder MapSampleModule(this IEndpointRouteBuilder endpoints) => endpoints;
}

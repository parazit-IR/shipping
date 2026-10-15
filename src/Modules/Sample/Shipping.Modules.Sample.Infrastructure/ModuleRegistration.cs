using Microsoft.Extensions.DependencyInjection;

namespace Shipping.Modules.Sample.Infrastructure;

public static class ModuleRegistration
{
    public static IServiceCollection AddSampleInfrastructure(this IServiceCollection services) => services;
}

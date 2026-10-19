using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shipping.Tests.Shared;

namespace Shipping.UnitTests;

/// <summary>
/// SLOS-127 (US-EN-1 T4): how the real host exposes GET /health, without a database. What the endpoint answers
/// against a real PostgreSQL is in Shipping.IntegrationTests.
/// </summary>
public class HealthEndpointCompositionTests
{
    [Fact]
    public async Task Health_is_mapped_on_the_real_host_for_GET_only_and_open_to_callers_without_a_token()
    {
        await using var factory = ModelOnlyHost();
        _ = factory.Server;

        var endpoint = Assert.Single(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>(),
            static endpoint => endpoint.RoutePattern.RawText == "/health");

        Assert.Equal(["GET"], endpoint.Metadata.GetRequiredMetadata<IHttpMethodMetadata>().HttpMethods);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Each_module_registers_one_bounded_check_of_its_own_database_and_the_host_registers_none()
    {
        await using var factory = ModelOnlyHost();

        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.NotEmpty(ModulePersistence.Modules);
        Assert.Equal(
            ModulePersistence.Modules.Select(static module => $"{module.Schema}-database").Order(StringComparer.Ordinal),
            registrations.Select(static registration => registration.Name).Order(StringComparer.Ordinal));
        Assert.All(registrations, static registration =>
        {
            Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
            Assert.InRange(registration.Timeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(3));
        });
    }

    [Fact]
    public async Task A_POST_to_health_is_refused()
    {
        await using var factory = ModelOnlyHost();
        using var client = factory.CreateClient();

        using var post = await client.PostAsync("/health", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    /// <summary>The real host with a connection string that is never opened: these tests do not run the checks.</summary>
    private static WebApplicationFactory<Program> ModelOnlyHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(static host =>
            host.UseSetting("ConnectionStrings:PostgreSql", ModulePersistence.ModelOnlyConnection));
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Shipping.UnitTests;

public class HostCompositionTests
{
    [Fact]
    public async Task Real_host_builds_and_serves_requests_with_its_configured_services()
    {
        // The host requires a connection string (SLS-125); it is never opened by this test.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(static host =>
            host.UseSetting("ConnectionStrings:PostgreSql", "Host=model-only.invalid;Database=model_only"));
        using var client = factory.CreateClient();
        Assert.NotNull(factory.Services.GetRequiredService<IServiceScopeFactory>());

        using var response = await client.GetAsync("/t1-unmapped-route");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}

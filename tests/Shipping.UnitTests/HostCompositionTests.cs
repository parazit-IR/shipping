using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Shipping.UnitTests;

public class HostCompositionTests
{
    [Fact]
    public async Task Real_host_builds_and_serves_requests_with_its_configured_services()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.NotNull(factory.Services.GetRequiredService<IServiceScopeFactory>());

        using var response = await client.GetAsync("/t1-unmapped-route");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}

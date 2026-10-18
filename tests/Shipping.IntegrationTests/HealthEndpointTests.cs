using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shipping.Tests.Shared;

namespace Shipping.IntegrationTests;

/// <summary>
/// SLOS-127 (US-EN-1 T4) against a real PostgreSQL: GET /health on the real host answers 200 when every module's
/// context reaches the database and 503 when one does not, says nothing beyond the status word, needs no token, and
/// never migrates or creates anything.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class HealthEndpointTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task Health_returns_200_Healthy_without_a_token_when_every_module_reaches_the_database()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        await using var host = Host(database);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "a probe answer must never be served from a cache");
    }

    [Fact]
    public async Task Health_neither_migrates_nor_creates_anything_in_the_database()
    {
        var database = await postgres.CreateDatabaseAsync();
        var before = await Sql.DescribeAsync(database);
        await using var host = Host(database);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await Sql.DescribeAsync(database));
        foreach (var module in ModulePersistence.Modules)
        {
            Assert.DoesNotContain(module.Schema, await Sql.SchemasAsync(database));
        }
    }

    [Fact]
    public async Task Health_returns_503_with_no_detail_while_the_database_refuses_connections_and_200_once_it_accepts_them()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        await using var host = Host(database);
        using var client = host.CreateClient();

        await postgres.AllowConnectionsAsync(database, allow: false);
        try
        {
            using var unavailable = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            // The status word only: no check name, host, database name or exception text.
            Assert.Equal("Unhealthy", await unavailable.Content.ReadAsStringAsync());
        }
        finally
        {
            await postgres.AllowConnectionsAsync(database, allow: true);
        }

        using var recovered = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal("Healthy", await recovered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_returns_503_within_its_timeout_when_opening_a_connection_ignores_cancellation()
    {
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        // Opening a connection to the stopped postgres container took 10-12 s in Compose and did not stop when the
        // check's token was cancelled. This covers the time bound only; the disposal failure that a cut-off attempt
        // on the request's context caused is covered by the next test and by the Compose smoke test.
        await using var host = Host(database).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var module in ModulePersistence.Modules)
            {
                ConfigureDbContext.MakeGenericMethod(module.Context).Invoke(null,
                    [services, (Action<DbContextOptionsBuilder>)(options => options.AddInterceptors(new StalledConnectionOpening())), ServiceLifetime.Scoped]);
            }
        }));
        using var client = host.CreateClient();

        var elapsed = Stopwatch.StartNew();
        using var response = await client.GetAsync("/health");
        elapsed.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
        // The check's timeout is 2 s; 6 s leaves room for a cold first request and stays well short of the 10 s stall.
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task Health_never_uses_the_request_scoped_module_context()
    {
        // A cut-off attempt on the request's context made scope disposal throw while it was still connecting: a 500
        // with the exception page instead of 503. The check must run on a context of its own.
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        await using var host = Host(database).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var module in ModulePersistence.Modules)
            {
                services.AddScoped(module.Context, _ => throw new InvalidOperationException("GET /health resolved the request-scoped context."));
            }
        }));
        using var client = host.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Concurrent_requests_share_one_connection_attempt_while_it_is_still_running()
    {
        // A timed-out attempt keeps running and holds a slot of the connection pool business traffic uses. Each
        // request must not start another: at most one attempt per module, whatever the request rate.
        var database = await postgres.CreateDatabaseAsync();
        await Sql.MigrateModulesAsync(database);
        var opening = new StalledConnectionOpening();
        await using var host = Host(database).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            foreach (var module in ModulePersistence.Modules)
            {
                ConfigureDbContext.MakeGenericMethod(module.Context).Invoke(null,
                    [services, (Action<DbContextOptionsBuilder>)(options => options.AddInterceptors(opening)), ServiceLifetime.Scoped]);
            }
        }));
        using var client = host.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync("/health")));

        Assert.All(responses, static response => Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode));
        Assert.Equal(ModulePersistence.Modules.Count, opening.Openings);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private static readonly MethodInfo ConfigureDbContext = typeof(EntityFrameworkServiceCollectionExtensions).GetMethods()
        .Single(static method => method.Name == nameof(EntityFrameworkServiceCollectionExtensions.ConfigureDbContext) &&
                                 method.GetParameters()[1].ParameterType == typeof(Action<DbContextOptionsBuilder>));

    private static WebApplicationFactory<Program> Host(string database) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.UseSetting("ConnectionStrings:PostgreSql", database));

    /// <summary>Opens a connection only after 10 s, whatever the caller's token says, and counts the openings.</summary>
    private sealed class StalledConnectionOpening : DbConnectionInterceptor
    {
        private int _openings;

        public int Openings => Volatile.Read(ref _openings);

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _openings);
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
            return result;
        }
    }
}

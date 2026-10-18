using Npgsql;
using Testcontainers.PostgreSql;

namespace Shipping.IntegrationTests;

/// <summary>One PostgreSQL container for the run; every test gets a database of its own in it.</summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    /// <summary>Pinned, so a run never meets a PostgreSQL version nobody chose.</summary>
    public const string Image = "postgres:17.6-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "sls125_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        // No pooling: each test's database is used by that test only, and a pooled connection would outlive it.
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
    }

    /// <summary>
    /// Makes the server refuse, or accept again, every new connection to one test database: an unavailable
    /// database for that test only, while the container keeps serving the others.
    /// </summary>
    public async Task AllowConnectionsAsync(string database, bool allow)
    {
        var name = new NpgsqlConnectionStringBuilder(database).Database;
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var alter = new NpgsqlCommand($"ALTER DATABASE \"{name}\" WITH ALLOW_CONNECTIONS {(allow ? "true" : "false")}", connection);
        await alter.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}

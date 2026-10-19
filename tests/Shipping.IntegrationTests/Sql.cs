using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shipping.Tests.Shared;

namespace Shipping.IntegrationTests;

/// <summary>Reads the database directly, never through a context under test.</summary>
internal static class Sql
{
    /// <summary>Applies every shipped module's migrations, each with its own context, as a deployment does.</summary>
    public static async Task MigrateModulesAsync(string database)
    {
        foreach (var module in ModulePersistence.Modules)
        {
            await using var context = ModulePersistence.CreateModuleContext(module, database);
            await context.Database.MigrateAsync();
        }
    }

    public static async Task MigrateProbesAsync(string database)
    {
        await using (var alpha = AlphaProbeDbContext.Create(database))
        {
            await alpha.Database.MigrateAsync();
        }

        await using var beta = BetaProbeDbContext.Create(database);
        await beta.Database.MigrateAsync();
    }

    public static Task<List<string>> SchemasAsync(string database) =>
        ListAsync(database, "SELECT nspname FROM pg_namespace ORDER BY 1");

    public static Task<List<string>> HistoryAsync(string database, string schema) =>
        ListAsync(database, $"SELECT \"MigrationId\" FROM \"{schema}\".\"{HistoryRepository.DefaultTableName}\" ORDER BY 1");

    public static async Task<bool> TableExistsAsync(string database, string schema, string table) =>
        (await ListAsync(database, $"SELECT 1 FROM information_schema.tables WHERE table_schema = '{schema}' AND table_name = '{table}'")).Count > 0;

    /// <summary>Every user schema, table and column with its type, plus every migration history row.</summary>
    public static async Task<List<string>> DescribeAsync(string database, params string[] exceptSchemas)
    {
        var described = await ListAsync(database,
            "SELECT n.nspname || COALESCE('.' || c.table_name || '.' || c.column_name || ' ' || c.data_type, '') " +
            "FROM pg_namespace n LEFT JOIN information_schema.columns c ON c.table_schema = n.nspname " +
            "WHERE n.nspname NOT LIKE 'pg\\_%' AND n.nspname <> 'information_schema' ORDER BY 1");
        foreach (var schema in await ListAsync(database,
                     $"SELECT table_schema FROM information_schema.tables WHERE table_name = '{HistoryRepository.DefaultTableName}' ORDER BY 1"))
        {
            described.AddRange((await HistoryAsync(database, schema)).Select(migration => $"{schema} history {migration}"));
        }

        return described
            .Where(entry => !exceptSchemas.Any(schema =>
                entry == schema || entry.StartsWith(schema + ".", StringComparison.Ordinal) || entry.StartsWith(schema + " ", StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>The identifiers in a probe schema's table, read with plain SQL.</summary>
    public static Task<List<string>> ProbeIdsAsync(string database, string schema) =>
        ListAsync(database, $"SELECT \"Id\"::text FROM \"{schema}\".\"{ProbeDbContext.Table}\" ORDER BY 1");

    public static async Task<List<string>> ListAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetValue(0).ToString()!);
        }

        return values;
    }
}

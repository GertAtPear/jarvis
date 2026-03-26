// CREDENTIAL SAFETY: Never log SshCredentials, WinRmCredentials, SqlCredentials, or any password/key values.
// Log only: hostname, operation name, duration, success/failure.

using System.Diagnostics;
using Dapper;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace Mediahost.Tools.Sql;

public sealed class SqlTool(ILogger<SqlTool> logger) : ISqlTool
{
    public async Task<ToolResult> TestConnectionAsync(
        SqlCredentials credentials,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = OpenConnection(credentials);
            await conn.OpenAsync(ct);
            sw.Stop();
            logger.LogDebug("SQL connection test to {Host}:{Port}/{Db} succeeded in {Ms}ms",
                credentials.Host, credentials.Port, credentials.Database, sw.ElapsedMilliseconds);
            return ToolResult.Ok(sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("SQL connection test to {Host}:{Port}/{Db} failed in {Ms}ms: {Message}",
                credentials.Host, credentials.Port, credentials.Database, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<ToolResult<IReadOnlyList<IDictionary<string, object?>>>> QueryAsync(
        SqlCredentials credentials,
        string sql,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = OpenConnection(credentials);
            await conn.OpenAsync(ct);

            var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
            var rows = await conn.QueryAsync(command);

            var result = rows
                .Select(row => (IDictionary<string, object?>)
                    ((IDictionary<string, object>)row)
                    .ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                .ToList();

            sw.Stop();
            logger.LogDebug("SQL query on {Host}:{Port}/{Db} returned {Count} rows in {Ms}ms",
                credentials.Host, credentials.Port, credentials.Database, result.Count, sw.ElapsedMilliseconds);
            return ToolResult<IReadOnlyList<IDictionary<string, object?>>>.Ok(result, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("SQL query on {Host}:{Port}/{Db} failed in {Ms}ms: {Message}",
                credentials.Host, credentials.Port, credentials.Database, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult<IReadOnlyList<IDictionary<string, object?>>>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<ToolResult<int>> ExecuteAsync(
        SqlCredentials credentials,
        string sql,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = OpenConnection(credentials);
            await conn.OpenAsync(ct);

            var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
            var affected = await conn.ExecuteAsync(command);
            sw.Stop();

            logger.LogDebug("SQL execute on {Host}:{Port}/{Db} affected {Rows} rows in {Ms}ms",
                credentials.Host, credentials.Port, credentials.Database, affected, sw.ElapsedMilliseconds);
            return ToolResult<int>.Ok(affected, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("SQL execute on {Host}:{Port}/{Db} failed in {Ms}ms: {Message}",
                credentials.Host, credentials.Port, credentials.Database, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult<int>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private static System.Data.Common.DbConnection OpenConnection(SqlCredentials credentials) =>
        credentials.Type switch
        {
            DatabaseType.PostgreSQL => new NpgsqlConnection(credentials.BuildConnectionString()),
            DatabaseType.MySQL or DatabaseType.MariaDB => new MySqlConnection(credentials.BuildConnectionString()),
            _ => throw new NotSupportedException($"DatabaseType {credentials.Type} is not supported")
        };
}

using System.Text.Json;
using Dapper;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;
using MySqlConnector;
using Npgsql;
using Sam.Agent.Data.Repositories;
using Sam.Agent.Services;

namespace Sam.Agent.Tools;

public class SamToolExecutor(
    DatabaseRepository databaseRepo,
    ConnectionStatsRepository connRepo,
    TableStatsRepository tableRepo,
    SlowQueryRepository slowRepo,
    ReplicationRepository replRepo,
    DiscoveryLogRepository logRepo,
    MySqlScanService mySqlScan,
    PostgreSqlScanService pgScan,
    IVaultService vault,
    ILogger<SamToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => SamToolDefinitions.All;

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "list_databases"         => await ListDatabasesAsync(),
                "get_database_health"    => await GetDatabaseHealthAsync(input),
                "get_slow_queries"       => await GetSlowQueriesAsync(input),
                "get_table_stats"        => await GetTableStatsAsync(input),
                "get_connection_stats"   => await GetConnectionStatsAsync(input),
                "get_replication_status" => await GetReplicationStatusAsync(input),
                "run_safe_query"         => await RunSafeQueryAsync(input, ct),
                "explain_query"          => await ExplainQueryAsync(input, ct),
                "trigger_scan"           => await TriggerScanAsync(input, ct),
                "get_discovery_log"      => await GetDiscoveryLogAsync(input),
                _ => $"{{\"error\": \"Unknown tool: {toolName}\"}}"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Sam] Tool {Tool} failed", toolName);
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\"}}";
        }
    }

    private async Task<string> ListDatabasesAsync()
    {
        var dbs = await databaseRepo.GetAllAsync();
        return JsonSerializer.Serialize(dbs);
    }

    private async Task<string> GetDatabaseHealthAsync(JsonDocument input)
    {
        var name = GetString(input, "name");
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";
        var conns = await connRepo.GetLatestAsync(db.Id);
        var repl  = await replRepo.GetLatestAsync(db.Id);
        var slow  = await slowRepo.GetRecentAsync(db.Id, 5);
        return JsonSerializer.Serialize(new { database = db, connections = conns, replication = repl, top_slow_queries = slow });
    }

    private async Task<string> GetSlowQueriesAsync(JsonDocument input)
    {
        var name  = GetString(input, "name");
        var limit = input.RootElement.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";
        var result = await slowRepo.GetRecentAsync(db.Id, limit);
        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetTableStatsAsync(JsonDocument input)
    {
        var name  = GetString(input, "name");
        var limit = input.RootElement.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";
        var result = await tableRepo.GetTopBySizeAsync(db.Id, limit);
        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetConnectionStatsAsync(JsonDocument input)
    {
        var name = GetString(input, "name");
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";
        var result = await connRepo.GetLatestAsync(db.Id);
        return JsonSerializer.Serialize(result);
    }

    private async Task<string> GetReplicationStatusAsync(JsonDocument input)
    {
        var name = GetString(input, "name");
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";
        var result = await replRepo.GetLatestAsync(db.Id);
        return JsonSerializer.Serialize(result);
    }

    private async Task<string> RunSafeQueryAsync(JsonDocument input, CancellationToken ct)
    {
        var dbName = GetString(input, "database_name");
        var query  = GetString(input, "query");

        // Safety: strip comments, validate SELECT-only, inject LIMIT
        var safe = StripComments(query).Trim();
        if (!safe.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "{\"error\": \"Only SELECT statements are allowed\"}";
        if (ContainsDml(safe))
            return "{\"error\": \"Query contains disallowed DML keywords\"}";
        if (!safe.Contains("LIMIT ", StringComparison.OrdinalIgnoreCase))
            safe += " LIMIT 1000";

        var db = await databaseRepo.GetByNameAsync(dbName);
        if (db is null) return $"{{\"error\": \"Database '{dbName}' not found\"}}";

        Dictionary<string, string>? secret = null;
        if (db.VaultSecretPath is not null)
            secret = await vault.GetSecretsBulkAsync(db.VaultSecretPath, ct);

        try
        {
            if (db.DbType.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            {
                var user = secret?.TryGetValue("username", out var u) == true ? u : "postgres";
                var pass = secret?.TryGetValue("password", out var p) == true ? p : "";
                var connStr = $"Host={db.Host};Port={db.Port};Database={db.DbName};Username={user};Password={pass};CommandTimeout=30";
                await using var ds = NpgsqlDataSource.Create(connStr);
                await using var conn = await ds.OpenConnectionAsync(ct);
                var rows = await conn.QueryAsync(safe);
                return JsonSerializer.Serialize(rows);
            }
            else
            {
                var user = secret?.TryGetValue("username", out var u) == true ? u : "root";
                var pass = secret?.TryGetValue("password", out var p) == true ? p : "";
                var connStr = $"Server={db.Host};Port={db.Port};Database={db.DbName};User={user};Password={pass};DefaultCommandTimeout=30";
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync(ct);
                var rows = await conn.QueryAsync(safe);
                return JsonSerializer.Serialize(rows);
            }
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\"}}";
        }
    }

    private async Task<string> ExplainQueryAsync(JsonDocument input, CancellationToken ct)
    {
        var dbName = GetString(input, "database_name");
        var query  = GetString(input, "query");

        // Safety: never run EXPLAIN ANALYZE
        var safe = System.Text.RegularExpressions.Regex.Replace(query, @"\bANALYZE\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        var db = await databaseRepo.GetByNameAsync(dbName);
        if (db is null) return $"{{\"error\": \"Database '{dbName}' not found\"}}";

        Dictionary<string, string>? secret = null;
        if (db.VaultSecretPath is not null)
            secret = await vault.GetSecretsBulkAsync(db.VaultSecretPath, ct);

        try
        {
            if (db.DbType.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            {
                var user = secret?.TryGetValue("username", out var u) == true ? u : "postgres";
                var pass = secret?.TryGetValue("password", out var p) == true ? p : "";
                var connStr = $"Host={db.Host};Port={db.Port};Database={db.DbName};Username={user};Password={pass};CommandTimeout=30";
                await using var ds = NpgsqlDataSource.Create(connStr);
                await using var conn = await ds.OpenConnectionAsync(ct);
                var plan = await conn.QueryAsync($"EXPLAIN (FORMAT JSON) {safe}");
                return JsonSerializer.Serialize(plan);
            }
            else
            {
                var user = secret?.TryGetValue("username", out var u) == true ? u : "root";
                var pass = secret?.TryGetValue("password", out var p) == true ? p : "";
                var connStr = $"Server={db.Host};Port={db.Port};Database={db.DbName};User={user};Password={pass};DefaultCommandTimeout=30";
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync(ct);
                var plan = await conn.QueryAsync($"EXPLAIN {safe}");
                return JsonSerializer.Serialize(plan);
            }
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message.Replace("\"", "'")}\"}}";
        }
    }

    private async Task<string> TriggerScanAsync(JsonDocument input, CancellationToken ct)
    {
        var name = GetString(input, "name");
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";

        if (db.DbType.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            await pgScan.ScanAsync(db, ct);
        else
            await mySqlScan.ScanAsync(db, ct);

        return $"{{\"status\": \"scan triggered\", \"database\": \"{name}\"}}";
    }

    private async Task<string> GetDiscoveryLogAsync(JsonDocument input)
    {
        // Not yet implemented — return placeholder
        var name = GetString(input, "name");
        var db = await databaseRepo.GetByNameAsync(name);
        if (db is null) return $"{{\"error\": \"Database '{name}' not found\"}}";
        return JsonSerializer.Serialize(new { message = "Discovery log retrieval not yet wired", database = name });
    }

    private static string GetString(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static string StripComments(string sql)
    {
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"--[^\r\n]*", "");
        sql = System.Text.RegularExpressions.Regex.Replace(sql, @"/\*[\s\S]*?\*/", "");
        return sql;
    }

    private static bool ContainsDml(string sql)
    {
        var upper = sql.ToUpperInvariant();
        return upper.Contains("INSERT ") || upper.Contains("UPDATE ") ||
               upper.Contains("DELETE ") || upper.Contains("DROP ")   ||
               upper.Contains("TRUNCATE ") || upper.Contains("ALTER ");
    }
}

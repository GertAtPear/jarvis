using System.Text.Json;
using Dapper;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;
using MySqlConnector;
using Npgsql;
using Research.Agent.Data;
using Research.Agent.Data.Repositories;

namespace Research.Agent.Tools;

public class ResearchToolExecutor(
    ResearchDatabaseRepository databaseRepo,
    ResearchMemoryService memory,
    IVaultService vault,
    ILogger<ResearchToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => ResearchToolDefinitions.All;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "list_databases"   => await ListDatabasesAsync(ct),
                "sql_query"        => await SqlQueryAsync(input, ct),
                "remember_finding" => await RememberFindingAsync(input, ct),
                "forget_finding"   => await ForgetFindingAsync(input, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Research] Tool {Tool} failed", toolName);
            return Err(ex.Message);
        }
    }

    // ── Tools ─────────────────────────────────────────────────────────────────

    private async Task<string> ListDatabasesAsync(CancellationToken ct)
    {
        var dbs = await databaseRepo.GetAllActiveAsync(ct);
        var result = dbs.Select(d => new
        {
            name         = d.Name,
            display_name = d.DisplayName ?? d.Name,
            db_type      = d.DbType,
            host         = d.Host,
            db_name      = d.DbName,
            description  = d.Description
        }).ToList();
        return Ok(new { count = result.Count, databases = result });
    }

    private async Task<string> SqlQueryAsync(JsonDocument input, CancellationToken ct)
    {
        var dbName = RequireString(input, "database_name");
        var query  = RequireString(input, "query");

        // Safety: SELECT-only, no DML, enforce LIMIT
        var safe = query.Trim();
        if (!safe.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !safe.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase))
            return Err("Only SELECT statements (and CTEs starting with WITH) are allowed.");

        if (ContainsDml(safe))
            return Err("Query contains disallowed data-modification keywords.");

        if (!safe.Contains("LIMIT ", StringComparison.OrdinalIgnoreCase))
            safe += " LIMIT 500";

        var db = await databaseRepo.GetByNameAsync(dbName, ct);
        if (db is null)
            return Err($"Database '{dbName}' not found. Call list_databases to see available databases.");

        Dictionary<string, string>? secret = null;
        if (db.VaultSecretPath is not null)
            secret = await vault.GetSecretsBulkAsync(db.VaultSecretPath, ct);

        try
        {
            IEnumerable<dynamic> rows;

            if (db.DbType.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            {
                var user    = secret?.TryGetValue("username", out var u) == true ? u : "postgres";
                var pass    = secret?.TryGetValue("password", out var p) == true ? p : "";
                var connStr = $"Host={db.Host};Port={db.Port};Database={db.DbName};Username={user};Password={pass};CommandTimeout=30";
                await using var ds   = NpgsqlDataSource.Create(connStr);
                await using var conn = await ds.OpenConnectionAsync(ct);
                rows = await conn.QueryAsync(safe);
            }
            else
            {
                var user    = secret?.TryGetValue("username", out var u) == true ? u : "root";
                var pass    = secret?.TryGetValue("password", out var p) == true ? p : "";
                var connStr = $"Server={db.Host};Port={db.Port};Database={db.DbName};User={user};Password={pass};DefaultCommandTimeout=30";
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync(ct);
                rows = await conn.QueryAsync(safe);
            }

            var list = rows.ToList();
            logger.LogDebug("[Research] sql_query on {Db} returned {Count} rows", dbName, list.Count);
            return JsonSerializer.Serialize(new { database = dbName, row_count = list.Count, rows = list }, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning("[Research] sql_query on {Db} failed: {Msg}", dbName, ex.Message);
            return Err($"Query failed: {ex.Message}");
        }
    }

    private async Task<string> RememberFindingAsync(JsonDocument input, CancellationToken ct)
    {
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");
        await memory.RememberFactAsync(key, value, ct);
        return Ok(new { remembered = true, key });
    }

    private async Task<string> ForgetFindingAsync(JsonDocument input, CancellationToken ct)
    {
        var key = RequireString(input, "key");
        await memory.ForgetFactAsync(key, ct);
        return Ok(new { forgotten = true, key });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool ContainsDml(string sql)
    {
        var upper = sql.ToUpperInvariant();
        string[] blocked = ["INSERT ", "UPDATE ", "DELETE ", "DROP ", "TRUNCATE ", "ALTER ", "CREATE ", "EXEC ", "EXECUTE "];
        return blocked.Any(upper.Contains);
    }

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string Ok(object value)  => JsonSerializer.Serialize(value, JsonOpts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, JsonOpts);
}

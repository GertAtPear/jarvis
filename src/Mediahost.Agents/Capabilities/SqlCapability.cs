using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Capabilities;

public enum SqlPermission
{
    ReadOnly,  // SELECT queries only (and WITH for CTEs)
    ReadWrite, // Any DML: SELECT, INSERT, UPDATE, DELETE
    DbaLevel   // Any SQL including DDL: CREATE, ALTER, DROP, TRUNCATE, etc.
}

/// <summary>
/// Capability wrapper for SQL. Enforces per-agent query scope before delegating to ISqlTool.
/// </summary>
public class SqlCapability(ISqlTool tool, ILogger<SqlCapability> logger)
{
    private static readonly string[] DdlKeywords =
        ["CREATE", "ALTER", "DROP", "TRUNCATE", "RENAME", "COMMENT"];

    private static readonly string[] BlockedAlways =
        ["EXEC ", "EXECUTE ", "CALL ", "XP_", "SP_"];

    public async Task<ToolResult<IReadOnlyList<IDictionary<string, object?>>>> QueryAsync(
        SqlCredentials credentials,
        string sql,
        SqlPermission permission = SqlPermission.ReadOnly,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var block = ValidateSql(sql, permission);
        if (block is not null)
            return ToolResult<IReadOnlyList<IDictionary<string, object?>>>.Fail(block);

        return await tool.QueryAsync(credentials, sql, parameters, ct);
    }

    public async Task<ToolResult<int>> ExecuteAsync(
        SqlCredentials credentials,
        string sql,
        SqlPermission permission = SqlPermission.ReadOnly,
        object? parameters = null,
        CancellationToken ct = default)
    {
        var block = ValidateSql(sql, permission);
        if (block is not null)
            return ToolResult<int>.Fail(block);

        return await tool.ExecuteAsync(credentials, sql, parameters, ct);
    }

    public Task<ToolResult> TestConnectionAsync(
        SqlCredentials credentials,
        CancellationToken ct = default)
        => tool.TestConnectionAsync(credentials, ct);

    // ── Validation ────────────────────────────────────────────────────────────

    private string? ValidateSql(string sql, SqlPermission permission)
    {
        var firstKeyword = GetFirstKeyword(sql);

        // Always blocked patterns regardless of permission
        foreach (var blocked in BlockedAlways)
        {
            if (sql.Contains(blocked, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("SQL blocked: always-blocked keyword '{Keyword}'", blocked.Trim());
                return $"Query blocked: '{blocked.Trim()}' is not permitted. " +
                       "Stored procedures, dynamic execution, and extended procedures are not allowed.";
            }
        }

        if (permission == SqlPermission.ReadOnly)
        {
            if (!string.Equals(firstKeyword, "SELECT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(firstKeyword, "WITH", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("SQL blocked under ReadOnly: first keyword '{Keyword}'", firstKeyword);
                return $"Query blocked: '{firstKeyword}' is not permitted under ReadOnly access. " +
                       "This agent may only run SELECT queries.";
            }
        }

        if (permission == SqlPermission.ReadWrite)
        {
            foreach (var ddl in DdlKeywords)
            {
                if (string.Equals(firstKeyword, ddl, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("SQL blocked under ReadWrite: DDL keyword '{Keyword}'", ddl);
                    return $"Query blocked: '{ddl}' is not permitted under ReadWrite access. " +
                           "This agent may only run SELECT, INSERT, UPDATE, and DELETE queries.";
                }
            }
        }

        return null; // DbaLevel: no restrictions
    }

    private static string GetFirstKeyword(string sql)
    {
        var trimmed = sql.AsSpan().TrimStart();
        var end = trimmed.IndexOfAny([' ', '\t', '\n', '\r', '(']);
        return end < 0
            ? trimmed.ToString().ToUpperInvariant()
            : trimmed[..end].ToString().ToUpperInvariant();
    }
}

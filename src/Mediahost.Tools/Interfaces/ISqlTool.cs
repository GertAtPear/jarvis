using Mediahost.Tools.Models;

namespace Mediahost.Tools.Interfaces;

public interface ISqlTool
{
    Task<ToolResult> TestConnectionAsync(
        SqlCredentials credentials,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a SQL query and returns rows as dictionaries.
    /// No read-only enforcement here — scope restrictions belong in agent capability wrappers.
    /// </summary>
    Task<ToolResult<IReadOnlyList<IDictionary<string, object?>>>> QueryAsync(
        SqlCredentials credentials,
        string sql,
        object? parameters = null,
        CancellationToken ct = default);

    Task<ToolResult<int>> ExecuteAsync(
        SqlCredentials credentials,
        string sql,
        object? parameters = null,
        CancellationToken ct = default);
}

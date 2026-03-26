namespace Mediahost.Tools.Models;

public record ToolResult(bool Success, string? ErrorMessage, long DurationMs)
{
    public static ToolResult Ok(long durationMs) =>
        new(true, null, durationMs);

    public static ToolResult Fail(string error, long durationMs = 0) =>
        new(false, error, durationMs);
}

public record ToolResult<T>(bool Success, string? ErrorMessage, long DurationMs, T? Value)
    : ToolResult(Success, ErrorMessage, DurationMs)
{
    public static ToolResult<T> Ok(T value, long durationMs) =>
        new(true, null, durationMs, value);

    public static new ToolResult<T> Fail(string error, long durationMs = 0) =>
        new(false, error, durationMs, default);
}

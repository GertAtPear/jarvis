namespace Andrew.Agent.Models;

public record CheckResult(
    Guid Id,
    Guid CheckId,
    string Status,       // ok | failed
    string? DetailsJson,
    int? DurationMs,
    DateTime CheckedAt
);

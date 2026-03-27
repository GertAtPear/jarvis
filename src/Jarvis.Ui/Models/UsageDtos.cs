namespace Jarvis.Ui.Models;

public record UsageSummaryDto(
    int     TotalRequests,
    int     TotalInputTokens,
    int     TotalOutputTokens,
    int     EscalationCount,
    decimal EstimatedCostUsd);

public record DailyUsageDto(
    DateOnly Date,
    string   Provider,
    int      Requests,
    int      InputTokens,
    int      OutputTokens);

public record AgentUsageDto(
    string  AgentName,
    int     Requests,
    int     InputTokens,
    int     OutputTokens,
    decimal EstimatedCostUsd);

public record RoutingAnalyticDto(
    string? RuleName,
    string  ModelId,
    string  Provider,
    int     Fires,
    decimal PctOfTotal);

public record EscalationRecordDto(
    Guid           Id,
    Guid?          SessionId,
    string         AgentName,
    string         ToModel,
    string         FromModel,
    DateTimeOffset CreatedAt);

public record SlowRequestDto(
    Guid           Id,
    string         AgentName,
    string         ModelId,
    string         Provider,
    int            DurationMs,
    string?        TaskType,
    DateTimeOffset CreatedAt);

public record CostRateDto(
    Guid    Id,
    string  ProviderName,
    string  ModelId,
    decimal InputCostPer1k,
    decimal OutputCostPer1k,
    DateTimeOffset EffectiveFrom);

public record RoutingRuleDto(
    Guid    Id,
    string  RuleName,
    int     Priority,
    bool?   NeedsVision,
    bool?   NeedsLongCtx,
    string? Complexity,
    string? TaskType,
    string? AgentName,
    string  ProviderName,
    string  ModelId,
    string? Reason,
    bool    IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

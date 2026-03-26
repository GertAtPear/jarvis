namespace Mediahost.Llm.Models;

public record ModelContext(
    string Provider,
    string Model,
    string SelectionReason,
    string? RuleApplied,
    int MaxTokens,
    Func<EscalationReason, Task<ModelContext>>? EscalateAsync = null
    // Phase 3 hook: currently always throws NotSupportedException.
    // Agents call this when mid-task complexity exceeds initial classification.
);

public record EscalationReason(string Reason, string? PreferredProvider = null);

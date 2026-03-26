namespace Mediahost.Llm.Models;

public record TaskClassification(
    string Complexity,         // simple | moderate | complex
    string TaskType,           // lookup | analysis | code | writing | briefing | tool_use
    bool NeedsVision,
    bool NeedsLongContext,     // true if likely > 50k tokens
    string AgentName,
    string ClassificationReason
);

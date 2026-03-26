namespace Jarvis.Api.Models;

public record OrchestratorResponse(
    string       Response,
    Guid         SessionId,
    List<string> AgentsUsed,
    int          TotalMs,
    bool         IsMorningBriefing,
    bool         SecretPurged = false
);

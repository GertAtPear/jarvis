namespace Mediahost.Agents.Alerting;

public interface IAlertDispatchService
{
    Task DispatchAsync(AlertPayload alert, CancellationToken ct = default);
}

public record AlertPayload(
    string AgentName,
    string AlertType,
    string Severity,
    string Title,
    string Body,
    string? SourceUrl,
    Guid   AlertId);

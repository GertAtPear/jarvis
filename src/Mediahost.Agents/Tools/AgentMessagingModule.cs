using System.Text.Json;
using Dapper;
using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Tools;

/// <summary>
/// Shared tool module that gives every agent the ability to post messages to
/// other agents and read messages sent to them.
///
/// All messages are stored in jarvis_schema.agent_messages and are visible to
/// Jarvis and surfaced in the UI as an activity feed. Messages marked
/// requires_approval = true will prompt Gert in the UI before the target agent
/// can act on them (used for tool requests, risky operations, etc.).
///
/// Register in any agent's DI setup:
///   services.AddScoped&lt;IToolModule, AgentMessagingModule&gt;();
///
/// Requires: agentName passed via constructor (each agent registers with its own name).
/// </summary>
public class AgentMessagingModule : IToolModule
{
    private readonly DbConnectionFactory _db;
    private readonly string              _agentName;
    private readonly ILogger             _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public AgentMessagingModule(DbConnectionFactory db, IAgentNameProvider nameProvider, ILogger<AgentMessagingModule> logger)
    {
        _db        = db;
        _agentName = nameProvider.AgentName;
        _logger    = logger;
    }

    public IEnumerable<ToolDefinition> GetDefinitions() =>
    [
        new ToolDefinition(
            "post_agent_message",
            "Post a message to another agent (or broadcast to all agents). " +
            "Use this to notify another agent of findings, request help, or escalate issues. " +
            "Set requires_approval=true when the action needs Gert's sign-off before the target agent can proceed. " +
            "Jarvis and Gert see ALL messages — nothing is private between agents.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "to_agent":          { "type": "string",  "description": "Target agent name (e.g. 'rex', 'andrew', 'rocky'). Omit to broadcast." },
                "message":           { "type": "string",  "description": "The message content" },
                "thread_id":         { "type": "integer", "description": "Optional: ID of a previous message to reply to (creates a thread)" },
                "requires_approval": { "type": "boolean", "description": "Set true if this message requires Gert's approval before the target agent acts. Default false." }
              },
              "required": ["message"]
            }
            """)),

        new ToolDefinition(
            "read_agent_messages",
            "Read messages sent to this agent (or broadcast to all). " +
            "Call this at the start of a session to check for pending requests from other agents.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "unread_only": { "type": "boolean", "description": "If true (default), return only unread messages. Set false to see recent history." },
                "limit":       { "type": "integer", "description": "Max messages to return (default: 20, max: 50)" }
              }
            }
            """))
    ];

    public async Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct = default)
    {
        return toolName switch
        {
            "post_agent_message"  => await PostMessageAsync(input, ct),
            "read_agent_messages" => await ReadMessagesAsync(input, ct),
            _ => null
        };
    }

    // ── Tool implementations ──────────────────────────────────────────────────

    private async Task<string> PostMessageAsync(JsonDocument input, CancellationToken ct)
    {
        var root             = input.RootElement;
        var message          = root.GetProperty("message").GetString()!;
        var toAgent          = root.TryGetProperty("to_agent", out var ta) ? ta.GetString() : null;
        var threadId         = root.TryGetProperty("thread_id", out var tid) ? (long?)tid.GetInt64() : null;
        var requiresApproval = root.TryGetProperty("requires_approval", out var ra) && ra.GetBoolean();

        try
        {
            await using var conn = _db.Create();
            var id = await conn.QuerySingleAsync<long>("""
                INSERT INTO jarvis_schema.agent_messages
                    (from_agent, to_agent, message, thread_id, requires_approval)
                VALUES
                    (@fromAgent, @toAgent, @message, @threadId, @requiresApproval)
                RETURNING id
                """, new
            {
                fromAgent        = _agentName,
                toAgent,
                message,
                threadId,
                requiresApproval
            });

            _logger.LogInformation("[{Agent}] Posted message #{Id} to {Target}",
                _agentName, id, toAgent ?? "broadcast");

            return JsonSerializer.Serialize(new
            {
                id,
                from_agent        = _agentName,
                to_agent          = toAgent,
                requires_approval = requiresApproval,
                status            = requiresApproval
                    ? "pending_approval — Gert will see this in the activity feed and can approve or deny"
                    : "sent"
            }, Opts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Agent}] Failed to post agent message", _agentName);
            return JsonSerializer.Serialize(new { error = ex.Message }, Opts);
        }
    }

    private async Task<string> ReadMessagesAsync(JsonDocument input, CancellationToken ct)
    {
        var root       = input.RootElement;
        var unreadOnly = !root.TryGetProperty("unread_only", out var u) || u.GetBoolean();
        var limit      = root.TryGetProperty("limit", out var l) ? Math.Min(l.GetInt32(), 50) : 20;

        try
        {
            await using var conn = _db.Create();

            var whereClause = unreadOnly
                ? "AND m.read_at IS NULL"
                : "";

            var rows = (await conn.QueryAsync($$"""
                SELECT
                    m.id,
                    m.from_agent,
                    m.to_agent,
                    m.message,
                    m.thread_id,
                    m.requires_approval,
                    m.approved_at,
                    m.denied_at,
                    m.created_at
                FROM jarvis_schema.agent_messages m
                WHERE (m.to_agent = @agentName OR m.to_agent IS NULL)
                  AND m.from_agent <> @agentName
                  {{whereClause}}
                ORDER BY m.created_at DESC
                LIMIT @limit
                """, new { agentName = _agentName, limit })).ToList();

            // Mark returned messages as read
            if (rows.Count > 0 && unreadOnly)
            {
                var ids = rows.Select(r => (long)r.id).ToList();
                await conn.ExecuteAsync("""
                    UPDATE jarvis_schema.agent_messages
                    SET read_at = NOW()
                    WHERE id = ANY(@ids) AND read_at IS NULL
                    """, new { ids });
            }

            var messages = rows.Select(r => new
            {
                id                = (long)r.id,
                from_agent        = (string)r.from_agent,
                to_agent          = (string?)r.to_agent,
                message           = (string)r.message,
                thread_id         = (long?)r.thread_id,
                requires_approval = (bool)r.requires_approval,
                approved          = r.approved_at is not null,
                denied            = r.denied_at is not null,
                created_at        = (DateTime)r.created_at
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                count    = messages.Count,
                messages
            }, Opts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Agent}] Failed to read agent messages", _agentName);
            return JsonSerializer.Serialize(new { error = ex.Message }, Opts);
        }
    }
}

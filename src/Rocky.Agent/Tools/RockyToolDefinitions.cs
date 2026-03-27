using System.Text.Json;
using Mediahost.Llm.Models;

namespace Rocky.Agent.Tools;

/// <summary>
/// All tool definitions available to RockyAgentService.
/// Rocky has 8 read-only tools for pipeline and service monitoring.
/// </summary>
public static class RockyToolDefinitions
{
    public static IReadOnlyList<ToolDefinition> All =>
    [
        new ToolDefinition(
            "get_service_status",
            "Get the current health status and most recent check result for a watched service.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "service_name": { "type": "string", "description": "Name or display name of the watched service" }
              },
              "required": ["service_name"]
            }
            """)),

        new ToolDefinition(
            "list_services",
            "List all watched services with their current health status. Optionally filter to only unhealthy services.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "unhealthy_only": { "type": "boolean", "description": "If true, return only services that are currently unhealthy" }
              }
            }
            """)),

        new ToolDefinition(
            "get_check_history",
            "Get recent check history for a specific service (last N results).",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "service_name": { "type": "string", "description": "Name of the watched service" },
                "limit":        { "type": "integer", "description": "Number of results to return (default: 20, max: 100)" }
              },
              "required": ["service_name"]
            }
            """)),

        new ToolDefinition(
            "get_active_alerts",
            "Get all currently unresolved alerts across all services.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "run_http_check",
            "Immediately run an HTTP health check against a URL and return the result.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "url":             { "type": "string",  "description": "URL to check" },
                "timeout_seconds": { "type": "integer", "description": "Timeout in seconds (default: 10)" }
              },
              "required": ["url"]
            }
            """)),

        new ToolDefinition(
            "run_tcp_check",
            "Immediately run a TCP port probe against a host:port and return the result.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "host":       { "type": "string",  "description": "Hostname or IP to probe" },
                "port":       { "type": "integer", "description": "TCP port to probe" },
                "timeout_ms": { "type": "integer", "description": "Timeout in milliseconds (default: 5000)" }
              },
              "required": ["host", "port"]
            }
            """)),

        new ToolDefinition(
            "run_container_check",
            "Check if a specific container is running on a registered server.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "server":    { "type": "string", "description": "Server hostname as registered in andrew" },
                "container": { "type": "string", "description": "Container name or partial name" }
              },
              "required": ["server", "container"]
            }
            """)),

        new ToolDefinition(
            "run_ssh_process_check",
            "Check if a process is running on a registered server via SSH.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "server":  { "type": "string", "description": "Server hostname as registered in andrew" },
                "process": { "type": "string", "description": "Process name or pattern to search for (used with pgrep -f)" }
              },
              "required": ["server", "process"]
            }
            """)),

        // ── Alert Channels ────────────────────────────────────────────────────

        new ToolDefinition(
            "list_alert_channels",
            "List all configured alert channels (Slack, email) for alert dispatch.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "configure_alert_channel",
            "Create or update an alert channel for Slack webhook or email notifications.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "channel_name":      { "type": "string", "description": "Unique name for this channel (e.g. 'slack-ops', 'email-oncall')" },
                "channel_type":      { "type": "string", "description": "Channel type: slack | email" },
                "config_json":       { "type": "string", "description": "JSON config: Slack needs {\"webhook_url_secret\":\"/path\"}, email needs {\"to\":[\"addr\"],\"smtp_secret\":\"/path\"}" },
                "min_severity":      { "type": "string", "description": "Minimum severity to dispatch: low | medium | high | critical (default: high)" },
                "agent_filter":      { "type": "string", "description": "Comma-separated agent names to filter (empty = all agents)" },
                "alert_type_filter": { "type": "string", "description": "Comma-separated alert types to filter (empty = all types)" }
              },
              "required": ["channel_name", "channel_type", "config_json"]
            }
            """))
    ];
}

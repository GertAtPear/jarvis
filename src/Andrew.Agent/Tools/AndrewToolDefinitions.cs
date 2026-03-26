using System.Text.Json;
using Mediahost.Llm.Models;

namespace Andrew.Agent.Tools;

public static class AndrewToolDefinitions
{
    public static List<ToolDefinition> GetTools() =>
    [
        new ToolDefinition(
            "list_servers",
            "List all known servers from Andrew's knowledge store with status, IP, OS, last scan",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "filter_status": {
                  "type": "string",
                  "enum": ["online", "offline", "unknown", "pending_credentials"],
                  "description": "Optional: filter servers by status"
                }
              }
            }
            """)),

        new ToolDefinition(
            "get_server_details",
            "Get full details for a server: OS info, all containers, running apps, recent discovery log",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "hostname": {
                  "type": "string",
                  "description": "The server hostname"
                }
              },
              "required": ["hostname"]
            }
            """)),

        new ToolDefinition(
            "list_containers",
            "List Docker containers across all or a specific server",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "server_hostname": {
                  "type": "string",
                  "description": "Optional: filter by server hostname"
                },
                "status_filter": {
                  "type": "string",
                  "enum": ["running", "stopped", "all"],
                  "description": "Filter by container status (default: running)"
                }
              }
            }
            """)),

        new ToolDefinition(
            "find_application",
            "Find where a named application is running. Searches container names, image names, app names.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "search_term": {
                  "type": "string",
                  "description": "Name or partial name of the application to find"
                }
              },
              "required": ["search_term"]
            }
            """)),

        new ToolDefinition(
            "get_network_status",
            "Get VPN status, active internet line (primary/backup), DNS status, and last check times",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "get_discovery_log",
            "Get recent discovery history — when servers were last scanned and results",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "server_hostname": {
                  "type": "string",
                  "description": "Optional: filter log by server hostname"
                },
                "limit": {
                  "type": "number",
                  "description": "Number of log entries to return (default: 10)"
                }
              }
            }
            """)),

        new ToolDefinition(
            "refresh_server",
            "Force live SSH re-discovery of a server right now. Use only when asked explicitly or data > 4h old.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "hostname": {
                  "type": "string",
                  "description": "The server hostname to refresh"
                }
              },
              "required": ["hostname"]
            }
            """)),

        new ToolDefinition(
            "register_server",
            "Register a new server for Andrew to monitor. Returns vault path for credential storage. " +
            "Use connection_type='winrm' for Windows Server, 'ssh' for Linux (default).",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "hostname": {
                  "type": "string",
                  "description": "Server hostname or FQDN"
                },
                "ip_address": {
                  "type": "string",
                  "description": "Server IP address"
                },
                "ssh_port": {
                  "type": "number",
                  "description": "SSH port for Linux servers (default: 22)"
                },
                "connection_type": {
                  "type": "string",
                  "enum": ["ssh", "winrm"],
                  "description": "Connection protocol: 'ssh' for Linux (default), 'winrm' for Windows Server"
                },
                "notes": {
                  "type": "string",
                  "description": "Optional notes about this server"
                }
              },
              "required": ["hostname", "ip_address"]
            }
            """)),

        new ToolDefinition(
            "activate_server",
            "After credentials are stored in vault, test connection and run first discovery.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "hostname": {
                  "type": "string",
                  "description": "The server hostname to activate"
                }
              },
              "required": ["hostname"]
            }
            """)),

        // ── Scheduled checks ──────────────────────────────────────────────────

        new ToolDefinition(
            "schedule_check",
            "Create a recurring health check: container running, server/DB up, website up, or port open. " +
            "Use schedule_type=interval with interval_minutes for 'every N minutes'. " +
            "Use schedule_type=cron with cron_expression (Quartz 6-field: seconds minutes hours day month weekday) " +
            "for time-of-day schedules, e.g. '0 0 6 * * ?' for daily at 06:00 SAST.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Friendly name for this check, e.g. 'Redis on prod-01'"
                },
                "check_type": {
                  "type": "string",
                  "enum": ["container_running", "server_up", "website_up", "port_listening"],
                  "description": "What to check"
                },
                "target": {
                  "type": "string",
                  "description": "Container name, hostname, URL, or hostname:port depending on check_type"
                },
                "server_hostname": {
                  "type": "string",
                  "description": "For container_running: restrict to this server (optional)"
                },
                "schedule_type": {
                  "type": "string",
                  "enum": ["interval", "cron"],
                  "description": "interval = every N minutes; cron = Quartz cron expression"
                },
                "interval_minutes": {
                  "type": "number",
                  "description": "For interval schedule: repeat every N minutes (e.g. 10)"
                },
                "cron_expression": {
                  "type": "string",
                  "description": "For cron schedule: Quartz 6-field expression e.g. '0 0 6 * * ?' for daily at 06:00"
                },
                "notify_on_failure": {
                  "type": "boolean",
                  "description": "Log a warning when check fails (default: true)"
                }
              },
              "required": ["name", "check_type", "target", "schedule_type"]
            }
            """)),

        new ToolDefinition(
            "list_scheduled_checks",
            "List all configured scheduled checks with their current status, last result, and schedule.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "status_filter": {
                  "type": "string",
                  "enum": ["all", "ok", "failed", "unknown"],
                  "description": "Filter by last known status (default: all)"
                }
              }
            }
            """)),

        new ToolDefinition(
            "delete_scheduled_check",
            "Remove a scheduled check by name or ID. Stops future executions immediately.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name_or_id": {
                  "type": "string",
                  "description": "The check name (exact match) or UUID"
                }
              },
              "required": ["name_or_id"]
            }
            """)),

        new ToolDefinition(
            "store_secret",
            "Store a secret (API key, password, credential) in the vault at the specified path and key. " +
            "Use when the user provides a sensitive value to be stored. " +
            "The chat exchange containing the secret will be purged from history after storing.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Vault path, e.g. 'ai/openai' or 'servers/prod-01'"
                },
                "key": {
                  "type": "string",
                  "description": "Secret key name, e.g. 'api_key' or 'ssh_password'"
                },
                "value": {
                  "type": "string",
                  "description": "The secret value to store"
                }
              },
              "required": ["path", "key", "value"]
            }
            """)),

        new ToolDefinition(
            "remember_fact",
            "Store a fact in Andrew's permanent memory. Persists across all future conversations.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key":   { "type": "string", "description": "Short identifier e.g. 'user_timezone' or 'prod_vpn_ip'" },
                "value": { "type": "string", "description": "The value to remember" }
              },
              "required": ["key", "value"]
            }
            """)),

        new ToolDefinition(
            "forget_fact",
            "Remove a fact from Andrew's permanent memory.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key": { "type": "string", "description": "The memory key to delete" }
              },
              "required": ["key"]
            }
            """)),

        new ToolDefinition(
            "get_check_history",
            "Get recent results for a specific scheduled check. Shows pass/fail history with timestamps.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name_or_id": {
                  "type": "string",
                  "description": "The check name or UUID"
                },
                "limit": {
                  "type": "number",
                  "description": "Number of recent results to return (default: 20)"
                }
              },
              "required": ["name_or_id"]
            }
            """)),

    ];
}

using System.Text.Json;
using Mediahost.Llm.Models;

namespace Sam.Agent.Tools;

public static class SamToolDefinitions
{
    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new("list_databases",
            "List all registered databases with their current health status.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("get_database_health",
            "Get detailed health information for a specific database including connections, replication status, and recent slow queries.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name as registered in sam_schema.databases"}
              },
              "required":["name"]
            }
            """)),

        new("get_slow_queries",
            "Retrieve the slowest queries for a database.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name"},
                "limit": {"type":"integer","description":"Max results (default 20)"}
              },
              "required":["name"]
            }
            """)),

        new("get_table_stats",
            "Get table size statistics for a database, ordered by largest tables first.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name"},
                "limit": {"type":"integer","description":"Max results (default 20)"}
              },
              "required":["name"]
            }
            """)),

        new("get_connection_stats",
            "Get current connection statistics for a database.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name"}
              },
              "required":["name"]
            }
            """)),

        new("get_replication_status",
            "Get replication lag and status for a database.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name"}
              },
              "required":["name"]
            }
            """)),

        new("run_safe_query",
            "Run a read-only SELECT query against a registered database. Only SELECT statements are allowed. A LIMIT 1000 is enforced if absent.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "database_name": {"type":"string","description":"Database name as registered"},
                "query": {"type":"string","description":"The SELECT query to run"}
              },
              "required":["database_name","query"]
            }
            """)),

        new("explain_query",
            "Run EXPLAIN (not ANALYZE) on a query to see the query plan without executing it.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "database_name": {"type":"string","description":"Database name as registered"},
                "query": {"type":"string","description":"The query to explain"}
              },
              "required":["database_name","query"]
            }
            """)),

        new("trigger_scan",
            "Trigger an immediate scan of a specific database.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name to scan"}
              },
              "required":["name"]
            }
            """)),

        new("get_discovery_log",
            "Get recent scan history for a database.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Database name"},
                "limit": {"type":"integer","description":"Max results (default 10)"}
              },
              "required":["name"]
            }
            """))
    ];
}

using System.Text.Json;
using Mediahost.Llm.Models;

namespace Research.Agent.Tools;

public static class ResearchToolDefinitions
{
    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new ToolDefinition(
            "list_databases",
            "List all business databases available for research queries. Always call this first to know what data sources are available.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "sql_query",
            "Execute a read-only SQL SELECT query against a registered business database. " +
            "Use for business data analysis: revenue, content volumes, subscriber counts, trends, etc. " +
            "Only SELECT statements are allowed. Results are limited to 500 rows.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "database_name": { "type": "string", "description": "Database name as shown in list_databases" },
                "query":         { "type": "string", "description": "SQL SELECT query to execute" }
              },
              "required": ["database_name", "query"]
            }
            """)),

        new ToolDefinition(
            "remember_finding",
            "Store a research finding or data source note in long-term memory for future reference.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key":   { "type": "string", "description": "Short identifier e.g. 'top_stations_query', 'broadcast_db_schema'" },
                "value": { "type": "string", "description": "The finding or note to store" }
              },
              "required": ["key", "value"]
            }
            """)),

        new ToolDefinition(
            "forget_finding",
            "Remove a previously stored research finding from memory.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key": { "type": "string", "description": "The memory key to remove" }
              },
              "required": ["key"]
            }
            """))
    ];
}

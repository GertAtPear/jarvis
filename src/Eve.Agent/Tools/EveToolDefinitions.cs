using System.Text.Json;
using Mediahost.Llm.Models;

namespace Eve.Agent.Tools;

public static class EveToolDefinitions
{
    public static List<ToolDefinition> GetTools() =>
    [
        new ToolDefinition(
            "add_reminder",
            "Add a reminder, follow-up, or note for Gert",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "title": {
                  "type": "string",
                  "description": "Short title of the reminder"
                },
                "description": {
                  "type": "string",
                  "description": "Optional longer description or context"
                },
                "reminder_type": {
                  "type": "string",
                  "enum": ["once", "yearly", "monthly", "weekly"],
                  "description": "How this reminder recurs"
                },
                "due_date": {
                  "type": "string",
                  "description": "Date for once-off reminders: YYYY-MM-DD"
                },
                "recur_month": {
                  "type": "number",
                  "description": "For yearly reminders: month number 1-12"
                },
                "recur_day": {
                  "type": "number",
                  "description": "For yearly/monthly: day of month (1-31). For weekly: day of week (0=Sun, 1=Mon … 6=Sat)"
                },
                "person_name": {
                  "type": "string",
                  "description": "Person this reminder is associated with"
                },
                "tags": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Optional tags e.g. [\"birthday\", \"work\"]"
                }
              },
              "required": ["title", "reminder_type"]
            }
            """)),

        new ToolDefinition(
            "list_reminders",
            "List upcoming reminders, today's items, or search by person name",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "filter": {
                  "type": "string",
                  "enum": ["today", "tomorrow", "week", "all"],
                  "description": "Which reminders to return"
                },
                "person_name": {
                  "type": "string",
                  "description": "Optional: filter reminders for a specific person"
                }
              },
              "required": ["filter"]
            }
            """)),

        new ToolDefinition(
            "complete_reminder",
            "Mark a reminder as done",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "reminder_id": {
                  "type": "string",
                  "description": "UUID of the reminder to mark done"
                }
              },
              "required": ["reminder_id"]
            }
            """)),

        new ToolDefinition(
            "snooze_reminder",
            "Snooze a reminder until a later date",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "reminder_id": {
                  "type": "string",
                  "description": "UUID of the reminder to snooze"
                },
                "until_date": {
                  "type": "string",
                  "description": "Date to snooze until: YYYY-MM-DD"
                }
              },
              "required": ["reminder_id", "until_date"]
            }
            """)),

        new ToolDefinition(
            "add_contact",
            "Add or update a person's details including birthday and anniversary",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Full name of the person"
                },
                "relationship": {
                  "type": "string",
                  "description": "Relationship type: colleague, friend, family, supplier"
                },
                "birthday": {
                  "type": "string",
                  "description": "Birthday as MM-DD (year not required)"
                },
                "anniversary": {
                  "type": "string",
                  "description": "Anniversary date as MM-DD"
                },
                "notes": {
                  "type": "string",
                  "description": "Any notes about this person"
                }
              },
              "required": ["name"]
            }
            """)),

        new ToolDefinition(
            "get_briefing",
            "Generate today's morning briefing with reminders, birthdays, and upcoming events",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "remember_fact",
            "Store a fact in Eve's permanent memory. Use for things to remember forever — user details, preferences, family info.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key":   { "type": "string", "description": "Short identifier e.g. 'user_birthday' or 'spouse_name'" },
                "value": { "type": "string", "description": "The value to remember" }
              },
              "required": ["key", "value"]
            }
            """)),

        new ToolDefinition(
            "forget_fact",
            "Remove a fact from Eve's permanent memory.",
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
            "create_calendar_event",
            "Create a Google Calendar event for a reminder or task",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "title": {
                  "type": "string",
                  "description": "Event title"
                },
                "date": {
                  "type": "string",
                  "description": "Event date: YYYY-MM-DD"
                },
                "time": {
                  "type": "string",
                  "description": "Optional event time: HH:MM (24-hour)"
                },
                "description": {
                  "type": "string",
                  "description": "Optional event description"
                },
                "reminder_id": {
                  "type": "string",
                  "description": "Optional: UUID of an existing reminder to link to this event"
                }
              },
              "required": ["title", "date"]
            }
            """))
    ];
}

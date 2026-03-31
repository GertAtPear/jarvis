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
            "Add or update a person or business in the contact database. Always call this immediately when Gert mentions a new person or shares any contact detail.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Full name of the person or business"
                },
                "relationship": {
                  "type": "string",
                  "description": "Relationship to Gert e.g. 'girlfriend', 'sister', 'colleague', 'client', 'supplier'"
                },
                "contact_type": {
                  "type": "string",
                  "enum": ["person", "business"],
                  "description": "Whether this is a person or a business"
                },
                "company": {
                  "type": "string",
                  "description": "Employer or business name"
                },
                "phone_cell": {
                  "type": "string",
                  "description": "Cell / mobile number"
                },
                "phone_work": {
                  "type": "string",
                  "description": "Work / office number"
                },
                "phone_home": {
                  "type": "string",
                  "description": "Home landline number"
                },
                "email_personal": {
                  "type": "string",
                  "description": "Personal email address"
                },
                "email_work": {
                  "type": "string",
                  "description": "Work / business email address"
                },
                "address_home": {
                  "type": "string",
                  "description": "Full home address"
                },
                "address_work": {
                  "type": "string",
                  "description": "Full work / office address"
                },
                "website": {
                  "type": "string",
                  "description": "Website URL"
                },
                "facebook_url": {
                  "type": "string",
                  "description": "Facebook profile or page URL"
                },
                "linkedin_url": {
                  "type": "string",
                  "description": "LinkedIn profile URL"
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
                  "description": "Any notes about this person, including relationship context (e.g. 'Joan\\'s son')"
                }
              },
              "required": ["name"]
            }
            """)),

        new ToolDefinition(
            "get_contact",
            "Look up a person's full stored details: phones, emails, addresses, social links, relationship, and notes. " +
            "Always call this before drafting an email, sending a location, or when Gert mentions any person by name.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Name or partial name to search for"
                }
              },
              "required": ["name"]
            }
            """)),

        new ToolDefinition(
            "get_briefing",
            "Generate today's morning briefing with overdue items, today's reminders, birthdays, and upcoming events",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {}
            }
            """)),

        new ToolDefinition(
            "remember_fact",
            "Store a fact in Eve's permanent memory. Use for things to remember forever — user details, preferences, family info, document locations.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "key":   { "type": "string", "description": "Short identifier e.g. 'joan_relation' or 'id_document_path'" },
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
            """)),

        new ToolDefinition(
            "draft_email",
            "Prepare an email draft for Gert. Resolves the recipient's email address from contacts if a name is given. " +
            "Returns a compose_url — immediately call laptop_open_url with it to open the email client. " +
            "Infers Gmail (personal contacts) vs Outlook web (business contacts) unless mail_client is specified. " +
            "Note: email clients cannot receive file attachments via URL — list attachment paths so Gert can attach manually.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "to": {
                  "type": "string",
                  "description": "Recipient name (will be looked up in contacts) or a raw email address"
                },
                "subject": {
                  "type": "string",
                  "description": "Email subject line"
                },
                "body": {
                  "type": "string",
                  "description": "Email body text"
                },
                "mail_client": {
                  "type": "string",
                  "enum": ["gmail", "outlook"],
                  "description": "Override the inferred email client. Default: gmail for personal contacts, outlook for business."
                },
                "attachment_paths": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "Optional list of file paths on Gert's laptop to list as attachments (must be added manually)"
                }
              },
              "required": ["to", "subject", "body"]
            }
            """)),

        new ToolDefinition(
            "get_location_pin",
            "Get a Google Maps link for a contact's home or work address. " +
            "After receiving maps_url, immediately call laptop_open_url with it to open the map.",
            JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Contact name to look up"
                },
                "address_type": {
                  "type": "string",
                  "enum": ["home", "work"],
                  "description": "Which address to use (default: home)"
                }
              },
              "required": ["name"]
            }
            """))
    ];
}

using System.Text.Json;
using Eve.Agent.Data;
using Eve.Agent.Data.Repositories;
using Eve.Agent.Models;
using Eve.Agent.Services;
using Mediahost.Agents.Data;
using Mediahost.Agents.Services;
using Mediahost.Llm.Models;

namespace Eve.Agent.Tools;

public class EveToolExecutor(
    ReminderRepository reminders,
    ContactRepository contacts,
    MorningBriefingGeneratorService briefingGenerator,
    GoogleCalendarService calendar,
    EveMemoryService memory,
    SharedMemoryService sharedMemory,
    ILogger<EveToolExecutor> logger) : IAgentToolExecutor
{
    public IReadOnlyList<ToolDefinition> GetTools() => EveToolDefinitions.GetTools();
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<string> ExecuteAsync(string toolName, JsonDocument input, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "add_reminder"          => await AddReminderAsync(input, ct),
                "list_reminders"        => await ListRemindersAsync(input, ct),
                "complete_reminder"     => await CompleteReminderAsync(input, ct),
                "snooze_reminder"       => await SnoozeReminderAsync(input, ct),
                "add_contact"           => await AddContactAsync(input, ct),
                "get_contact"           => await GetContactAsync(input, ct),
                "get_briefing"          => await GetBriefingAsync(ct),
                "create_calendar_event" => await CreateCalendarEventAsync(input, ct),
                "remember_fact"         => await RememberFactAsync(input, ct),
                "forget_fact"           => await ForgetFactAsync(input, ct),
                "share_context"         => await ShareContextAsync(input, ct),
                "unshare_context"       => await UnshareContextAsync(input, ct),
                "draft_email"           => await DraftEmailAsync(input, ct),
                "get_location_pin"      => await GetLocationPinAsync(input, ct),
                _ => Err($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool execution failed: {Tool}", toolName);
            return Err(ex.Message);
        }
    }

    // ── Reminders ─────────────────────────────────────────────────────────────

    private async Task<string> AddReminderAsync(JsonDocument input, CancellationToken ct)
    {
        var title   = RequireString(input, "title");
        var type    = GetString(input, "reminder_type") ?? "once";
        var desc    = GetString(input, "description");
        var person  = GetString(input, "person_name");
        var notes   = GetString(input, "notes");

        DateOnly? dueDate = null;
        if (GetString(input, "due_date") is { } ds)
            dueDate = DateOnly.Parse(ds);

        int? recurMonth = GetInt(input, "recur_month");
        int? recurDay   = GetInt(input, "recur_day");

        string? tagsJson = null;
        if (input.RootElement.TryGetProperty("tags", out var tagsEl))
            tagsJson = tagsEl.GetRawText();

        var reminder = new Reminder
        {
            Id           = Guid.NewGuid(),
            Title        = title,
            Description  = desc,
            ReminderType = type,
            DueDate      = dueDate,
            RecurMonth   = recurMonth,
            RecurDay     = recurDay,
            PersonName   = person,
            TagsJson     = tagsJson ?? "null",
            Status       = "active",
            Notes        = notes
        };

        var id = await reminders.UpsertAsync(reminder);

        return Ok(new { id, title, reminder_type = type, message = "Reminder saved." });
    }

    private async Task<string> ListRemindersAsync(JsonDocument input, CancellationToken ct)
    {
        var filter = GetString(input, "filter") ?? "today";
        var person = GetString(input, "person_name");

        var today = DateOnly.FromDateTime(DateTime.Today);

        IEnumerable<Reminder> list;

        if (!string.IsNullOrEmpty(person))
        {
            list = await reminders.GetByPersonAsync(person);
        }
        else
        {
            list = filter switch
            {
                "today"    => await reminders.GetDueTodayAsync(today),
                "tomorrow" => await reminders.GetDueTomorrowAsync(today.AddDays(1)),
                "week"     => await reminders.GetUpcomingAsync(7),
                _          => await reminders.GetAllActiveAsync()
            };
        }

        var rows = list.Select(r => new
        {
            id           = r.Id,
            title        = r.Title,
            type         = r.ReminderType,
            due_date     = r.DueDate?.ToString("yyyy-MM-dd"),
            person_name  = r.PersonName,
            status       = r.Status,
            description  = r.Description
        }).ToList();

        return Ok(new { count = rows.Count, filter, reminders = rows });
    }

    private async Task<string> CompleteReminderAsync(JsonDocument input, CancellationToken ct)
    {
        var id = Guid.Parse(RequireString(input, "reminder_id"));
        await reminders.MarkDoneAsync(id);
        return Ok(new { id, status = "done", message = "Reminder marked as done." });
    }

    private async Task<string> SnoozeReminderAsync(JsonDocument input, CancellationToken ct)
    {
        var id    = Guid.Parse(RequireString(input, "reminder_id"));
        var until = DateOnly.Parse(RequireString(input, "until_date"));
        await reminders.SnoozeAsync(id, until);
        return Ok(new { id, snoozed_until = until.ToString("yyyy-MM-dd"), message = "Reminder snoozed." });
    }

    // ── Contacts ──────────────────────────────────────────────────────────────

    private async Task<string> AddContactAsync(JsonDocument input, CancellationToken ct)
    {
        var name         = RequireString(input, "name");
        var relationship = GetString(input, "relationship");
        var contactType  = GetString(input, "contact_type") ?? "person";
        var company      = GetString(input, "company");
        var phoneCell    = GetString(input, "phone_cell");
        var phoneWork    = GetString(input, "phone_work");
        var phoneHome    = GetString(input, "phone_home");
        var emailPersonal = GetString(input, "email_personal");
        var emailWork    = GetString(input, "email_work");
        var addressHome  = GetString(input, "address_home");
        var addressWork  = GetString(input, "address_work");
        var website      = GetString(input, "website");
        var notes        = GetString(input, "notes");

        // Build social_links JSONB from individual URL params
        var socialLinks = new Dictionary<string, string?>();
        if (GetString(input, "facebook_url") is { } fb)  socialLinks["facebook"]  = fb;
        if (GetString(input, "linkedin_url") is { } li)  socialLinks["linkedin"]  = li;
        var socialLinksJson = socialLinks.Count > 0
            ? JsonSerializer.Serialize(socialLinks)
            : null;

        DateOnly? birthday    = null;
        DateOnly? anniversary = null;

        if (GetString(input, "birthday") is { } bStr)
        {
            var parts = bStr.Split('-');
            birthday = new DateOnly(2000, int.Parse(parts[0]), int.Parse(parts[1]));
        }

        if (GetString(input, "anniversary") is { } aStr)
        {
            var parts = aStr.Split('-');
            anniversary = new DateOnly(2000, int.Parse(parts[0]), int.Parse(parts[1]));
        }

        var contact = new Contact
        {
            Id            = Guid.NewGuid(),
            Name          = name,
            Relationship  = relationship,
            ContactType   = contactType,
            Company       = company,
            PhoneCell     = phoneCell,
            PhoneWork     = phoneWork,
            PhoneHome     = phoneHome,
            EmailPersonal = emailPersonal,
            EmailWork     = emailWork,
            AddressHome   = addressHome,
            AddressWork   = addressWork,
            Website       = website,
            SocialLinksJson = socialLinksJson,
            Birthday      = birthday,
            Anniversary   = anniversary,
            Notes         = notes
        };

        await contacts.UpsertAsync(contact);
        return Ok(new { name, message = "Contact saved." });
    }

    private async Task<string> GetContactAsync(JsonDocument input, CancellationToken ct)
    {
        var name    = RequireString(input, "name");
        var contact = await contacts.SearchByNameAsync(name);

        if (contact is null)
            return Ok(new { found = false, name, message = $"No contact found matching '{name}'." });

        // Parse social links for cleaner output
        object? socialLinks = null;
        if (contact.SocialLinksJson is { } sl && sl != "{}")
        {
            try { socialLinks = JsonDocument.Parse(sl).RootElement; } catch { /* ignore */ }
        }

        return Ok(new
        {
            found         = true,
            id            = contact.Id,
            name          = contact.Name,
            relationship  = contact.Relationship,
            contact_type  = contact.ContactType,
            company       = contact.Company,
            phone_cell    = contact.PhoneCell,
            phone_work    = contact.PhoneWork,
            phone_home    = contact.PhoneHome,
            email_personal = contact.EmailPersonal,
            email_work    = contact.EmailWork,
            address_home  = contact.AddressHome,
            address_work  = contact.AddressWork,
            website       = contact.Website,
            social_links  = socialLinks,
            birthday      = contact.Birthday?.ToString("MM-dd"),
            anniversary   = contact.Anniversary?.ToString("MM-dd"),
            notes         = contact.Notes
        });
    }

    // ── Briefing ──────────────────────────────────────────────────────────────

    private async Task<string> GetBriefingAsync(CancellationToken ct)
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var briefing = await briefingGenerator.GenerateBriefingAsync(today);
        return Ok(new { briefing });
    }

    // ── Calendar ──────────────────────────────────────────────────────────────

    private async Task<string> CreateCalendarEventAsync(JsonDocument input, CancellationToken ct)
    {
        var title       = RequireString(input, "title");
        var date        = RequireString(input, "date");
        var time        = GetString(input, "time");
        var description = GetString(input, "description");

        var eventId = await calendar.CreateEventAsync(title, date, time, description, ct);

        if (GetString(input, "reminder_id") is { } ridStr && Guid.TryParse(ridStr, out var rid))
            await reminders.SetCalendarEventIdAsync(rid, eventId);

        return Ok(new
        {
            calendar_event_id = eventId,
            title,
            date,
            message = "Calendar event created."
        });
    }

    // ── Permanent memory ──────────────────────────────────────────────────────

    private async Task<string> RememberFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");
        await memory.RememberFactAsync(key, value, ct);
        return Ok(new { remembered = true, key, message = $"I'll remember: {key} = {value}" });
    }

    private async Task<string> ForgetFactAsync(JsonDocument input, CancellationToken ct)
    {
        var key = RequireString(input, "key");
        await memory.ForgetFactAsync(key, ct);
        return Ok(new { forgotten = true, key });
    }

    // ── Shared platform context ───────────────────────────────────────────────

    private async Task<string> ShareContextAsync(JsonDocument input, CancellationToken ct)
    {
        var key   = RequireString(input, "key");
        var value = RequireString(input, "value");
        await sharedMemory.WriteContextAsync(key, value, "eve", ct);
        return Ok(new { shared = true, key, message = $"'{key}' is now visible to all agents." });
    }

    private async Task<string> UnshareContextAsync(JsonDocument input, CancellationToken ct)
    {
        var key = RequireString(input, "key");
        await sharedMemory.DeleteContextAsync(key, ct);
        return Ok(new { unshared = true, key });
    }

    // ── Email drafting ────────────────────────────────────────────────────────

    private async Task<string> DraftEmailAsync(JsonDocument input, CancellationToken ct)
    {
        var to      = RequireString(input, "to");
        var subject = RequireString(input, "subject");
        var body    = RequireString(input, "body");
        var clientOverride = GetString(input, "mail_client");

        List<string> attachmentPaths = [];
        if (input.RootElement.TryGetProperty("attachment_paths", out var apEl)
            && apEl.ValueKind == JsonValueKind.Array)
        {
            attachmentPaths = apEl.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        string  resolvedEmail = to;
        string? contactName   = null;
        string? relationship  = null;

        if (!to.Contains('@'))
        {
            var contact = await contacts.SearchByNameAsync(to);
            if (contact is null)
            {
                return Ok(new
                {
                    resolved         = false,
                    to,
                    message          = $"No contact found for '{to}'. Please provide an email address or add the contact first.",
                    subject,
                    body,
                    attachment_paths = attachmentPaths
                });
            }

            contactName  = contact.Name;
            relationship = contact.Relationship?.ToLower();

            // Choose email: infer from client override or relationship
            var isPersonal = IsPersonalRelationship(relationship, clientOverride);
            var email = isPersonal
                ? (contact.EmailPersonal ?? contact.EmailWork)
                : (contact.EmailWork     ?? contact.EmailPersonal);

            if (email is null)
            {
                return Ok(new
                {
                    resolved         = false,
                    to               = contact.Name,
                    message          = $"No email address stored for {contact.Name}. Ask Gert for the address or add it to the contact.",
                    subject,
                    body,
                    attachment_paths = attachmentPaths
                });
            }

            resolvedEmail = email;
        }

        var mailClient  = ResolveMailClient(relationship, clientOverride);
        var composeUrl  = BuildComposeUrl(resolvedEmail, subject, body, mailClient);
        var attachNote  = attachmentPaths.Count > 0
            ? $"Open the email, then manually attach: {string.Join(", ", attachmentPaths)}"
            : "Draft ready — open the link to review and send.";

        return Ok(new
        {
            resolved          = true,
            to                = resolvedEmail,
            contact_name      = contactName,
            subject,
            body,
            mail_client_used  = mailClient,
            compose_url       = composeUrl,
            attachment_paths  = attachmentPaths,
            message           = attachNote
        });
    }

    private static bool IsPersonalRelationship(string? relationship, string? clientOverride)
    {
        if (clientOverride == "gmail")   return true;
        if (clientOverride == "outlook") return false;
        if (relationship is null) return false;
        return relationship is "girlfriend" or "boyfriend" or "wife" or "husband"
            or "sister" or "brother" or "mother" or "father" or "parent"
            or "son" or "daughter" or "family" or "friend" or "personal";
    }

    private static string ResolveMailClient(string? relationship, string? clientOverride)
    {
        if (clientOverride is "gmail" or "outlook") return clientOverride;
        return IsPersonalRelationship(relationship, null) ? "gmail" : "outlook";
    }

    private static string BuildComposeUrl(string email, string subject, string body, string mailClient)
    {
        var es = Uri.EscapeDataString(subject);
        var eb = Uri.EscapeDataString(body);
        return mailClient == "gmail"
            ? $"https://mail.google.com/mail/?view=cm&to={Uri.EscapeDataString(email)}&su={es}&body={eb}"
            : $"https://outlook.office.com/mail/deeplink/compose?to={Uri.EscapeDataString(email)}&subject={es}&body={eb}";
    }

    // ── Location pin ──────────────────────────────────────────────────────────

    private async Task<string> GetLocationPinAsync(JsonDocument input, CancellationToken ct)
    {
        var name        = RequireString(input, "name");
        var addressType = GetString(input, "address_type") ?? "home";

        var contact = await contacts.SearchByNameAsync(name);
        if (contact is null)
            return Ok(new { found = false, name, message = $"No contact found matching '{name}'." });

        var address = addressType == "work" ? contact.AddressWork : contact.AddressHome;

        if (string.IsNullOrWhiteSpace(address))
        {
            return Ok(new
            {
                found        = false,
                name         = contact.Name,
                address_type = addressType,
                message      = $"No {addressType} address stored for {contact.Name}. " +
                               "You could try searching for it using web_search."
            });
        }

        var mapsUrl = $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(address)}";
        return Ok(new
        {
            found        = true,
            name         = contact.Name,
            address_type = addressType,
            address,
            maps_url     = mapsUrl,
            message      = $"Got the {addressType} address for {contact.Name}. Call laptop_open_url with maps_url to open the map."
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RequireString(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty(key, out var prop))
            throw new ArgumentException($"Required parameter '{key}' is missing.");
        return prop.GetString() ?? throw new ArgumentException($"Parameter '{key}' must not be null.");
    }

    private static string? GetString(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) ? p.GetString() : null;

    private static int? GetInt(JsonDocument doc, string key) =>
        doc.RootElement.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32()
            : null;

    private static string Ok(object value)  => JsonSerializer.Serialize(value, JsonOpts);
    private static string Err(string msg)   => JsonSerializer.Serialize(new { error = msg }, JsonOpts);
}

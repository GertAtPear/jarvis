using System.Text;
using Eve.Agent.Data.Repositories;

namespace Eve.Agent.Services;

public class MorningBriefingGeneratorService(
    ReminderRepository reminders,
    ContactRepository contacts)
{
    public async Task<string> GenerateBriefingAsync(DateOnly today)
    {
        var tomorrow  = today.AddDays(1);

        var dueToday    = (await reminders.GetDueTodayAsync(today)).ToList();
        var dueTomorrow = (await reminders.GetDueTomorrowAsync(tomorrow)).ToList();
        var upcoming    = (await reminders.GetUpcomingAsync(3))
                            .Where(r => r.DueDate > tomorrow)
                            .ToList();

        var birthdaysThisWeek     = (await contacts.GetBirthdaysThisWeekAsync()).ToList();
        var anniversariesThisWeek = (await contacts.GetAnniversariesThisWeekAsync()).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"### 📅 Today — {today.DayOfWeek} {today:d MMMM yyyy}");

        // ── Due today ─────────────────────────────────────────────────────────
        if (dueToday.Count > 0 || birthdaysThisWeek.Any(IsBirthdayToday(today)) || anniversariesThisWeek.Any(IsAnniversaryToday(today)))
        {
            sb.AppendLine();
            sb.AppendLine("**Due today:**");

            foreach (var c in birthdaysThisWeek.Where(IsBirthdayToday(today)))
                sb.AppendLine($"- 🎂 {c.Name}'s birthday{(c.Notes is not null ? $" — {c.Notes}" : " — give them a call")}");

            foreach (var c in anniversariesThisWeek.Where(IsAnniversaryToday(today)))
                sb.AppendLine($"- 💍 {c.Name}'s anniversary");

            foreach (var r in dueToday)
            {
                var icon = r.ReminderType == "once" ? "📞" : "🔁";
                var person = r.PersonName is not null ? $" ({r.PersonName})" : "";
                sb.AppendLine($"- {icon} {r.Title}{person}");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("**Due today:** Nothing on the calendar.");
        }

        // ── Due tomorrow ──────────────────────────────────────────────────────
        var tomorrowBirthdays     = birthdaysThisWeek.Where(IsBirthdayOn(tomorrow)).ToList();
        var tomorrowAnniversaries = anniversariesThisWeek.Where(IsAnniversaryOn(tomorrow)).ToList();

        if (dueTomorrow.Count > 0 || tomorrowBirthdays.Count > 0 || tomorrowAnniversaries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Tomorrow:**");

            foreach (var c in tomorrowBirthdays)
                sb.AppendLine($"- 🎂 {c.Name}'s birthday");

            foreach (var c in tomorrowAnniversaries)
                sb.AppendLine($"- 💍 {c.Name}'s anniversary");

            foreach (var r in dueTomorrow)
                sb.AppendLine($"- {r.Title}{(r.PersonName is not null ? $" ({r.PersonName})" : "")}");
        }

        // ── This week ─────────────────────────────────────────────────────────
        var weekItems = new List<(DateOnly Date, string Text)>();

        foreach (var c in birthdaysThisWeek.Where(c => !IsBirthdayToday(today)(c) && !IsBirthdayOn(tomorrow)(c)))
        {
            var d = ThisYearOccurrence(c.Birthday!.Value, today);
            weekItems.Add((d, $"🎂 {c.Name}'s birthday"));
        }

        foreach (var c in anniversariesThisWeek.Where(c => !IsAnniversaryToday(today)(c) && !IsAnniversaryOn(tomorrow)(c)))
        {
            var d = ThisYearOccurrence(c.Anniversary!.Value, today);
            weekItems.Add((d, $"💍 {c.Name}'s anniversary"));
        }

        foreach (var r in upcoming)
        {
            if (r.DueDate.HasValue)
                weekItems.Add((r.DueDate.Value, r.Title));
        }

        if (weekItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**This week:**");
            foreach (var (date, text) in weekItems.OrderBy(x => x.Date))
                sb.AppendLine($"- {date:ddd d}: {text}");
        }

        return sb.ToString().TrimEnd();
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static Func<Models.Contact, bool> IsBirthdayToday(DateOnly today) =>
        c => c.Birthday.HasValue && c.Birthday.Value.Month == today.Month && c.Birthday.Value.Day == today.Day;

    private static Func<Models.Contact, bool> IsBirthdayOn(DateOnly date) =>
        c => c.Birthday.HasValue && c.Birthday.Value.Month == date.Month && c.Birthday.Value.Day == date.Day;

    private static Func<Models.Contact, bool> IsAnniversaryToday(DateOnly today) =>
        c => c.Anniversary.HasValue && c.Anniversary.Value.Month == today.Month && c.Anniversary.Value.Day == today.Day;

    private static Func<Models.Contact, bool> IsAnniversaryOn(DateOnly date) =>
        c => c.Anniversary.HasValue && c.Anniversary.Value.Month == date.Month && c.Anniversary.Value.Day == date.Day;

    private static DateOnly ThisYearOccurrence(DateOnly stored, DateOnly today)
    {
        var thisYear = new DateOnly(today.Year, stored.Month, stored.Day);
        return thisYear >= today ? thisYear : thisYear.AddYears(1);
    }
}

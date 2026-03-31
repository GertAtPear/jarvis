using Dapper;
using Mediahost.Agents.Data;
using Eve.Agent.Models;

namespace Eve.Agent.Data.Repositories;

public class ContactRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        name,
        relationship,
        contact_type    AS ContactType,
        company,
        phone_cell      AS PhoneCell,
        phone_work      AS PhoneWork,
        phone_home      AS PhoneHome,
        email_personal  AS EmailPersonal,
        email_work      AS EmailWork,
        address_home    AS AddressHome,
        address_work    AS AddressWork,
        website,
        social_links::text  AS SocialLinksJson,
        extra::text         AS ExtraJson,
        birthday,
        anniversary,
        notes,
        tags::text      AS TagsJson,
        created_at      AS CreatedAt
        """;

    public async Task<IEnumerable<Contact>> GetAllAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<Contact>(
            $"SELECT {SelectColumns} FROM eve_schema.contacts ORDER BY name");
    }

    public async Task<Contact?> SearchByNameAsync(string name)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<Contact>(
            $"SELECT {SelectColumns} FROM eve_schema.contacts WHERE name ILIKE @pattern ORDER BY name LIMIT 1",
            new { pattern = $"%{name}%" });
    }

    public async Task<IEnumerable<Contact>> GetBirthdaysThisWeekAsync()
    {
        await using var conn = db.Create();
        const string sql = """
            SELECT {0}
            FROM eve_schema.contacts
            WHERE birthday IS NOT NULL
              AND (
                make_date(
                    EXTRACT(YEAR FROM CURRENT_DATE)::int,
                    EXTRACT(MONTH FROM birthday)::int,
                    EXTRACT(DAY FROM birthday)::int
                ) BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '7 days'
                OR
                make_date(
                    EXTRACT(YEAR FROM CURRENT_DATE)::int + 1,
                    EXTRACT(MONTH FROM birthday)::int,
                    EXTRACT(DAY FROM birthday)::int
                ) BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '7 days'
              )
            ORDER BY EXTRACT(MONTH FROM birthday), EXTRACT(DAY FROM birthday)
            """;
        return await conn.QueryAsync<Contact>(string.Format(sql, SelectColumns));
    }

    public async Task<IEnumerable<Contact>> GetAnniversariesThisWeekAsync()
    {
        await using var conn = db.Create();
        const string sql = """
            SELECT {0}
            FROM eve_schema.contacts
            WHERE anniversary IS NOT NULL
              AND (
                make_date(
                    EXTRACT(YEAR FROM CURRENT_DATE)::int,
                    EXTRACT(MONTH FROM anniversary)::int,
                    EXTRACT(DAY FROM anniversary)::int
                ) BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '7 days'
                OR
                make_date(
                    EXTRACT(YEAR FROM CURRENT_DATE)::int + 1,
                    EXTRACT(MONTH FROM anniversary)::int,
                    EXTRACT(DAY FROM anniversary)::int
                ) BETWEEN CURRENT_DATE AND CURRENT_DATE + INTERVAL '7 days'
              )
            ORDER BY EXTRACT(MONTH FROM anniversary), EXTRACT(DAY FROM anniversary)
            """;
        return await conn.QueryAsync<Contact>(string.Format(sql, SelectColumns));
    }

    public async Task UpsertAsync(Contact contact)
    {
        await using var conn = db.Create();
        const string sql = """
            INSERT INTO eve_schema.contacts
                (name, relationship, contact_type, company,
                 phone_cell, phone_work, phone_home,
                 email_personal, email_work,
                 address_home, address_work,
                 website, social_links, extra,
                 birthday, anniversary, notes, tags)
            VALUES
                (@Name, @Relationship, @ContactType, @Company,
                 @PhoneCell, @PhoneWork, @PhoneHome,
                 @EmailPersonal, @EmailWork,
                 @AddressHome, @AddressWork,
                 @Website, @SocialLinksJson::jsonb, @ExtraJson::jsonb,
                 @Birthday, @Anniversary, @Notes, @TagsJson::jsonb)
            ON CONFLICT (name) DO UPDATE SET
                relationship   = EXCLUDED.relationship,
                contact_type   = EXCLUDED.contact_type,
                company        = EXCLUDED.company,
                phone_cell     = EXCLUDED.phone_cell,
                phone_work     = EXCLUDED.phone_work,
                phone_home     = EXCLUDED.phone_home,
                email_personal = EXCLUDED.email_personal,
                email_work     = EXCLUDED.email_work,
                address_home   = EXCLUDED.address_home,
                address_work   = EXCLUDED.address_work,
                website        = EXCLUDED.website,
                social_links   = EXCLUDED.social_links,
                extra          = EXCLUDED.extra,
                birthday       = EXCLUDED.birthday,
                anniversary    = EXCLUDED.anniversary,
                notes          = EXCLUDED.notes,
                tags           = EXCLUDED.tags
            """;
        await conn.ExecuteAsync(sql, new
        {
            contact.Name,
            contact.Relationship,
            contact.ContactType,
            contact.Company,
            contact.PhoneCell,
            contact.PhoneWork,
            contact.PhoneHome,
            contact.EmailPersonal,
            contact.EmailWork,
            contact.AddressHome,
            contact.AddressWork,
            contact.Website,
            SocialLinksJson = contact.SocialLinksJson ?? "{}",
            ExtraJson       = contact.ExtraJson       ?? "{}",
            contact.Birthday,
            contact.Anniversary,
            contact.Notes,
            TagsJson        = contact.TagsJson        ?? "null"
        });
    }
}

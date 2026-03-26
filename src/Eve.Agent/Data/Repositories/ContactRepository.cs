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
        birthday,
        anniversary,
        notes,
        tags::text  AS TagsJson,
        created_at  AS CreatedAt
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
        // Construct "this year's birthday" for each contact and check if it falls
        // within the next 7 days (handles year-end boundary by also checking next year)
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
                (name, relationship, birthday, anniversary, notes, tags)
            VALUES
                (@Name, @Relationship, @Birthday, @Anniversary, @Notes, @TagsJson::jsonb)
            ON CONFLICT (id) DO UPDATE SET
                name         = EXCLUDED.name,
                relationship = EXCLUDED.relationship,
                birthday     = EXCLUDED.birthday,
                anniversary  = EXCLUDED.anniversary,
                notes        = EXCLUDED.notes,
                tags         = EXCLUDED.tags
            """;
        await conn.ExecuteAsync(sql, new
        {
            contact.Name,
            contact.Relationship,
            contact.Birthday,
            contact.Anniversary,
            contact.Notes,
            TagsJson = contact.TagsJson ?? "null"
        });
    }
}

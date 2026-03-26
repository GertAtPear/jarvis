using Mediahost.Agents.Data;
using System.Text.Json;
using Andrew.Agent.Models;
using Dapper;

namespace Andrew.Agent.Data.Repositories;

public class NetworkFactRepository(DbConnectionFactory db)
{
    public async Task<NetworkFact?> GetFactAsync(string key)
    {
        await using var conn = db.Create();
        return await conn.QuerySingleOrDefaultAsync<NetworkFact>(
            """
            SELECT id, fact_key AS FactKey, fact_value AS FactValue, checked_at AS CheckedAt
            FROM andrew_schema.network_facts
            WHERE fact_key = @key
            """,
            new { key });
    }

    public async Task SetFactAsync(string key, object value)
    {
        await using var conn = db.Create();
        await conn.ExecuteAsync(
            """
            INSERT INTO andrew_schema.network_facts (fact_key, fact_value, checked_at)
            VALUES (@key, @value::jsonb, NOW())
            ON CONFLICT (fact_key) DO UPDATE SET
                fact_value = EXCLUDED.fact_value,
                checked_at = NOW()
            """,
            new { key, value = JsonSerializer.Serialize(value) });
    }

    public async Task<Dictionary<string, JsonDocument>> GetAllFactsAsync()
    {
        await using var conn = db.Create();
        var rows = await conn.QueryAsync<NetworkFact>(
            "SELECT fact_key AS FactKey, fact_value AS FactValue FROM andrew_schema.network_facts");
        return rows.ToDictionary(r => r.FactKey, r => r.FactValue);
    }
}

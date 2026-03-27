using Dapper;
using Lexi.Agent.Models;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class CveRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<CveAlertRecord>> GetUnacknowledgedAsync(string? severity = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<CveAlertRecord>(
            """
            SELECT id, cve_id, severity, cvss_score, description, affected_software, affected_version,
                   published_at, matched_at, is_acknowledged
            FROM lexi_schema.cve_alerts
            WHERE is_acknowledged = false
              AND (@severity IS NULL OR severity = @severity)
            ORDER BY cvss_score DESC NULLS LAST, matched_at DESC
            """,
            new { severity });
    }

    public async Task UpsertAsync(string cveId, string severity, double? cvssScore, string? description,
        string? affectedSoftware, string? affectedVersion, DateTimeOffset? publishedAt)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO lexi_schema.cve_alerts
                (cve_id, severity, cvss_score, description, affected_software, affected_version, published_at)
            VALUES (@cveId, @severity, @cvssScore, @description, @affectedSoftware, @affectedVersion, @publishedAt)
            ON CONFLICT (cve_id) DO UPDATE SET
                severity          = EXCLUDED.severity,
                cvss_score        = EXCLUDED.cvss_score,
                description       = EXCLUDED.description,
                affected_software = EXCLUDED.affected_software,
                matched_at        = NOW()
            """,
            new { cveId, severity, cvssScore, description, affectedSoftware, affectedVersion, publishedAt });
    }

    public async Task AcknowledgeAsync(Guid id, string? reason = null)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE lexi_schema.cve_alerts SET is_acknowledged = true WHERE id = @id",
            new { id });
    }
}

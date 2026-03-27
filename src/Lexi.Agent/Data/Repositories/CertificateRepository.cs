using Dapper;
using Lexi.Agent.Models;
using Npgsql;

namespace Lexi.Agent.Data.Repositories;

public class CertificateRepository(NpgsqlDataSource db)
{
    public async Task<IEnumerable<TlsCertificateRecord>> GetAllAsync()
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<TlsCertificateRecord>(
            "SELECT id, host, port, subject_cn, issuer, san, valid_from, valid_to, days_remaining, is_valid, checked_at FROM lexi_schema.tls_certificates ORDER BY days_remaining ASC NULLS LAST");
    }

    public async Task<IEnumerable<TlsCertificateRecord>> GetExpiringAsync(int days)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryAsync<TlsCertificateRecord>(
            "SELECT id, host, port, subject_cn, issuer, san, valid_from, valid_to, days_remaining, is_valid, checked_at FROM lexi_schema.tls_certificates WHERE days_remaining <= @days AND is_valid = true ORDER BY days_remaining ASC",
            new { days });
    }

    public async Task UpsertAsync(string host, int port, string? subjectCn, string? issuer,
        string? sanJson, DateTimeOffset? validFrom, DateTimeOffset? validTo, int? daysRemaining, bool isValid)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO lexi_schema.tls_certificates
                (host, port, subject_cn, issuer, san, valid_from, valid_to, days_remaining, is_valid)
            VALUES (@host, @port, @subjectCn, @issuer, @sanJson::jsonb, @validFrom, @validTo, @daysRemaining, @isValid)
            ON CONFLICT (host, port) DO UPDATE SET
                subject_cn     = EXCLUDED.subject_cn,
                issuer         = EXCLUDED.issuer,
                san            = EXCLUDED.san,
                valid_from     = EXCLUDED.valid_from,
                valid_to       = EXCLUDED.valid_to,
                days_remaining = EXCLUDED.days_remaining,
                is_valid       = EXCLUDED.is_valid,
                checked_at     = NOW()
            """,
            new { host, port, subjectCn, issuer, sanJson, validFrom, validTo, daysRemaining, isValid });
    }

    public async Task<TlsCertificateRecord?> GetByHostAsync(string host, int port = 443)
    {
        await using var conn = await db.OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<TlsCertificateRecord>(
            "SELECT id, host, port, subject_cn, issuer, san, valid_from, valid_to, days_remaining, is_valid, checked_at FROM lexi_schema.tls_certificates WHERE host = @host AND port = @port",
            new { host, port });
    }
}

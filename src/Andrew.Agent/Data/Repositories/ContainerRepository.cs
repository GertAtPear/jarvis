using Mediahost.Agents.Data;
using Andrew.Agent.Models;
using Dapper;

namespace Andrew.Agent.Data.Repositories;

public class ContainerRepository(DbConnectionFactory db)
{
    private const string SelectColumns = """
        id,
        server_id           AS ServerId,
        container_id        AS ContainerId,
        name,
        image,
        status,
        ports,
        env_vars            AS EnvVars,
        compose_project     AS ComposeProject,
        compose_service     AS ComposeService,
        cpu_percent         AS CpuPercent,
        mem_mb              AS MemMb,
        created_at_container AS CreatedAtContainer,
        scanned_at          AS ScannedAt
        """;

    public async Task<IEnumerable<ContainerInfo>> GetByServerIdAsync(Guid serverId)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ContainerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.containers WHERE server_id = @serverId ORDER BY name",
            new { serverId });
    }

    public async Task<IEnumerable<ContainerInfo>> SearchByNameAsync(string namePattern)
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ContainerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.containers WHERE name ILIKE @pattern OR image ILIKE @pattern ORDER BY name",
            new { pattern = $"%{namePattern}%" });
    }

    public async Task<IEnumerable<ContainerInfo>> GetRunningAcrossAllServersAsync()
    {
        await using var conn = db.Create();
        return await conn.QueryAsync<ContainerInfo>(
            $"SELECT {SelectColumns} FROM andrew_schema.containers WHERE status ILIKE '%running%' ORDER BY name");
    }

    public async Task BulkUpsertAsync(Guid serverId, IEnumerable<ContainerInfo> containers)
    {
        var list = containers as ICollection<ContainerInfo> ?? containers.ToList();

        await using var conn = db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        if (list.Count > 0)
        {
            var currentIds = list.Select(c => c.ContainerId).ToArray();
            await conn.ExecuteAsync(
                """
                DELETE FROM andrew_schema.containers
                WHERE server_id = @serverId
                  AND NOT (container_id = ANY(@currentIds))
                """,
                new { serverId, currentIds }, tx);

            const string upsertSql = """
                INSERT INTO andrew_schema.containers
                    (server_id, container_id, name, image, status, ports, env_vars,
                     compose_project, compose_service, cpu_percent, mem_mb,
                     created_at_container, scanned_at)
                VALUES
                    (@ServerId, @ContainerId, @Name, @Image, @Status, CAST(@Ports AS jsonb), CAST(@EnvVars AS jsonb),
                     @ComposeProject, @ComposeService, @CpuPercent, @MemMb,
                     @CreatedAtContainer, NOW())
                ON CONFLICT (server_id, container_id) DO UPDATE SET
                    name                 = EXCLUDED.name,
                    image                = EXCLUDED.image,
                    status               = EXCLUDED.status,
                    ports                = EXCLUDED.ports,
                    env_vars             = EXCLUDED.env_vars,
                    compose_project      = EXCLUDED.compose_project,
                    compose_service      = EXCLUDED.compose_service,
                    cpu_percent          = EXCLUDED.cpu_percent,
                    mem_mb               = EXCLUDED.mem_mb,
                    created_at_container = EXCLUDED.created_at_container,
                    scanned_at           = NOW()
                RETURNING id
                """;

            foreach (var container in list)
            {
                container.ServerId = serverId;
                container.Id = await conn.ExecuteScalarAsync<Guid>(upsertSql, container, tx);
            }
        }
        else
        {
            await conn.ExecuteAsync(
                "DELETE FROM andrew_schema.containers WHERE server_id = @serverId",
                new { serverId }, tx);
        }

        await tx.CommitAsync();
    }
}

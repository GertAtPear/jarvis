using Mediahost.Agents.Data;

namespace Rex.Agent.Data;

public class RexMemoryService(DbConnectionFactory db) : AgentMemoryService(db)
{
    protected override string Schema => "rex_schema";
}

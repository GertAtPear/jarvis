using Mediahost.Agents.Data;

namespace Nadia.Agent.Data;

public class NadiaMemoryService(DbConnectionFactory db) : AgentMemoryService(db)
{
    protected override string Schema => "nadia_schema";
}

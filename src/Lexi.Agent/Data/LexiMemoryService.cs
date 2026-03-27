using Mediahost.Agents.Data;

namespace Lexi.Agent.Data;

public class LexiMemoryService(DbConnectionFactory db) : AgentMemoryService(db)
{
    protected override string Schema => "lexi_schema";
}

using Mediahost.Agents.Data;

namespace Andrew.Agent.Data;

public class AndrewMemoryService(DbConnectionFactory db) : AgentMemoryService(db)
{
    protected override string Schema => "andrew_schema";
}

using Mediahost.Agents.Data;

namespace Research.Agent.Data;

public class ResearchMemoryService(DbConnectionFactory db) : AgentMemoryService(db)
{
    protected override string Schema => "research_schema";
}

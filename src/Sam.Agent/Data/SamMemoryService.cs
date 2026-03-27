using Mediahost.Agents.Data;

namespace Sam.Agent.Data;

public class SamMemoryService(DbConnectionFactory db) : AgentMemoryService(db)
{
    protected override string Schema => "sam_schema";
}

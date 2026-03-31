namespace Mediahost.Agents.Services;

/// <summary>
/// Provides the current agent's name to shared tool modules that need
/// to identify themselves (e.g. AgentMessagingModule posts "from_agent").
/// Registered as a singleton by AddAgentInfrastructure.
/// </summary>
public interface IAgentNameProvider
{
    string AgentName { get; }
}

public sealed class AgentNameProvider(string agentName) : IAgentNameProvider
{
    public string AgentName { get; } = agentName;
}

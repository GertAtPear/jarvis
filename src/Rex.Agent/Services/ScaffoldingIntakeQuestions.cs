namespace Rex.Agent.Services;

internal static class ScaffoldingIntakeQuestions
{
    public const string Default = """
        Please answer the following 6 questions to help me scaffold your new agent:

        1. **Agent Name:** What should this agent be called? (PascalCase, e.g. "Benny", "DataSync")

        2. **Department:** Which department does this agent belong to?
           (IT Operations | Development | Projects | Finance | Personal | Research)

        3. **Purpose:** What is this agent's primary responsibility? Describe what it should do in 1-3 sentences.

        4. **Data Sources & Capabilities:** Does the agent need:
           - SSH access to servers? (yes/no)
           - Database read/write access? (yes/no)
           - Scheduled/recurring jobs? (yes/no)
           - Web browsing? (yes/no)
           - Any external APIs or services?

        5. **State:** Should this agent remember conversation history and permanent facts across sessions?
           (stateful = yes, remember things | stateless = each request is independent)

        6. **Routing Keywords:** What words or phrases should cause Jarvis to route messages to this agent?
           (comma-separated list, e.g. "backup, restore, sync, archive")
        """;
}

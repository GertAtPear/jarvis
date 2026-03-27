## Scaffolding Plan: {AgentName}

**Department:** {Department}
**Port:** {Port} (auto-assigned, no conflicts)
**Base class:** {BaseClass}
**Schema:** {SchemaName}

### Files to create
```
src/{AgentName}.Agent/
  {AgentName}.Agent.csproj
  Dockerfile
  Program.cs
  SystemPrompts/{AgentName}SystemPrompt.cs
  Services/{AgentName}AgentService.cs
  Tools/{AgentName}ToolDefinitions.cs
  Tools/{AgentName}ToolExecutor.cs
  Controllers/{AgentName}Controller.cs
```

### Proposed tools

{ToolTable}

### Database schema
```
{SchemaName}
  ├── sessions
  ├── conversations
  └── memory
```

### Docker Compose
Service `{AgentNameLower}-agent` added to docker-compose.yml on port {Port}.

### Routing keywords
{RoutingKeywords}

### Infisical Machine Identity path
`/agents/{AgentNameLower}`

---
Type **approve** to scaffold this agent, or tell me what to change.

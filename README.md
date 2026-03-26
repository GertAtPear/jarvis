# Mediahost AI Platform

A self-hosted, multi-agent AI orchestration platform built on .NET 10. Jarvis acts as the central orchestrator, dynamically routing tasks to a team of specialised agents — each with its own domain, tools, and backing storage — while a Blazor Server UI provides the chat interface.

---

## Architecture Overview

```
User (Browser)
    ↓
Nginx (TLS termination — ai.mediahost.co.za)
    ↓
Jarvis UI  ←──SignalR──→  Jarvis API
                               │
            ┌──────────────────┼──────────────────┐──────────────┐
            ▼                  ▼                  ▼              ▼              ▼
      Andrew.Agent        Rocky.Agent        Eve.Agent    Browser.Agent    Rex.Agent
      (sys admin)        (ops monitor)      (assistant)   (web auto)      (developer)
            │                  │                  │              │              │
        PostgreSQL         PostgreSQL         PostgreSQL     Playwright     PostgreSQL
       andrew_schema      rocky_schema        eve_schema    (Chromium)     rex_schema
```

**Request flow:**

1. Chat message hits Jarvis API
2. LLM classifies the task and selects a model (primary + fallback chain)
3. Dynamic routing determines which agent(s) to invoke based on keywords and routing rules
4. Jarvis calls the target agent's `/api/{agent}/chat` endpoint
5. The agent runs its own agentic loop — calling tools, accumulating results — and returns a final response
6. Response streams back to the UI via SignalR

---

## Components

### Jarvis API (`Jarvis.Api` — port 5000)

Central orchestrator. Manages conversation state, attachment handling (images, documents up to 20 MB), LLM model routing, and agent dispatch. Stores sessions and conversations in PostgreSQL (`jarvis_schema`).

### Jarvis UI (`Jarvis.Ui` — port 5002)

Blazor Server chat interface. Features real-time SignalR updates, file attachments (file picker, drag-drop, clipboard paste), session history sidebar, and markdown rendering.

### Andrew Agent (`Andrew.Agent` — port 5001)

System administration agent. Runs on **host network** so it can reach LAN/VPN servers directly.

- SSH-based server discovery across the network
- Container status, process, port, and website health checks
- Network diagnostics: VPN gateway, DNS, internet connectivity
- Quartz scheduler — discovery every 4 h, network status every 5 min, custom checks on demand
- Read/write SSH access to managed servers
- Tools: `ssh_exec`, `winrm_exec`, `ping_host`, `tcp_probe`, `check_url`, `schedule_check`, and more

### Rocky Agent (`Rocky.Agent` — port 5006)

Read-only pipeline monitoring agent. Runs on **host network** to reach the same LAN servers Andrew manages.

- Watches STT pipelines, Kafka consumer groups, radio capture daemons, web scrapers, and the client delivery API
- Scheduled checks via Quartz (per-service interval, configurable per row in the DB)
- Auto-discovers running containers from `andrew_schema.containers` every 30 min
- Alert lifecycle: raises an alert when a service goes unhealthy, resolves it when it recovers
- Results retention: 48-hour rolling window (cleanup job runs daily at 03:00 SAST)
- **Strictly read-only** — never modifies, restarts, or reconfigures anything
- Tools: `get_service_status`, `list_services`, `get_check_history`, `get_active_alerts`, `run_http_check`, `run_tcp_check`, `run_container_check`, `run_ssh_process_check`

### Eve Agent (`Eve.Agent` — port 5003)

Personal assistant agent.

- Reminders with scheduling
- Contact management
- Morning briefing (delivered on first app open of the day)
- Tools: `set_reminder`, `list_reminders`, `add_contact`, `search_contacts`, and more

### Browser Agent (`Browser.Agent` — port 5004)

Headless Chromium automation via Playwright.

- Navigate, click, fill, extract, screenshot
- Up to 3 concurrent sessions (configurable)
- 2 GB shared memory for browser stability

### Rex Agent (`Rex.Agent` — port 5005)

Developer agent. The workspace directory and the Podman socket are mounted in.

- Git operations: clone, pull, commit, push, branch management
- GitHub API via Octokit: repos, branches, PRs, issues, file updates
- Container orchestration via Podman socket (build, restart, inspect, logs)
- Plans and writes code using temporary developer sub-agents
- Writes tools and services for other platform agents
- Tools: `read_file`, `write_file`, `git_*`, `gh_*`, `container_*`, `plan_task`, `develop_file`, `review_changes`, `create_workflow`, `remember_fact`, and more

---

## Shared Libraries

| Project | Purpose |
|---|---|
| `Mediahost.Shared` | Base models, shared services |
| `Mediahost.Vault` | Infisical secrets management client |
| `Mediahost.Llm` | LLM provider abstraction (Anthropic / Google / Azure OpenAI), model routing, usage logging |
| `Mediahost.Tools` | Raw execution layer — SSH, WinRM, Docker-over-SSH, SQL, HTTP, Ping |
| `Mediahost.Agents` | Agent base infrastructure — capability wrappers, memory, tool modules, base classes |

---

## Agent Memory System

Every agent that extends `BaseAgentService` gets a three-tier memory system backed by PostgreSQL. The memory is scoped per agent (each agent writes to its own schema) and is automatically woven into every request.

### Tier 1 — Conversation history (session memory)

Each conversation is tracked by a `session_id` (GUID). When a message arrives:

1. `EnsureSessionAsync` creates a session row if it does not exist.
2. `LoadHistoryAsync` retrieves the last **20 messages** (10 turns) for that session, ordered by timestamp. Older messages are silently dropped — the sliding window prevents unbounded context growth.
3. Those messages are prepended to the LLM request as `user`/`assistant` turn pairs, giving the model the recent conversation context.
4. After the agent produces its final response, `SaveTurnAsync` appends both the user message and the assistant response to `{schema}.conversations` and updates `last_message_at` on the session.

Each agent owns its own schema tables:

```
{agent}_schema.sessions       — id, title, last_message_at, created_at
{agent}_schema.conversations  — session_id, role, content, created_at
```

Sessions are created and managed by each agent independently. Jarvis maps its own session IDs to agent-side session IDs when routing across agents.

### Tier 2 — Permanent facts (long-term memory)

Facts are global across all sessions for an agent — they persist forever until explicitly deleted.

```
{agent}_schema.memory  — key (unique), value, updated_at
```

Facts are loaded on every request via `LoadFactsAsync` and injected into the system prompt under a `## Permanent Memory` section. The LLM sees them as always-true background knowledge:

```
## Permanent Memory
- github_username: mediahost-gert
- default_server: prod-01.mediahost.co.za
- preferred_stack: dotnet10 + postgresql + dapper
```

Agents expose `remember_fact` and `forget_fact` tools so the LLM can update its own memory mid-conversation. Rex uses this heavily to track repo URLs, GitHub credentials, Docker Hub org names, and architecture decisions across sessions.

### Tier 3 — Agent-specific context (injected per request)

Agents can override `LoadAdditionalContextAsync` to inject dynamic context that changes per request. Eve uses this to inject today's pending reminders into her system prompt on every call, so the model always knows what is due without needing to call a tool first.

### StateLess agents (Rocky)

Rocky extends `AgentBase` instead of `BaseAgentService`. `AgentBase` provides only the raw tool-use loop — no session tracking, no fact storage, no history. Each Rocky request is completely independent. This is intentional: Rocky's job is to read live monitoring data on demand, not to maintain a conversation thread.

---

## Tool Architecture

Tools are the mechanism by which agents interact with the outside world. The platform uses a three-layer tool architecture.

### Layer 1 — Raw execution (`Mediahost.Tools`)

The bottom layer provides stateless, connection-per-call execution clients with no business logic:

| Interface | Implementation | What it does |
|---|---|---|
| `ISshTool` | `SshTool` | Runs a shell command over SSH; connection opened and closed per call |
| `IWinRmTool` | `WinRmTool` | Runs a PowerShell command over WS-Man/NTLM |
| `IDockerTool` | `DockerTool` | Runs `docker ps`, `docker inspect`, `docker stats` via SSH |
| `ISqlTool` | `SqlTool` | Runs parameterised SQL via Npgsql (PostgreSQL) or MySqlConnector |
| `IHttpCheckTool` | `HttpCheckTool` | HTTP health probe via a named `IHttpClientFactory` client |
| `IPingTool` | `PingTool` | ICMP ping and TCP port probe |

All implementations are registered as **Transient** by `AddMediahostTools()`. They never log credentials — only hostname, operation name, duration, and success/failure.

### Layer 2 — Capability wrappers (`Mediahost.Agents`)

The middle layer wraps each raw tool with **per-agent permission enforcement**. An agent declares its permission level when calling a capability; the wrapper blocks any command that exceeds that level before it reaches the execution tool.

| Capability | Permission levels | What gets blocked |
|---|---|---|
| `SshCapability` | `ReadOnly` / `ReadWrite` / `DeployOnly` | ReadOnly blocks: `rm`, `chmod`, `systemctl start/stop/restart`, `docker run/exec/pull`, redirects (`>`, `>>`), `sudo su`, and more |
| `WinRmCapability` | `ReadOnly` / `ReadWrite` | ReadOnly blocks: `Remove-Item`, `Stop/Start/Restart-Service`, `Kill`, `Set-ExecutionPolicy`, `Invoke-Expression`, output redirects |
| `SqlCapability` | `ReadOnly` / `ReadWrite` / `DbaLevel` | ReadOnly: SELECT and WITH only. ReadWrite: no DDL (CREATE/ALTER/DROP/TRUNCATE). All levels: no EXEC, CALL, XP_, SP_ |
| `DockerCapability` | Read-only by design | Pass-through — list, inspect, logs, stats only |
| `HttpCapability` | Read-only by design | Pass-through — HTTP check, ICMP ping, TCP probe |

Capabilities are registered as **Singletons** (stateless, safe to share). They also provide credential helpers — `SshCapability.GetCredentialsAsync(hostname)` fetches SSH credentials from Infisical vault at `/servers/{hostname}` automatically.

Rocky uses `SshCapability` with `ReadOnly` permission everywhere. Andrew uses `ReadWrite` for server management. Neither agent holds credentials directly — they always flow from vault at call time.

### Layer 3 — Tool modules (`IToolModule`) and agent tool executors

The top layer is how tools are composed into specific agents.

**`IToolModule`** is the composability unit:

```csharp
public interface IToolModule
{
    IEnumerable<ToolDefinition> GetDefinitions();
    Task<string?> TryExecuteAsync(string toolName, JsonDocument input, CancellationToken ct);
}
```

Each module implements a cohesive group of related tools. If a module does not handle a given tool name, it returns `null` from `TryExecuteAsync` — the executor tries the next module in the chain.

**Current shared modules** (live in `Mediahost.Agents/Tools/`):

| Module | Tools | Used by |
|---|---|---|
| `NetworkDiagnosticsModule` | `ping_host`, `tcp_probe`, `check_url` | Andrew |
| `RemoteExecModule` | `ssh_exec`, `winrm_exec` | Andrew |

Modules are registered in each agent's DI setup and injected as `IEnumerable<IToolModule>` into the agent's tool executor. Adding a new module to multiple agents requires only adding a single `services.AddScoped<IToolModule, MyModule>()` line per agent — the executor picks it up automatically.

**Agent-specific tools** that are not reusable across agents live inside the agent's own `HandleAgentSpecificAsync` method rather than in a shared module.

### Shared tools in `BaseAgentService`

Every agent that extends `BaseAgentService` automatically receives two additional tools without any registration:

| Tool | Description |
|---|---|
| `web_search` | Delegated to the LLM provider natively (Anthropic / Gemini built-in web search) |
| `fetch_page` | Fetches and strips a URL to plain text, up to 10 000 characters |

These are appended to every agent's tool list on every request. No agent needs to declare them.

---

## Rex: Writing Tools for Other Agents

Rex has direct write access to the platform's own source tree at `/project` (mapped to the repo root) and the Podman socket at `/run/podman/podman.sock`. This means Rex can modify any agent's code, build a new container image, and restart the service — completing the entire tool delivery cycle from a single chat instruction.

### How Rex adds a shared tool module

A **shared module** lives in `Mediahost.Agents/Tools/` and is available to any agent that registers it. Rex follows this workflow (it is encoded in its system prompt):

1. `read_file` the existing module files to understand the pattern.
2. `plan_task` — the temporary developer agent produces a structured implementation plan. Rex presents it to you for approval before writing a single line.
3. `develop_file` the new `IToolModule` implementation in `Mediahost.Agents/Tools/MyModule.cs`.
4. `develop_file` the updated `AgentServiceExtensions.cs` for each target agent to register the module.
5. If the module needs server lookup, `develop_file` an `IServerResolver` implementation (or confirm an existing one covers it).
6. `write_file` all produced files to disk.
7. `git_diff` to see what changed, then `review_changes` to have the developer sub-agent verify correctness.
8. `git_add_commit` with a meaningful message, then `git_push`.
9. `container_build` the affected agent(s), `container_restart`, then `container_logs` to confirm a clean startup.

### How Rex adds an agent-specific tool

An **agent-specific tool** belongs inside one agent's own tool executor. The pattern is:

1. `read_file` the agent's `{Agent}ToolDefinitions.cs` and `{Agent}ToolExecutor.cs`.
2. `plan_task` for approval.
3. `develop_file` the updated definitions file (new `ToolDefinition` entry).
4. `develop_file` the updated executor file (new `case` in `HandleAgentSpecificAsync`).
5. `write_file`, build (`dotnet build`), `container_build`, `container_restart`, `container_logs`.

### Decision: shared module vs agent-specific

| Criterion | Use a shared module | Use agent-specific |
|---|---|---|
| More than one agent needs this tool | Yes | |
| Tool uses agent-specific DB tables or state | | Yes |
| Tool is generic infrastructure (SSH, HTTP, SQL) | Yes | |
| Tool depends on the agent's own resolver or context | | Yes |

---

## Rex: Temporary Developer Sub-Agents

Rex does not write code directly using the LLM that handles its conversation. Instead, it spawns **isolated, single-purpose LLM sessions** for focused coding tasks. These sub-agents use the `"rex-dev"` routing key (resolved to a capable model via the routing matrix — typically Sonnet or above) and have an 8 192-token output limit.

There are three sub-agent modes:

### `plan_task`

Rex sends the task description and optional context files to a developer sub-agent and asks it to produce a structured Markdown implementation plan — files to create, files to modify, key changes per file, and new dependencies. Rex presents this plan to you for approval before writing any code. Nothing is written to disk at this stage.

### `develop_file`

Rex sends a task description, the target filename, and relevant context files (e.g. the existing interfaces, related implementations, the project file). The sub-agent writes the **complete file content** — no markdown fences, no explanation, raw source code ready to be saved. Rex then calls `write_file` to persist it.

Each file is developed in its own isolated sub-agent session, meaning:
- The sub-agent has no conversation history from Rex's session — it only sees what Rex explicitly passes as context.
- Multiple files can be developed with focused context per file rather than accumulating everything into one bloated prompt.
- The sub-agent cannot call tools — it is purely generative. All file I/O and system interactions go through Rex.

### `review_changes`

After Rex writes and commits all files, it runs `git_diff` to capture the diff, then sends that diff plus the original task description to a fresh sub-agent and asks it to verify: (1) does the diff fully implement the task? (2) are there bugs or missed edge cases? The review result is reported back to you before pushing.

### Why isolated sub-agents

- **Context separation** — Rex's conversation history (tool calls, prior messages) is not leaked into the code-writing session, keeping the coding prompt clean and focused.
- **Token efficiency** — each sub-agent gets only what it needs for its specific file, not the full conversation.
- **Independent quality gate** — the review sub-agent has no stake in the implementation; it sees only the diff and the spec, making it a genuine second opinion.
- **No tool bleed** — developer sub-agents have zero tools. They cannot accidentally call `write_file` or `container_restart`. All side effects are gated through Rex.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Frontend | Blazor Server, SignalR |
| Database | PostgreSQL 16 (Dapper ORM) |
| Cache | Redis 7 |
| Secrets | Infisical (self-hosted) |
| LLM Providers | Anthropic Claude, Google Gemini, Azure OpenAI |
| Browser | Microsoft Playwright (Chromium) |
| Scheduler | Quartz.NET |
| Reverse Proxy | Nginx + Certbot (Let's Encrypt) |
| Containers | Podman / Docker Compose |
| Logging | Serilog |

---

## Prerequisites

- Podman (or Docker) with Compose support
- A domain with DNS pointing to the host (for TLS)
- API keys for at least one LLM provider (Anthropic recommended)
- Infisical instance (bundled in the compose file) or an external Infisical server

---

## Getting Started

### 1. Configure environment

```bash
cp .env.example .env
```

Edit `.env` and fill in:

- `DOMAIN` — your primary domain (e.g. `ai.example.com`)
- `VAULT_DOMAIN` — Infisical domain (e.g. `vault.example.com`)
- Database and Redis passwords
- Infisical machine identity credentials
- LLM API keys (Anthropic, OpenAI, Azure, Gemini)
- Agent-specific settings (timezone, discovery interval, etc.)

### 2. Start the stack

```bash
podman compose up -d
```

Services start in dependency order. Health checks ensure downstream services wait for databases and Redis to be ready.

### 3. Initialise Infisical

On first run, browse to your vault domain and complete the Infisical setup wizard. Create a machine identity with Universal Auth and add the credentials to `.env`.

### 4. Access the UI

Browse to `https://<your-domain>` to open the Jarvis chat interface.

---

## Configuration Reference

Key environment variables (see `.env.example` for the full list):

| Variable | Description |
|---|---|
| `DOMAIN` | Primary app domain |
| `VAULT_DOMAIN` | Infisical domain |
| `POSTGRES_PASSWORD` | PostgreSQL password |
| `REDIS_PASSWORD` | Redis password |
| `INFISICAL_CLIENT_ID` | Machine identity client ID |
| `INFISICAL_CLIENT_SECRET` | Machine identity client secret |
| `ANTHROPIC_API_KEY` | Anthropic API key |
| `ANDREW_DISCOVERY_INTERVAL_HOURS` | Server discovery interval (default: `4`) |
| `ANDREW_SSH_PARALLELISM` | Parallel SSH discovery threads (default: `10`) |
| `EVE_TIMEZONE` | Eve timezone (default: `Africa/Johannesburg`) |
| `BROWSER_MAX_CONCURRENT_SESSIONS` | Playwright session limit (default: `3`) |

---

## Database Schemas

Each agent owns its own PostgreSQL schema within the shared `mediahostai` database:

| Schema | Owner | Tables |
|---|---|---|
| `jarvis_schema` | Jarvis API | sessions, conversations, agents, departments, model_routing_rules, vault_grants |
| `andrew_schema` | Andrew Agent | servers, containers, scheduled_checks, check_results, discovery_log, sessions, conversations, memory |
| `rocky_schema` | Rocky Agent | watched_services, check_results, alert_history |
| `eve_schema` | Eve Agent | reminders, contacts, calendar_events, sessions, conversations, memory |
| `rex_schema` | Rex Agent | projects, dev_sessions, sessions, conversations, memory |

Schema initialisation SQL is in [`db/init/`](db/init/). Migrations are numbered sequentially (`001_init.sql`, `002_scheduled_checks.sql`, …).

Rocky reads from `andrew_schema.containers` and `andrew_schema.servers` (cross-schema, read-only) for the `ServiceDiscoverySync` job. It never writes to any schema it does not own.

---

## Security Notes

- PostgreSQL and Redis ports are bound to `127.0.0.1` only — not exposed to the network
- Andrew and Rocky run on host network so they can reach LAN/VPN servers; other agents run on the bridge network
- Both require `NET_RAW` capability for ICMP (ping checks)
- All secrets are managed through Infisical; no credentials are hardcoded
- TLS is enforced via Nginx with modern cipher suites and HSTS headers
- Capability wrappers enforce permission levels at the call site — an agent configured as ReadOnly cannot run destructive commands even if the LLM tries
- SSH/SQL/WinRM credentials are never logged; only hostname, operation, duration, and success/failure appear in logs
- Developer sub-agents (Rex) have zero tools — they cannot access the filesystem or network directly

---

## Operations

See [`scripts/README.md`](scripts/README.md) for daily operations: starting/stopping services, viewing logs, running health checks, and certificate renewal.

---

## Project Structure

```
mediahost-ai/
├── src/
│   ├── Jarvis.Api/          # Orchestrator API
│   ├── Jarvis.Ui/           # Blazor Server UI
│   ├── Andrew.Agent/        # System admin agent (port 5001)
│   ├── Rocky.Agent/         # Pipeline monitor agent (port 5006)
│   ├── Eve.Agent/           # Personal assistant agent (port 5003)
│   ├── Browser.Agent/       # Web automation agent (port 5004)
│   ├── Rex.Agent/           # Developer agent (port 5005)
│   ├── Mediahost.Shared/    # Shared models and services
│   ├── Mediahost.Vault/     # Infisical client library
│   ├── Mediahost.Llm/       # LLM abstraction layer
│   ├── Mediahost.Tools/     # Raw execution tools (SSH, WinRM, Docker, SQL, HTTP, Ping)
│   └── Mediahost.Agents/    # Agent base infrastructure (capabilities, memory, tool modules)
├── db/
│   └── init/                # PostgreSQL schema migrations (run in numeric order)
├── nginx/
│   └── nginx.conf           # Reverse proxy configuration
├── scripts/                 # Operational scripts
├── docker-compose.yml       # Full stack definition
└── .env.example             # Environment variable template
```

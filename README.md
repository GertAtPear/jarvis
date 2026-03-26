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
                ┌──────────────┼──────────────┐
                ▼              ▼              ▼              ▼
          Andrew.Agent    Eve.Agent    Browser.Agent    Rex.Agent
          (sys admin)    (assistant)   (web auto)      (developer)
                │              │              │              │
            PostgreSQL     PostgreSQL     Playwright     PostgreSQL
            (andrew_schema) (eve_schema)   (Chromium)   (rex_schema)
```

**Request flow:**

1. Chat message hits Jarvis API
2. LLM classifies the task and selects a model (primary + fallback chain)
3. Dynamic routing determines which agent(s) to invoke
4. Jarvis loops through up to 10 agentic iterations, calling agent tools as needed
5. Final response streams back to the UI via SignalR

---

## Components

### Jarvis API (`Jarvis.Api` — port 5000)

Central orchestrator. Manages conversation state, attachment handling (images, documents up to 20 MB), LLM routing, and agent dispatch. Stores sessions and conversations in PostgreSQL (`jarvis_schema`).

### Jarvis UI (`Jarvis.Ui` — port 5002)

Blazor Server chat interface. Features real-time SignalR updates, file attachments (file picker, drag-drop, clipboard paste), session history sidebar, and markdown rendering.

### Andrew Agent (`Andrew.Agent` — port 5001)

System administration agent. Runs on **host network** so it can reach LAN/VPN directly.

- SSH-based server discovery across the network
- TCP port monitoring, container status checks, website health checks
- Network checks: VPN gateway, DNS, internet connectivity
- Quartz scheduler for periodic jobs (default: discovery every 4 h, network status every 5 min)
- Custom checks with cron or interval schedules
- Tools: `run_command`, `check_server_status`, `deploy_container`, `schedule_check`, and more

### Eve Agent (`Eve.Agent` — port 5003)

Personal assistant agent.

- Reminders with scheduling
- Contact management
- Morning briefing (delivered on first app open of the day)
- Google Calendar integration
- Tools: `set_reminder`, `list_reminders`, `add_contact`, `search_contacts`, and more

### Browser Agent (`Browser.Agent` — port 5004)

Headless Chromium automation via Playwright.

- Navigate, click, fill, extract, screenshot
- Up to 3 concurrent sessions (configurable)
- Restricted to internal network via Nginx; not publicly accessible
- 2 GB shared memory for browser stability

### Rex Agent (`Rex.Agent` — port 5005)

Developer agent. Workspace and Podman socket are mounted in.

- Git operations: clone, commit, push, branch management
- GitHub integration via Octokit
- Container orchestration via Podman socket
- Project and development session tracking
- Tools: `list_projects`, `git_clone`, `git_commit`, `create_container`, and more

### Shared Libraries

| Project | Purpose |
|---|---|
| `Mediahost.Shared` | Base models and shared services |
| `Mediahost.Vault` | Infisical secrets management client |
| `Mediahost.Llm` | LLM provider abstraction (Anthropic / Google / Azure OpenAI), model selection, usage logging |
| `Mediahost.Agents` | Shared agent infrastructure and base classes |

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
| `jarvis_schema` | Jarvis API | sessions, conversations, attachments, agent registry, LLM models |
| `andrew_schema` | Andrew Agent | servers, scheduled_checks, check_results |
| `eve_schema` | Eve Agent | reminders, contacts, calendar_events |
| `rex_schema` | Rex Agent | projects, dev_sessions |

Schema initialisation SQL is in [`db/init/`](db/init/).

---

## Operations

See [`scripts/README.md`](scripts/README.md) for daily operations: starting/stopping services, viewing logs, running health checks, and certificate renewal.

---

## Security Notes

- PostgreSQL and Redis ports are bound to `127.0.0.1` only — not exposed to the network
- Browser Agent is restricted to internal IPs via Nginx (`/agents/browser` block)
- Andrew Agent requires `NET_RAW` capability for ICMP (ping checks)
- All secrets are managed through Infisical; no credentials are hardcoded
- TLS is enforced via Nginx with modern cipher suites and HSTS headers

---

## Project Structure

```
mediahost-ai/
├── src/
│   ├── Jarvis.Api/          # Orchestrator API
│   ├── Jarvis.Ui/           # Blazor Server UI
│   ├── Andrew.Agent/        # System admin agent
│   ├── Eve.Agent/           # Personal assistant agent
│   ├── Browser.Agent/       # Web automation agent
│   ├── Rex.Agent/           # Developer agent
│   ├── Mediahost.Shared/    # Shared models and services
│   ├── Mediahost.Vault/     # Infisical client library
│   ├── Mediahost.Llm/       # LLM abstraction layer
│   └── Mediahost.Agents/    # Agent base infrastructure
├── db/
│   └── init/                # PostgreSQL schema migrations
├── nginx/
│   └── nginx.conf           # Reverse proxy configuration
├── scripts/                 # Operational scripts
├── docker-compose.yml       # Full stack definition
└── .env.example             # Environment variable template
```

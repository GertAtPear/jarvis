-- ─── Migration 005: Rex — Development Manager Agent ───────────────────────────
--
-- Run manually against the running DB:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/005_rex.sql

-- ─── SCHEMA ───────────────────────────────────────────────────────────────────
CREATE SCHEMA IF NOT EXISTS rex_schema;

-- ─── STANDARD MEMORY TABLES ───────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS rex_schema.sessions (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS rex_schema.conversations (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id  UUID        NOT NULL REFERENCES rex_schema.sessions(id) ON DELETE CASCADE,
    role        VARCHAR(20) NOT NULL,   -- user | assistant
    content     TEXT        NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_rex_conv_session ON rex_schema.conversations(session_id, created_at DESC);

CREATE TABLE IF NOT EXISTS rex_schema.memory (
    key         VARCHAR(200) PRIMARY KEY,
    value       TEXT         NOT NULL,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─── REX-SPECIFIC: PROJECT / REPO TRACKER ─────────────────────────────────────

CREATE TABLE IF NOT EXISTS rex_schema.projects (
    id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(200) NOT NULL UNIQUE,
    repo_url    VARCHAR(500),
    local_path  VARCHAR(500),
    description TEXT,
    language    VARCHAR(50),
    status      VARCHAR(50)  DEFAULT 'active',   -- active | archived | building
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─── REX-SPECIFIC: DEVELOPER SESSION LOG ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS rex_schema.dev_sessions (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id      UUID         REFERENCES rex_schema.sessions(id) ON DELETE SET NULL,
    task_summary    TEXT         NOT NULL,
    plan            TEXT,
    files_changed   JSONB,
    outcome         VARCHAR(50)  DEFAULT 'pending',   -- success | partial | failed | pending
    commit_sha      VARCHAR(100),
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_rex_dev_sessions_session ON rex_schema.dev_sessions(session_id, created_at DESC);

-- ─── AGENT REGISTRATION ───────────────────────────────────────────────────────

INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT
    'rex', 'Rex',
    (SELECT id FROM jarvis_schema.departments WHERE name = 'Development'),
    'Development manager – code changes, GitHub repos, CI/CD pipelines, container builds, temporary developer agents',
    'http://rex-agent:5005',
    '["code_development","github_management","ci_cd","container_management","tool_creation"]'::jsonb,
    '["code","develop","build","github","deploy","commit","push","workflow","pipeline","tool","create project","update app","fix","refactor","branch","pr","pull request","repository","repo"]'::jsonb,
    3
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name = 'rex');

-- ─── LLM ROUTING RULES ────────────────────────────────────────────────────────
--
-- Rex (priority 35-37): main conversational loop — Sonnet primary.
-- rex-dev (priority 38-40): temporary developer agent spawned inside Rex's
--   DeveloperAgentService; uses same LLM infrastructure with a coding prompt.

INSERT INTO jarvis_schema.model_routing_rules
    (rule_name, priority, agent_name, provider_name, model_id, reason)
VALUES
    ('Rex – Sonnet (primary)',           35, 'rex',     'anthropic', 'claude-sonnet-4-6', 'Rex primary: Sonnet for multi-step development tool-use'),
    ('Rex – Gemini Flash (fallback 1)',  36, 'rex',     'google',    'gemini-2.0-flash',  'Rex fallback 1: Gemini 2.0 Flash supports tool-use'),
    ('Rex – GPT-4o (fallback 2)',        37, 'rex',     'openai',    'gpt-4o',            'Rex fallback 2: GPT-4o reliable for code tasks'),
    ('Rex Dev – Sonnet (primary)',       38, 'rex-dev', 'anthropic', 'claude-sonnet-4-6', 'Temp dev agent primary: Sonnet best for code generation'),
    ('Rex Dev – GPT-4o (fallback 1)',    39, 'rex-dev', 'openai',    'gpt-4o',            'Temp dev agent fallback 1: GPT-4o strong coder'),
    ('Rex Dev – Gemini (fallback 2)',    40, 'rex-dev', 'google',    'gemini-2.0-flash',  'Temp dev agent fallback 2: Gemini Flash')
ON CONFLICT DO NOTHING;

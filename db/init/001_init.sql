-- ─── SCHEMA: JARVIS ORCHESTRATOR ────────────────────────────────────────────
CREATE SCHEMA IF NOT EXISTS jarvis_schema;

-- Department registry
CREATE TABLE IF NOT EXISTS jarvis_schema.departments (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(100) NOT NULL UNIQUE,
    description TEXT,
    icon        VARCHAR(50),
    sort_order  INT         DEFAULT 0,
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
INSERT INTO jarvis_schema.departments (name, description, icon, sort_order) VALUES
  ('IT Operations', 'Infrastructure, servers, monitoring',   '🖥️',  1),
  ('Development',   'Software development and repositories', '💻',  2),
  ('Projects',      'Project and task management',           '📋',  3),
  ('Finance',       'Budget, suppliers, contracts',          '💰',  4),
  ('Personal',      'Personal reminders and assistant',      '🤖',  5),
  ('Research',      'Research and information gathering',    '🔍',  6)
ON CONFLICT (name) DO NOTHING;

-- Agent registry — every agent is a row, not hardcoded
CREATE TABLE IF NOT EXISTS jarvis_schema.agents (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(100) NOT NULL UNIQUE,
    display_name     VARCHAR(100) NOT NULL,
    department_id    UUID        REFERENCES jarvis_schema.departments(id),
    description      TEXT        NOT NULL,
    system_prompt    TEXT,
    base_url         VARCHAR(500),
    health_path      VARCHAR(200) DEFAULT '/health',
    status           VARCHAR(20) DEFAULT 'active',   -- active|inactive|building
    capabilities     JSONB,
    routing_keywords JSONB,
    sort_order       INT         DEFAULT 0,
    created_at       TIMESTAMPTZ DEFAULT NOW(),
    updated_at       TIMESTAMPTZ DEFAULT NOW()
);
INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT 'andrew','Andrew',
    (SELECT id FROM jarvis_schema.departments WHERE name='IT Operations'),
    'Sysadmin – servers, containers, apps, network',
    'http://andrew-agent:5001',
    '["ssh","docker","network"]'::jsonb,
    '["server","container","docker","running","port","process","vpn","network","disk","cpu"]'::jsonb, 1
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name='andrew');

INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT 'eve','Eve',
    (SELECT id FROM jarvis_schema.departments WHERE name='Personal'),
    'Personal assistant – reminders, birthdays, anniversaries, morning briefing',
    'http://eve-agent:5003',
    '["reminders","calendar","contacts"]'::jsonb,
    '["remind","remember","birthday","anniversary","call","note","follow up","congratulate","personal"]'::jsonb, 2
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name='eve');

-- Conversation history
CREATE TABLE IF NOT EXISTS jarvis_schema.conversations (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id  UUID        NOT NULL,
    role        VARCHAR(20) NOT NULL,       -- user|assistant|agent|tool
    agent_name  VARCHAR(50),
    content     TEXT        NOT NULL,
    tool_calls  JSONB,
    department  VARCHAR(100),
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON jarvis_schema.conversations(session_id);
CREATE INDEX ON jarvis_schema.conversations(created_at DESC);

-- Session list for sidebar
CREATE TABLE IF NOT EXISTS jarvis_schema.sessions (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    title           VARCHAR(500),
    department      VARCHAR(100),
    message_count   INT         DEFAULT 0,
    last_message_at TIMESTAMPTZ DEFAULT NOW(),
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

-- File/image attachments uploaded in chat
CREATE TABLE IF NOT EXISTS jarvis_schema.attachments (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID        REFERENCES jarvis_schema.conversations(id),
    file_name       VARCHAR(500) NOT NULL,
    mime_type       VARCHAR(100) NOT NULL,
    file_size_bytes BIGINT,
    storage_path    TEXT,
    base64_preview  TEXT,       -- small thumbnail for images
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS jarvis_schema.agent_tasks (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID        REFERENCES jarvis_schema.conversations(id),
    agent_name      VARCHAR(50) NOT NULL,
    task_desc       TEXT        NOT NULL,
    status          VARCHAR(20) DEFAULT 'pending',
    result          JSONB,
    started_at      TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

-- Morning briefing delivery log (one per day)
CREATE TABLE IF NOT EXISTS jarvis_schema.briefing_log (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    briefing_date   DATE        NOT NULL UNIQUE,
    content         TEXT        NOT NULL,
    delivered_at    TIMESTAMPTZ DEFAULT NOW()
);

-- ─── LLM PROVIDERS & MODEL REGISTRY ─────────────────────────────────────────
-- All providers and models are rows — add new ones without code changes

CREATE TABLE IF NOT EXISTS jarvis_schema.llm_providers (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(50) NOT NULL UNIQUE,   -- anthropic|google|openai
    display_name    VARCHAR(100) NOT NULL,
    api_base_url    VARCHAR(500),
    vault_secret_path VARCHAR(200) NOT NULL,       -- /apis/anthropic, /apis/google-ai etc.
    vault_key       VARCHAR(100) DEFAULT 'api_key',
    is_active       BOOLEAN     DEFAULT true,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
INSERT INTO jarvis_schema.llm_providers
    (name, display_name, vault_secret_path) VALUES
    ('anthropic', 'Anthropic',  '/apis/anthropic'),
    ('google',    'Google AI',  '/apis/google-ai'),
    ('openai',    'OpenAI',     '/apis/openai')
ON CONFLICT (name) DO NOTHING;

CREATE TABLE IF NOT EXISTS jarvis_schema.llm_models (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_id         UUID        REFERENCES jarvis_schema.llm_providers(id),
    model_id            VARCHAR(200) NOT NULL,     -- "claude-sonnet-4-6", "gemini-1.5-pro"
    display_name        VARCHAR(200) NOT NULL,
    context_window      INT         NOT NULL,       -- max input tokens
    max_output_tokens   INT         NOT NULL,
    supports_vision     BOOLEAN     DEFAULT false,
    supports_tool_use   BOOLEAN     DEFAULT true,
    supports_long_ctx   BOOLEAN     DEFAULT false,  -- true if > 100k tokens
    cost_tier           VARCHAR(20) DEFAULT 'medium', -- cheap|medium|expensive
    is_active           BOOLEAN     DEFAULT true,
    notes               TEXT,
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(provider_id, model_id)
);
-- Seed models (update as providers release new versions)
INSERT INTO jarvis_schema.llm_models
    (provider_id, model_id, display_name, context_window, max_output_tokens,
     supports_vision, supports_tool_use, supports_long_ctx, cost_tier)
SELECT p.id, m.model_id, m.display_name, m.ctx, m.out,
       m.vision, m.tools, m.long_ctx, m.tier
FROM jarvis_schema.llm_providers p
JOIN (VALUES
    ('anthropic', 'claude-opus-4-6',     'Claude Opus 4.6',      200000, 32000, true,  true,  true,  'expensive'),
    ('anthropic', 'claude-sonnet-4-6',   'Claude Sonnet 4.6',    200000, 16000, true,  true,  true,  'medium'),
    ('anthropic', 'claude-haiku-4-5',    'Claude Haiku 4.5',     200000,  8000, true,  true,  false, 'cheap'),
    ('google',    'gemini-1.5-pro',      'Gemini 1.5 Pro',      2000000, 16000, true,  true,  true,  'medium'),
    ('google',    'gemini-1.5-flash',    'Gemini 1.5 Flash',    1000000,  8000, true,  true,  true,  'cheap'),
    ('google',    'gemini-2.0-flash',    'Gemini 2.0 Flash',    1000000,  8000, true,  true,  true,  'cheap'),
    ('openai',    'gpt-4o',              'GPT-4o',               128000,  4096, true,  true,  false, 'medium'),
    ('openai',    'gpt-4o-mini',         'GPT-4o Mini',          128000,  4096, true,  true,  false, 'cheap'),
    ('openai',    'o1-preview',          'o1 Preview',           128000, 32768, false, false, false, 'expensive')
) AS m(provider, model_id, display_name, ctx, out, vision, tools, long_ctx, tier)
ON p.name = m.provider
ON CONFLICT (provider_id, model_id) DO NOTHING;

-- Model routing rules — task classification → model selection
-- Evaluated top-to-bottom, first match wins. Editable without redeploying.
CREATE TABLE IF NOT EXISTS jarvis_schema.model_routing_rules (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_name       VARCHAR(200) NOT NULL,
    priority        INT         NOT NULL,           -- lower = evaluated first
    -- Conditions (NULL = "any")
    needs_vision    BOOLEAN,
    needs_long_ctx  BOOLEAN,
    complexity      VARCHAR(20),                    -- simple|moderate|complex
    task_type       VARCHAR(50),                    -- code|analysis|writing|lookup|briefing
    agent_name      VARCHAR(100),                   -- restrict to specific agent
    -- Selection
    provider_name   VARCHAR(50) NOT NULL,
    model_id        VARCHAR(200) NOT NULL,
    reason          TEXT,
    is_active       BOOLEAN     DEFAULT true,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
INSERT INTO jarvis_schema.model_routing_rules
    (rule_name, priority, needs_vision, needs_long_ctx, complexity, task_type, agent_name, provider_name, model_id, reason)
VALUES
    ('Vision tasks – Sonnet',         10,  true,  null,  null,       null,       null,     'anthropic', 'claude-sonnet-4-6',  'Vision required, Sonnet most reliable'),
    ('Long context – Gemini Pro',      20,  null,  true,  null,       null,       null,     'google',    'gemini-1.5-pro',     'Gemini 2M context window best for large docs'),
    ('Complex code – Sonnet',          30,  null,  null,  'complex',  'code',     null,     'anthropic', 'claude-sonnet-4-6',  'Sonnet strongest for complex code'),
    ('Complex analysis – Opus',        40,  null,  null,  'complex',  'analysis', null,     'anthropic', 'claude-opus-4-6',    'Opus for deep reasoning'),
    ('Complex writing – Opus',         50,  null,  null,  'complex',  'writing',  null,     'anthropic', 'claude-opus-4-6',    'Opus best for long-form writing'),
    ('Eve simple lookups – Haiku',     60,  null,  null,  'simple',   null,       'eve',    'anthropic', 'claude-haiku-4-5',   'Reminders and lookups need no heavy model'),
    ('Eve briefing – Haiku',           70,  null,  null,  null,       'briefing', 'eve',    'anthropic', 'claude-haiku-4-5',   'Briefing compilation is assembly, not reasoning'),
    ('Simple lookups – Haiku',         80,  null,  null,  'simple',   'lookup',   null,     'anthropic', 'claude-haiku-4-5',   'Fast cheap model for simple queries'),
    ('Moderate tasks – Sonnet',        90,  null,  null,  'moderate', null,       null,     'anthropic', 'claude-sonnet-4-6',  'Sonnet default for moderate tasks'),
    ('Default – Sonnet',              999,  null,  null,  null,       null,       null,     'anthropic', 'claude-sonnet-4-6',  'Safe default')
ON CONFLICT DO NOTHING;

-- LLM usage log — every API call logged for cost tracking
CREATE TABLE IF NOT EXISTS jarvis_schema.llm_usage (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id      UUID,
    agent_name      VARCHAR(100),
    provider_name   VARCHAR(50) NOT NULL,
    model_id        VARCHAR(200) NOT NULL,
    task_type       VARCHAR(50),
    input_tokens    INT         NOT NULL DEFAULT 0,
    output_tokens   INT         NOT NULL DEFAULT 0,
    duration_ms     INT,
    rule_applied    VARCHAR(200),           -- which routing rule was used
    escalated_from  VARCHAR(200),           -- Phase 3: if escalated, original model
    created_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON jarvis_schema.llm_usage(created_at DESC);
CREATE INDEX ON jarvis_schema.llm_usage(agent_name, created_at DESC);
CREATE INDEX ON jarvis_schema.llm_usage(provider_name, model_id);

-- ─── SCHEMA: ANDREW SYSADMIN AGENT ──────────────────────────────────────────
CREATE SCHEMA IF NOT EXISTS andrew_schema;

CREATE TABLE IF NOT EXISTS andrew_schema.servers (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    hostname        VARCHAR(255) NOT NULL UNIQUE,
    ip_address      INET        NOT NULL,
    ssh_port        INT         DEFAULT 22,
    os_name         VARCHAR(100),
    os_version      VARCHAR(100),
    cpu_cores       INT,
    ram_gb          DECIMAL(10,2),
    disk_total_gb   DECIMAL(10,2),
    disk_used_gb    DECIMAL(10,2),
    status          VARCHAR(30) DEFAULT 'unknown',
    vault_secret_path VARCHAR(500),
    last_scanned_at TIMESTAMPTZ,
    last_seen_at    TIMESTAMPTZ,
    notes           TEXT,
    tags            JSONB,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS andrew_schema.containers (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id           UUID        REFERENCES andrew_schema.servers(id) ON DELETE CASCADE,
    container_id        VARCHAR(100) NOT NULL,
    name                VARCHAR(255) NOT NULL,
    image               VARCHAR(500),
    status              VARCHAR(50),
    ports               JSONB,
    env_vars            JSONB,      -- passwords/keys stripped, replaced with ***
    compose_project     VARCHAR(255),
    compose_service     VARCHAR(255),
    cpu_percent         DECIMAL(5,2),
    mem_mb              DECIMAL(10,2),
    created_at_container TIMESTAMPTZ,
    scanned_at          TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(server_id, container_id)
);
CREATE INDEX ON andrew_schema.containers(server_id);
CREATE INDEX ON andrew_schema.containers(status);

CREATE TABLE IF NOT EXISTS andrew_schema.processes (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id   UUID        REFERENCES andrew_schema.servers(id) ON DELETE CASCADE,
    pid         INT,
    name        VARCHAR(255),
    command     TEXT,
    status      VARCHAR(50),
    cpu_percent DECIMAL(5,2),
    mem_mb      DECIMAL(10,2),
    user_name   VARCHAR(100),
    scanned_at  TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON andrew_schema.processes(server_id);

CREATE TABLE IF NOT EXISTS andrew_schema.applications (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(255) NOT NULL,
    server_id           UUID        REFERENCES andrew_schema.servers(id),
    container_id        UUID        REFERENCES andrew_schema.containers(id),
    app_type            VARCHAR(100),   -- webapi|worker|webapp|database|queue|scheduler
    framework           VARCHAR(100),   -- dotnet|node|java|python|etc
    port                INT,
    config_path         TEXT,
    git_repo_url        TEXT,
    health_check_url    TEXT,
    notes               TEXT,
    last_seen_running_at TIMESTAMPTZ,
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    updated_at          TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON andrew_schema.applications(server_id);

CREATE TABLE IF NOT EXISTS andrew_schema.network_facts (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    fact_key    VARCHAR(100) UNIQUE NOT NULL,
    fact_value  JSONB       NOT NULL,
    checked_at  TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS andrew_schema.discovery_log (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id       UUID        REFERENCES andrew_schema.servers(id),
    discovery_type  VARCHAR(50),
    status          VARCHAR(20),
    details         JSONB,
    duration_ms     INT,
    ran_at          TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON andrew_schema.discovery_log(server_id, ran_at DESC);

-- ─── SCHEMA: EVE PERSONAL ASSISTANT ─────────────────────────────────────────
CREATE SCHEMA IF NOT EXISTS eve_schema;

CREATE TABLE IF NOT EXISTS eve_schema.reminders (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    title           VARCHAR(500) NOT NULL,
    description     TEXT,
    reminder_type   VARCHAR(50) DEFAULT 'once',    -- once|yearly|monthly|weekly
    due_date        DATE,
    due_time        TIME,
    recur_month     INT,        -- for yearly: which month (1-12)
    recur_day       INT,        -- for yearly/monthly: which day
    person_name     VARCHAR(200),                  -- who this is about
    tags            JSONB,                         -- ["birthday","work","personal"]
    status          VARCHAR(20) DEFAULT 'active',  -- active|snoozed|done|dismissed
    last_triggered  TIMESTAMPTZ,
    snooze_until    DATE,
    calendar_event_id VARCHAR(500),                -- Google Calendar event ID if synced
    notes           TEXT,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON eve_schema.reminders(due_date);
CREATE INDEX ON eve_schema.reminders(status, reminder_type);

CREATE TABLE IF NOT EXISTS eve_schema.contacts (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(200) NOT NULL,
    relationship    VARCHAR(100),                  -- colleague|friend|family|supplier
    birthday        DATE,
    anniversary     DATE,
    notes           TEXT,
    tags            JSONB,
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS eve_schema.notes (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    title           VARCHAR(500),
    content         TEXT        NOT NULL,
    tags            JSONB,
    created_at      TIMESTAMPTZ DEFAULT NOW(),
    updated_at      TIMESTAMPTZ DEFAULT NOW()
);

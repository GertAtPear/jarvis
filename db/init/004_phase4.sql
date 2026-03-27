-- ─── PHASE 4: Rex Scaffolding · Alert Channels · Multi-User Auth ──────────────

-- ─── REX SCHEMA ───────────────────────────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS rex_schema;

-- Port registry — conflict-free port assignment for all agents
CREATE TABLE IF NOT EXISTS rex_schema.port_registry (
    port            INT          PRIMARY KEY,
    agent_name      VARCHAR(100) NOT NULL,
    reserved_at     TIMESTAMPTZ  DEFAULT NOW(),
    is_active       BOOLEAN      DEFAULT true,
    notes           TEXT
);

-- Seed: all existing agents (ports 5000–5009 reserved for Phase 1–3)
INSERT INTO rex_schema.port_registry (port, agent_name, notes) VALUES
    (5000, 'Jarvis.Api', 'Phase 1 — orchestrator API'),
    (5001, 'Andrew',     'Phase 1 — sysadmin agent'),
    (5002, 'Jarvis.Ui',  'Phase 1 — Blazor UI'),
    (5003, 'Eve',        'Phase 1 — personal assistant'),
    (5004, 'Browser',    'Phase 1 — Playwright automation'),
    (5005, 'Rex',        'Phase 1 — developer agent'),
    (5006, 'Rocky',      'Phase 2 — pipeline monitor'),
    (5007, 'Sam',        'Phase 3 — DBA agent'),
    (5008, 'Nadia',      'Phase 3 — network agent'),
    (5009, 'Lexi',       'Phase 3 — security agent')
ON CONFLICT (port) DO NOTHING;

-- Scaffolding sessions — intake state persisted across conversation turns
CREATE TABLE IF NOT EXISTS rex_schema.scaffolding_sessions (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_name          VARCHAR(100),
    department          VARCHAR(100),
    description         TEXT,
    intake_answers      JSONB,
    proposed_tools      JSONB,
    assigned_port       INT          REFERENCES rex_schema.port_registry(port),
    plan_presented_at   TIMESTAMPTZ,
    approved_at         TIMESTAMPTZ,
    status              VARCHAR(50)  DEFAULT 'intake',
        -- intake | planning | approved | scaffolding | complete | failed
    created_at          TIMESTAMPTZ  DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  DEFAULT NOW()
);

-- Audit log — every scaffolding attempt recorded
CREATE TABLE IF NOT EXISTS rex_schema.scaffolded_agents (
    id                      UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id              UUID         REFERENCES rex_schema.scaffolding_sessions(id),
    agent_name              VARCHAR(100) NOT NULL,
    port                    INT,
    department              VARCHAR(100),
    files_created           JSONB,
    schema_created          BOOLEAN      DEFAULT false,
    compose_patched         BOOLEAN      DEFAULT false,
    build_success           BOOLEAN      DEFAULT false,
    health_check_passed     BOOLEAN      DEFAULT false,
    registered_in_jarvis    BOOLEAN      DEFAULT false,
    smoke_test_response     TEXT,
    error_details           TEXT,
    scaffolded_at           TIMESTAMPTZ  DEFAULT NOW()
);

-- Lifecycle audit — metadata updates, code updates, retires, reactivations
CREATE TABLE IF NOT EXISTS rex_schema.agent_updates (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_name      VARCHAR(100) NOT NULL,
    operation       VARCHAR(50)  NOT NULL,
        -- metadata_update | code_update | soft_retire | hard_retire | reactivate
    was_scaffolded  BOOLEAN      NOT NULL,
    description     TEXT,
    files_modified  JSONB,
    schema_dropped  BOOLEAN,
    performed_by    VARCHAR(100),
    performed_at    TIMESTAMPTZ  DEFAULT NOW(),
    success         BOOLEAN      DEFAULT true,
    error_details   TEXT
);
CREATE INDEX ON rex_schema.agent_updates(agent_name, performed_at DESC);


-- ─── JARVIS SCHEMA — AGENT LIFECYCLE COLUMNS ──────────────────────────────────

ALTER TABLE jarvis_schema.agents
    ADD COLUMN IF NOT EXISTS system_prompt_override TEXT,
    ADD COLUMN IF NOT EXISTS display_name           VARCHAR(255),
    ADD COLUMN IF NOT EXISTS retired_at             TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS was_scaffolded         BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS notes                  TEXT;

-- Mark all Phase 1–3 hand-built agents
UPDATE jarvis_schema.agents
SET was_scaffolded = false
WHERE name IN ('Andrew', 'Eve', 'Rex', 'Rocky', 'Sam', 'Nadia', 'Lexi', 'Browser');


-- ─── ALERT CHANNELS ───────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.alert_channels (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    channel_name        VARCHAR(100) NOT NULL UNIQUE,
    channel_type        VARCHAR(20)  NOT NULL,   -- slack | email
    config              JSONB        NOT NULL,
        -- Slack:  { "webhook_url_secret": "/alerts/slack-ops-webhook" }
        -- Email:  { "to": ["ops@mediahost.co.za"], "smtp_secret": "/smtp" }
    min_severity        VARCHAR(20)  DEFAULT 'high',  -- low | medium | high | critical
    agent_filter        VARCHAR(100)[],               -- NULL = all agents
    alert_type_filter   VARCHAR(100)[],               -- NULL = all alert types
    is_active           BOOLEAN      DEFAULT true,
    created_at          TIMESTAMPTZ  DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS jarvis_schema.alert_dispatch_log (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_id        UUID         NOT NULL,
    agent_name      VARCHAR(50)  NOT NULL,
    alert_type      VARCHAR(100) NOT NULL,
    severity        VARCHAR(20)  NOT NULL,
    channel_id      UUID         REFERENCES jarvis_schema.alert_channels(id),
    channel_type    VARCHAR(20)  NOT NULL,
    delivered       BOOLEAN      DEFAULT false,
    error_message   TEXT,
    dispatched_at   TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX ON jarvis_schema.alert_dispatch_log(alert_id);
CREATE INDEX ON jarvis_schema.alert_dispatch_log(dispatched_at DESC);


-- ─── MULTI-USER AUTH ──────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.users (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    username        VARCHAR(100) NOT NULL UNIQUE,
    display_name    VARCHAR(255) NOT NULL,
    email           VARCHAR(255) UNIQUE,
    password_hash   VARCHAR(255) NOT NULL,   -- bcrypt, cost 12
    is_active       BOOLEAN      DEFAULT true,
    last_login_at   TIMESTAMPTZ,
    created_at      TIMESTAMPTZ  DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS jarvis_schema.roles (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    role_name   VARCHAR(50) NOT NULL UNIQUE  -- admin | operator | developer | user
);

CREATE TABLE IF NOT EXISTS jarvis_schema.user_roles (
    user_id     UUID REFERENCES jarvis_schema.users(id) ON DELETE CASCADE,
    role_id     UUID REFERENCES jarvis_schema.roles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE IF NOT EXISTS jarvis_schema.role_agent_access (
    role_id     UUID REFERENCES jarvis_schema.roles(id) ON DELETE CASCADE,
    agent_name  VARCHAR(100) NOT NULL,
    PRIMARY KEY (role_id, agent_name)
);

CREATE TABLE IF NOT EXISTS jarvis_schema.user_sessions (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id         UUID         REFERENCES jarvis_schema.users(id),
    token_hash      VARCHAR(255) NOT NULL,   -- SHA-256 of the bearer token
    expires_at      TIMESTAMPTZ  NOT NULL,
    created_at      TIMESTAMPTZ  DEFAULT NOW(),
    last_used_at    TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX ON jarvis_schema.user_sessions(token_hash);
CREATE INDEX ON jarvis_schema.user_sessions(expires_at);

-- Seed roles
INSERT INTO jarvis_schema.roles (role_name) VALUES
    ('admin'), ('operator'), ('developer'), ('user')
ON CONFLICT (role_name) DO NOTHING;

-- Default role-agent access
-- Admin: all agents (enforced in code — admin bypasses filter, no rows needed)

-- Operator: IT Ops agents
INSERT INTO jarvis_schema.role_agent_access (role_id, agent_name)
SELECT r.id, a.name
FROM jarvis_schema.roles r
CROSS JOIN (VALUES ('Andrew'), ('Rocky'), ('Sam'), ('Nadia'), ('Lexi')) a(name)
WHERE r.role_name = 'operator'
ON CONFLICT DO NOTHING;

-- Developer: IT Ops + Rex
INSERT INTO jarvis_schema.role_agent_access (role_id, agent_name)
SELECT r.id, a.name
FROM jarvis_schema.roles r
CROSS JOIN (VALUES ('Andrew'), ('Rocky'), ('Sam'), ('Nadia'), ('Lexi'), ('Rex')) a(name)
WHERE r.role_name = 'developer'
ON CONFLICT DO NOTHING;

-- NOTE: Admin user (gert) is created by scripts/setup.sh at first run.
-- The script generates a random password, hashes it with bcrypt (cost 12),
-- inserts the user, assigns the admin role, and prints the password once to stdout.
-- Do NOT insert a hardcoded password hash here.

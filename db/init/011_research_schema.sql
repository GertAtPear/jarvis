-- ============================================================
-- Research Agent Schema
-- Session history, memory, and registered business databases
-- for the Research Agent's read-only data analysis work.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS research_schema;

-- Agent session tracking
CREATE TABLE IF NOT EXISTS research_schema.sessions (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at      TIMESTAMPTZ NOT NULL    DEFAULT NOW(),
    last_message_at TIMESTAMPTZ
);

-- Conversation history
CREATE TABLE IF NOT EXISTS research_schema.conversations (
    id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID        NOT NULL REFERENCES research_schema.sessions(id) ON DELETE CASCADE,
    role       VARCHAR(20) NOT NULL,    -- user | assistant
    content    TEXT        NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_research_conversations_session
    ON research_schema.conversations(session_id, created_at DESC);

-- Long-term memory (research findings, data source notes)
CREATE TABLE IF NOT EXISTS research_schema.memory (
    key        VARCHAR(200) PRIMARY KEY,
    value      TEXT         NOT NULL,
    updated_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- Business databases the Research Agent has read-only access to
CREATE TABLE IF NOT EXISTS research_schema.databases (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(100) UNIQUE NOT NULL,   -- friendly name e.g. "broadcast_db"
    display_name     VARCHAR(200),
    db_type          VARCHAR(20)  NOT NULL DEFAULT 'postgresql',  -- postgresql | mysql | mariadb
    host             VARCHAR(255) NOT NULL,
    port             INT          NOT NULL DEFAULT 5432,
    db_name          VARCHAR(100) NOT NULL,
    vault_secret_path VARCHAR(300),     -- path in Infisical vault for credentials
    description      TEXT,
    is_active        BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE research_schema.databases IS
    'Business databases the Research Agent can query read-only. '
    'Credentials stored in vault at vault_secret_path.';

-- ── Agent registration ────────────────────────────────────────────────────────
INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, health_path, capabilities, routing_keywords, sort_order)
SELECT
    'research',
    'Research',
    (SELECT id FROM jarvis_schema.departments WHERE name = 'Research'),
    'Answers business questions by querying internal databases and researching external sources.',
    'http://localhost:5011',
    '/health',
    '["sql","browser","workspace"]'::jsonb,
    '["research","analyze","analyse","report","query","data","find","investigate","statistics","trend","how many","how much","top","ranking","compare","benchmark"]'::jsonb,
    10
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name = 'research');

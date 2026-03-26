-- ─── AGENT MEMORY: persistent conversation history per agent ─────────────────
-- Each agent gets sessions + conversations tables so memory survives restarts.
-- Only plain user/assistant text turns are stored (tool call internals are
-- ephemeral within a turn and do not need cross-session persistence).

-- ─── ANDREW ───────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS andrew_schema.sessions (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS andrew_schema.conversations (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id  UUID        NOT NULL REFERENCES andrew_schema.sessions(id) ON DELETE CASCADE,
    role        VARCHAR(20) NOT NULL,   -- user | assistant
    content     TEXT        NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_andrew_conv_session ON andrew_schema.conversations(session_id, created_at DESC);

-- ─── EVE ──────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS eve_schema.sessions (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS eve_schema.conversations (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id  UUID        NOT NULL REFERENCES eve_schema.sessions(id) ON DELETE CASCADE,
    role        VARCHAR(20) NOT NULL,
    content     TEXT        NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_eve_conv_session ON eve_schema.conversations(session_id, created_at DESC);

-- ─── PERMANENT MEMORY (key-value facts that survive restarts) ─────────────────

CREATE TABLE IF NOT EXISTS andrew_schema.memory (
    key         VARCHAR(200) PRIMARY KEY,
    value       TEXT         NOT NULL,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS eve_schema.memory (
    key         VARCHAR(200) PRIMARY KEY,
    value       TEXT         NOT NULL,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ─── TEMPLATE: add a block like this for each new agent ───────────────────────
--
-- Replace {AGENT} with the lowercase agent name (e.g. sam, rex).
-- Also create the agent's schema first (e.g. in 001_schemas.sql or a new migration).
--
-- CREATE TABLE IF NOT EXISTS {AGENT}_schema.sessions (
--     id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
--     created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
--     last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
-- );
--
-- CREATE TABLE IF NOT EXISTS {AGENT}_schema.conversations (
--     id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
--     session_id  UUID        NOT NULL REFERENCES {AGENT}_schema.sessions(id) ON DELETE CASCADE,
--     role        VARCHAR(20) NOT NULL,
--     content     TEXT        NOT NULL,
--     created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
-- );
-- CREATE INDEX IF NOT EXISTS idx_{AGENT}_conv_session
--     ON {AGENT}_schema.conversations(session_id, created_at DESC);
--
-- CREATE TABLE IF NOT EXISTS {AGENT}_schema.memory (
--     key         VARCHAR(200) PRIMARY KEY,
--     value       TEXT         NOT NULL,
--     updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW()
-- );

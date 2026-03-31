-- ─── Migration 008: Agent Message Bus ────────────────────────────────────────
--
-- Enables agents to communicate with each other through a shared message bus.
-- All messages are visible to Jarvis and surfaced in the UI as an activity feed.
-- Messages marked requires_approval = true prompt the user in the UI before
-- the target agent can act on them.
--
-- Run manually:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/008_agent_messaging.sql

-- ─── TABLE: agent_messages ────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.agent_messages (
    id                BIGSERIAL    PRIMARY KEY,
    from_agent        VARCHAR(50)  NOT NULL,
    to_agent          VARCHAR(50),                 -- NULL = broadcast to all agents
    message           TEXT         NOT NULL,
    thread_id         BIGINT       REFERENCES jarvis_schema.agent_messages(id) ON DELETE SET NULL,
    requires_approval BOOLEAN      NOT NULL DEFAULT FALSE,
    approved_at       TIMESTAMPTZ,
    denied_at         TIMESTAMPTZ,
    read_at           TIMESTAMPTZ,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    metadata          JSONB
);

CREATE INDEX IF NOT EXISTS agent_messages_to_agent_read_idx
    ON jarvis_schema.agent_messages (to_agent, read_at);

CREATE INDEX IF NOT EXISTS agent_messages_created_idx
    ON jarvis_schema.agent_messages (created_at DESC);

CREATE INDEX IF NOT EXISTS agent_messages_approval_idx
    ON jarvis_schema.agent_messages (requires_approval, approved_at, denied_at)
    WHERE requires_approval = TRUE;

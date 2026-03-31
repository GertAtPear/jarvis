-- ============================================================
-- Platform Shared Memory
-- A single schema readable by all agents; writable by Eve.
-- Agents load this into their system prompt as "Shared Platform Context".
-- ============================================================

CREATE SCHEMA IF NOT EXISTS platform_schema;

CREATE TABLE IF NOT EXISTS platform_schema.user_context (
    key          VARCHAR(200) PRIMARY KEY,
    value        TEXT         NOT NULL,
    author_agent VARCHAR(50)  NOT NULL DEFAULT 'eve',
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE  platform_schema.user_context              IS 'Cross-agent shared facts about the user and their environment. Written by Eve, read by all agents.';
COMMENT ON COLUMN platform_schema.user_context.key          IS 'Fact identifier, e.g. "timezone", "primary_datacenter"';
COMMENT ON COLUMN platform_schema.user_context.value        IS 'Fact value';
COMMENT ON COLUMN platform_schema.user_context.author_agent IS 'Which agent last wrote this fact';

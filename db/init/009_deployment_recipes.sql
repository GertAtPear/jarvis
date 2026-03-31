-- ─── Migration 009: Deployment Recipes ───────────────────────────────────────
--
-- Stores per-application deployment steps so Rex and Andrew can reliably
-- deploy any registered application without keeping the steps in conversation.
-- Steps are a JSONB array of typed actions: ssh_exec, container_restart, wait,
-- http_check, container_build.
--
-- Example steps:
-- [
--   {"type": "ssh_exec",         "server": "prod-1", "command": "cd /opt/app && git pull origin main"},
--   {"type": "container_restart","name": "my-app"},
--   {"type": "wait",             "seconds": 5},
--   {"type": "http_check",       "url": "http://prod-1/health", "expect_status": 200}
-- ]
--
-- Run manually:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/009_deployment_recipes.sql

-- ─── SCHEMA: REX ─────────────────────────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS rex_schema;

-- ─── TABLE: deployment_recipes ───────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS rex_schema.deployment_recipes (
    id             SERIAL       PRIMARY KEY,
    app_name       VARCHAR(100) NOT NULL UNIQUE,
    description    TEXT,
    target_server  VARCHAR(100) NOT NULL,    -- hostname registered in andrew_schema.servers
    steps          JSONB        NOT NULL DEFAULT '[]',
    pre_checks     JSONB        NOT NULL DEFAULT '[]',  -- health checks before deploy
    post_checks    JSONB        NOT NULL DEFAULT '[]',  -- health checks after deploy
    created_by     VARCHAR(50)  NOT NULL DEFAULT 'rex',
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS deployment_recipes_app_name_idx
    ON rex_schema.deployment_recipes (app_name);

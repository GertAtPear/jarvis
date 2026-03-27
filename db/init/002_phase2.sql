-- ─── Migration 002: Phase 2 — Local Agent Host device registry ───────────────
--
-- Run manually against a running DB:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/002_phase2.sql
--
-- Tables are added to jarvis_schema (alongside agents, sessions, conversations).

-- ─── DEVICE REGISTRY ─────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.devices (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(200) NOT NULL,
    device_type         VARCHAR(50)  DEFAULT 'laptop',     -- laptop|desktop|server
    os_platform         VARCHAR(50),                       -- linux|windows|macos
    registration_token  VARCHAR(200),                      -- one-time token, nulled after use
    token_expires_at    TIMESTAMPTZ,
    device_token_path   VARCHAR(500),                      -- vault path for permanent device token
    status              VARCHAR(20)  DEFAULT 'pending',    -- pending|online|offline
    last_seen_at        TIMESTAMPTZ,
    hostname            VARCHAR(255),
    ip_address          INET,
    lah_version         VARCHAR(50),
    advertised_modules  JSONB,                             -- module list reported on connect
    created_at          TIMESTAMPTZ  DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS devices_status_idx ON jarvis_schema.devices(status);

-- ─── DEVICE PERMISSIONS ───────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.device_permissions (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id       UUID         NOT NULL REFERENCES jarvis_schema.devices(id) ON DELETE CASCADE,
    agent_name      VARCHAR(100) NOT NULL,
    capability      VARCHAR(100) NOT NULL,    -- filesystem_read|filesystem_write|podman|git|system_info|run_command|app_launcher|trash
    is_granted      BOOLEAN      DEFAULT false,
    require_confirm BOOLEAN      DEFAULT false,
    path_scope      TEXT[],                   -- NULL = unrestricted; array of allowed paths
    created_at      TIMESTAMPTZ  DEFAULT NOW(),
    updated_at      TIMESTAMPTZ  DEFAULT NOW(),
    UNIQUE(device_id, agent_name, capability)
);

CREATE INDEX IF NOT EXISTS device_permissions_device_idx
    ON jarvis_schema.device_permissions(device_id);

-- ─── DEVICE TOOL LOG ─────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.device_tool_log (
    id                UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id         UUID         REFERENCES jarvis_schema.devices(id) ON DELETE SET NULL,
    agent_name        VARCHAR(100),
    tool_name         VARCHAR(200),
    parameters        JSONB,                  -- sanitised: no file content stored
    success           BOOLEAN,
    error_message     TEXT,
    duration_ms       INT,
    confirmed_by_user BOOLEAN      DEFAULT false,
    created_at        TIMESTAMPTZ  DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS device_tool_log_device_created_idx
    ON jarvis_schema.device_tool_log(device_id, created_at DESC);

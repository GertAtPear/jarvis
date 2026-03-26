-- ─── Migration 006: Rocky Agent — pipeline monitor ───────────────────────────
--
-- Run manually against the running DB:
--   podman exec mediahost-ai-postgres psql -U mediahostai -d mediahostai -f /path/to/006_rocky.sql
--
-- Or pipe the content:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/006_rocky.sql

-- ─── SCHEMA: ROCKY (pipeline monitor) ────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS rocky_schema;

-- Services Rocky monitors on a schedule
CREATE TABLE IF NOT EXISTS rocky_schema.watched_services (
    id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(200) NOT NULL UNIQUE,
    display_name     VARCHAR(200) NOT NULL,
    check_type       VARCHAR(50)  NOT NULL,    -- http_health|tcp_port|container_running|ssh_process|sql_select|kafka_lag|radio_capture
    check_config     JSONB        NOT NULL DEFAULT '{}',
    interval_seconds INT          NOT NULL DEFAULT 60,
    enabled          BOOLEAN      NOT NULL DEFAULT true,
    vault_secret_path VARCHAR(500),
    server_id        UUID,                     -- optional FK → andrew_schema.servers
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS watched_services_enabled_idx
    ON rocky_schema.watched_services(enabled);

-- Results of individual check runs (high-volume — purged after 48h by ResultsCleanupJob)
CREATE TABLE IF NOT EXISTS rocky_schema.check_results (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    service_id  UUID        NOT NULL REFERENCES rocky_schema.watched_services(id) ON DELETE CASCADE,
    is_healthy  BOOLEAN     NOT NULL,
    detail      TEXT,
    duration_ms BIGINT      NOT NULL DEFAULT 0,
    checked_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS check_results_service_checked_idx
    ON rocky_schema.check_results(service_id, checked_at DESC);

CREATE INDEX IF NOT EXISTS check_results_checked_at_idx
    ON rocky_schema.check_results(checked_at DESC);

-- Alert history — raised when a service transitions to unhealthy, resolved when it recovers
CREATE TABLE IF NOT EXISTS rocky_schema.alert_history (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    service_id  UUID        NOT NULL REFERENCES rocky_schema.watched_services(id) ON DELETE CASCADE,
    severity    VARCHAR(20) NOT NULL DEFAULT 'warning',  -- info|warning|critical
    message     TEXT        NOT NULL,
    resolved    BOOLEAN     NOT NULL DEFAULT false,
    resolved_at TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS alert_history_service_idx
    ON rocky_schema.alert_history(service_id, resolved);

CREATE INDEX IF NOT EXISTS alert_history_created_idx
    ON rocky_schema.alert_history(created_at DESC);

-- ─── SEED: Initial watched services (Mediahost pipelines) ────────────────────

INSERT INTO rocky_schema.watched_services
    (name, display_name, check_type, check_config, interval_seconds, enabled)
VALUES
    -- Client delivery API health endpoint
    ('mediahost.delivery-api',
     'Client Delivery API',
     'http_health',
     '{"url": "http://localhost:8080/health", "timeout_seconds": 10}'::jsonb,
     60, true),

    -- STT pipeline process check (update server/process for your environment)
    ('mediahost.stt-pipeline',
     'STT Transcription Pipeline',
     'ssh_process',
     '{"server": "mediahost-stt", "process": "stt_worker"}'::jsonb,
     120, false),

    -- Kafka broker TCP probe
    ('mediahost.kafka-broker',
     'Kafka Broker',
     'tcp_port',
     '{"host": "localhost", "port": 9092, "timeout_ms": 5000}'::jsonb,
     60, false),

    -- Radio capture process check
    ('mediahost.radio-capture',
     'Radio Capture Daemon',
     'radio_capture',
     '{"server": "mediahost-capture", "process": "radio_capture", "output_dir": "/var/radio-capture", "max_age_minutes": 5}'::jsonb,
     300, false)
ON CONFLICT (name) DO NOTHING;

-- ─── SEED: Agent registry entry ──────────────────────────────────────────────

INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT
    'rocky',
    'Rocky',
    (SELECT id FROM jarvis_schema.departments WHERE name = 'IT Operations'),
    'Read-only pipeline monitor — STT, Kafka, radio capture, web scrapers, client delivery API',
    'http://localhost:5006',
    '["http_health","tcp_port","container_running","ssh_process","sql_select","kafka_lag","radio_capture"]'::jsonb,
    '["pipeline","health","monitor","status","kafka","stt","transcription","radio","capture","delivery","api","lag","scraper","unhealthy","down","alert"]'::jsonb,
    5
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name = 'rocky');

-- ─── SEED: LLM routing rules ──────────────────────────────────────────────────

INSERT INTO jarvis_schema.model_routing_rules
    (rule_name, priority, agent_name, provider_name, model_id, reason)
VALUES
    ('Rocky – Haiku (primary)',           65, 'rocky', 'anthropic', 'claude-haiku-4-5-20251001', 'Rocky primary: Haiku for fast read-only status checks'),
    ('Rocky – Gemini Flash (fallback 1)', 66, 'rocky', 'google',    'gemini-2.0-flash',          'Rocky fallback 1: Gemini Flash for status queries'),
    ('Rocky – GPT-4o-mini (fallback 2)',  67, 'rocky', 'openai',    'gpt-4o-mini',               'Rocky fallback 2: GPT-4o-mini cheaper for monitoring tasks')
ON CONFLICT DO NOTHING;

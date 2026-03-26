-- ─── ANDREW: SCHEDULED CHECKS ────────────────────────────────────────────────
-- User-defined recurring checks: container running, server up, website up, port
-- Loaded by Quartz on startup; new checks scheduled dynamically at runtime.

CREATE TABLE IF NOT EXISTS andrew_schema.scheduled_checks (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(200) NOT NULL UNIQUE,
    check_type          VARCHAR(50)  NOT NULL,       -- container_running|server_up|website_up|port_listening
    target              VARCHAR(500) NOT NULL,       -- container name, hostname, URL, or hostname:port
    server_id           UUID         REFERENCES andrew_schema.servers(id) ON DELETE SET NULL,
    schedule_type       VARCHAR(20)  NOT NULL,       -- interval|cron
    interval_minutes    INT,                         -- for interval (e.g. 10)
    cron_expression     VARCHAR(100),                -- for cron, Quartz 6-field format (e.g. 0 0 6 * * ?)
    is_active           BOOLEAN      DEFAULT true,
    notify_on_failure   BOOLEAN      DEFAULT true,
    last_checked_at     TIMESTAMPTZ,
    last_status         VARCHAR(20),                 -- ok|failed|unknown
    last_result         JSONB,
    created_at          TIMESTAMPTZ  DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  DEFAULT NOW()
);
CREATE INDEX ON andrew_schema.scheduled_checks(is_active);
CREATE INDEX ON andrew_schema.scheduled_checks(check_type);

-- Result history — last N results per check (old rows pruned by the job)
CREATE TABLE IF NOT EXISTS andrew_schema.check_results (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    check_id    UUID        NOT NULL REFERENCES andrew_schema.scheduled_checks(id) ON DELETE CASCADE,
    status      VARCHAR(20) NOT NULL,               -- ok|failed
    details     JSONB,
    duration_ms INT,
    checked_at  TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX ON andrew_schema.check_results(check_id, checked_at DESC);

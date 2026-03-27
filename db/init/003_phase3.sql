-- ─── Migration 003: Phase 3 — Sam (DBA) · Nadia (Network) · Lexi (Security) ──
--
-- Run manually against the running DB:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/003_phase3.sql

-- ─── SCHEMA: SAM (DBA agent) ──────────────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS sam_schema;

-- Registered database instances Sam monitors
CREATE TABLE IF NOT EXISTS sam_schema.databases (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(200) NOT NULL UNIQUE,
    display_name        VARCHAR(200) NOT NULL,
    db_type             VARCHAR(20)  NOT NULL,               -- mysql|postgresql|mariadb
    host                VARCHAR(255) NOT NULL,
    port                INT          NOT NULL,
    db_name             VARCHAR(200) NOT NULL,
    vault_secret_path   VARCHAR(500),                        -- path to credentials in Infisical
    status              VARCHAR(20)  NOT NULL DEFAULT 'unknown',  -- unknown|online|offline|error
    last_scanned_at     TIMESTAMPTZ,
    notes               TEXT,
    tags                JSONB        NOT NULL DEFAULT '[]',
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS sam_databases_status_idx
    ON sam_schema.databases(status);

-- Slow query records captured from performance_schema / pg_stat_statements
CREATE TABLE IF NOT EXISTS sam_schema.slow_query_log (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    database_id         UUID         NOT NULL REFERENCES sam_schema.databases(id) ON DELETE CASCADE,
    query_hash          VARCHAR(64)  NOT NULL,               -- SHA256 of normalised query text
    query_text          TEXT         NOT NULL,
    avg_duration_ms     DECIMAL(12,2),
    max_duration_ms     DECIMAL(12,2),
    execution_count     BIGINT       NOT NULL DEFAULT 1,
    schema_name         VARCHAR(200),
    captured_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(database_id, query_hash)
);

CREATE INDEX IF NOT EXISTS sam_slow_query_db_avg_idx
    ON sam_schema.slow_query_log(database_id, avg_duration_ms DESC);

-- Point-in-time connection pool snapshots
CREATE TABLE IF NOT EXISTS sam_schema.connection_stats (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    database_id         UUID         NOT NULL REFERENCES sam_schema.databases(id) ON DELETE CASCADE,
    active_connections  INT,
    max_connections     INT,
    idle_connections    INT,
    waiting_queries     INT,
    captured_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS sam_connection_stats_db_time_idx
    ON sam_schema.connection_stats(database_id, captured_at DESC);

-- Table size and bloat estimates
CREATE TABLE IF NOT EXISTS sam_schema.table_stats (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    database_id         UUID         NOT NULL REFERENCES sam_schema.databases(id) ON DELETE CASCADE,
    schema_name         VARCHAR(200) NOT NULL,
    table_name          VARCHAR(200) NOT NULL,
    row_estimate        BIGINT,
    data_size_bytes     BIGINT,
    index_size_bytes    BIGINT,
    captured_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(database_id, schema_name, table_name)
);

CREATE INDEX IF NOT EXISTS sam_table_stats_db_size_idx
    ON sam_schema.table_stats(database_id, data_size_bytes DESC);

-- Replication health per database
CREATE TABLE IF NOT EXISTS sam_schema.replication_status (
    id                    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    database_id           UUID         NOT NULL REFERENCES sam_schema.databases(id) ON DELETE CASCADE,
    role                  VARCHAR(20)  NOT NULL DEFAULT 'primary',  -- primary|replica
    replication_lag_seconds DECIMAL(12,3),
    is_connected          BOOLEAN      NOT NULL DEFAULT true,
    replica_host          VARCHAR(255),
    captured_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS sam_replication_db_time_idx
    ON sam_schema.replication_status(database_id, captured_at DESC);

-- Scan audit log
CREATE TABLE IF NOT EXISTS sam_schema.discovery_log (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    database_id     UUID         NOT NULL REFERENCES sam_schema.databases(id) ON DELETE CASCADE,
    scan_type       VARCHAR(50)  NOT NULL,   -- full|connections|slow_queries|table_stats|replication
    status          VARCHAR(20)  NOT NULL,   -- success|error|partial
    details         JSONB        NOT NULL DEFAULT '{}',
    duration_ms     INT,
    ran_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS sam_discovery_log_db_time_idx
    ON sam_schema.discovery_log(database_id, ran_at DESC);


-- ─── SCHEMA: NADIA (Network agent) ───────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS nadia_schema;

-- Network interfaces Nadia monitors (WAN lines, VPN tunnels, LAN segments)
CREATE TABLE IF NOT EXISTS nadia_schema.network_interfaces (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name                VARCHAR(200) NOT NULL UNIQUE,
    display_name        VARCHAR(200) NOT NULL,
    if_type             VARCHAR(20)  NOT NULL,               -- wan|lan|vpn|loopback
    ip_address          INET,
    target_host         VARCHAR(255),                        -- probe target (gateway, 8.8.8.8, etc.)
    subnet              CIDR,                                -- used by Lexi for device scans
    is_active           BOOLEAN      NOT NULL DEFAULT true,
    is_primary          BOOLEAN      NOT NULL DEFAULT false, -- primary WAN line
    vault_secret_path   VARCHAR(500),                        -- VPN credentials if needed
    check_interval_sec  INT          NOT NULL DEFAULT 300,
    notes               TEXT,
    created_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS nadia_interfaces_active_idx
    ON nadia_schema.network_interfaces(is_active);

-- Latency / packet loss measurements per interface
CREATE TABLE IF NOT EXISTS nadia_schema.latency_history (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    interface_id        UUID         NOT NULL REFERENCES nadia_schema.network_interfaces(id) ON DELETE CASCADE,
    target_host         VARCHAR(255) NOT NULL,
    rtt_ms              DECIMAL(10,3),
    packet_loss_pct     DECIMAL(5,2) NOT NULL DEFAULT 0,
    hop_count           INT,
    status              VARCHAR(20)  NOT NULL DEFAULT 'ok',  -- ok|degraded|unreachable
    probed_at           TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS nadia_latency_interface_time_idx
    ON nadia_schema.latency_history(interface_id, probed_at DESC);

-- Wi-Fi access point inventory
CREATE TABLE IF NOT EXISTS nadia_schema.wifi_nodes (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    ssid                VARCHAR(255),
    bssid               MACADDR      NOT NULL,
    channel             INT,
    frequency_ghz       DECIMAL(5,3),
    signal_dbm          INT,
    noise_dbm           INT,
    connected_clients   INT          NOT NULL DEFAULT 0,
    ap_host             VARCHAR(255),                        -- managing AP/router hostname
    scanned_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(bssid)
);

CREATE INDEX IF NOT EXISTS nadia_wifi_nodes_ssid_idx
    ON nadia_schema.wifi_nodes(ssid);

-- DNS resolution checks
CREATE TABLE IF NOT EXISTS nadia_schema.dns_checks (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    resolver_host       VARCHAR(255) NOT NULL,               -- 8.8.8.8, 1.1.1.1, local
    record_type         VARCHAR(10)  NOT NULL DEFAULT 'A',
    query_name          VARCHAR(500) NOT NULL,
    resolved_value      VARCHAR(500),
    resolution_ms       INT,
    is_healthy          BOOLEAN      NOT NULL DEFAULT true,
    checked_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS nadia_dns_checks_resolver_time_idx
    ON nadia_schema.dns_checks(resolver_host, checked_at DESC);

-- Line failover events (when active WAN switches)
CREATE TABLE IF NOT EXISTS nadia_schema.failover_events (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    from_interface      VARCHAR(200),
    to_interface        VARCHAR(200),
    trigger_reason      TEXT,
    detected_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    resolved_at         TIMESTAMPTZ,
    duration_seconds    INT
);

CREATE INDEX IF NOT EXISTS nadia_failover_events_time_idx
    ON nadia_schema.failover_events(detected_at DESC);


-- ─── SCHEMA: LEXI (Security agent) ───────────────────────────────────────────

CREATE SCHEMA IF NOT EXISTS lexi_schema;

-- TLS certificates Lexi monitors
CREATE TABLE IF NOT EXISTS lexi_schema.tls_certificates (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    host                VARCHAR(500) NOT NULL,
    port                INT          NOT NULL DEFAULT 443,
    subject_cn          VARCHAR(500),
    issuer              VARCHAR(500),
    san                 JSONB        NOT NULL DEFAULT '[]',  -- Subject Alternative Names
    valid_from          DATE,
    valid_to            DATE,
    days_remaining      INT,
    is_valid            BOOLEAN      NOT NULL DEFAULT true,
    chain_depth         INT,
    checked_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(host, port)
);

CREATE INDEX IF NOT EXISTS lexi_certs_days_remaining_idx
    ON lexi_schema.tls_certificates(days_remaining);

CREATE INDEX IF NOT EXISTS lexi_certs_valid_to_idx
    ON lexi_schema.tls_certificates(valid_to);

-- Open ports discovered on registered servers
CREATE TABLE IF NOT EXISTS lexi_schema.open_ports (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    server_id           UUID,                                -- optional FK → andrew_schema.servers
    host                VARCHAR(255) NOT NULL,
    port                INT          NOT NULL,
    protocol            VARCHAR(10)  NOT NULL DEFAULT 'tcp',
    service_name        VARCHAR(200),
    state               VARCHAR(20)  NOT NULL DEFAULT 'open',
    is_expected         BOOLEAN      NOT NULL DEFAULT false, -- true = known legitimate
    scanned_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(host, port, protocol)
);

CREATE INDEX IF NOT EXISTS lexi_open_ports_host_idx
    ON lexi_schema.open_ports(host);

CREATE INDEX IF NOT EXISTS lexi_open_ports_unexpected_idx
    ON lexi_schema.open_ports(is_expected)
    WHERE is_expected = false;

-- Access anomalies detected from SSH auth log analysis
CREATE TABLE IF NOT EXISTS lexi_schema.access_anomalies (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    source_ip           INET,
    country_code        VARCHAR(5),
    city                VARCHAR(200),
    event_type          VARCHAR(50)  NOT NULL,               -- failed_login_burst|brute_force|geo_anomaly|off_hours_root|new_user
    username            VARCHAR(200),
    target_host         VARCHAR(255),
    event_count         INT          NOT NULL DEFAULT 1,
    severity            VARCHAR(20)  NOT NULL DEFAULT 'medium',  -- low|medium|high|critical
    first_seen          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_seen           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    is_resolved         BOOLEAN      NOT NULL DEFAULT false,
    resolved_at         TIMESTAMPTZ,
    notes               TEXT
);

CREATE INDEX IF NOT EXISTS lexi_anomalies_unresolved_idx
    ON lexi_schema.access_anomalies(is_resolved, severity)
    WHERE is_resolved = false;

CREATE INDEX IF NOT EXISTS lexi_anomalies_source_ip_idx
    ON lexi_schema.access_anomalies(source_ip);

-- CVE alerts matched against installed software
CREATE TABLE IF NOT EXISTS lexi_schema.cve_alerts (
    id                    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    cve_id                VARCHAR(50)  NOT NULL UNIQUE,
    severity              VARCHAR(20)  NOT NULL DEFAULT 'medium',  -- low|medium|high|critical
    cvss_score            DECIMAL(4,1),
    description           TEXT,
    affected_software     VARCHAR(500),
    affected_version      VARCHAR(200),
    fixed_version         VARCHAR(200),
    published_at          TIMESTAMPTZ,
    matched_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    is_acknowledged       BOOLEAN      NOT NULL DEFAULT false,
    acknowledged_at       TIMESTAMPTZ,
    acknowledgement_notes TEXT
);

CREATE INDEX IF NOT EXISTS lexi_cve_alerts_unack_severity_idx
    ON lexi_schema.cve_alerts(is_acknowledged, severity)
    WHERE is_acknowledged = false;

-- Software inventory per host (feeds CVE matching)
CREATE TABLE IF NOT EXISTS lexi_schema.software_inventory (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    host                VARCHAR(255) NOT NULL,
    package_name        VARCHAR(500) NOT NULL,
    version             VARCHAR(200),
    package_manager     VARCHAR(20)  NOT NULL DEFAULT 'apt',  -- apt|yum|pip|npm|docker
    scanned_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(host, package_name, package_manager)
);

CREATE INDEX IF NOT EXISTS lexi_software_host_idx
    ON lexi_schema.software_inventory(host);

-- Network device inventory (for rogue device detection)
CREATE TABLE IF NOT EXISTS lexi_schema.network_devices (
    id                  UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    mac_address         MACADDR      NOT NULL UNIQUE,
    ip_address          INET,
    hostname            VARCHAR(255),
    vendor              VARCHAR(255),                        -- OUI-based vendor lookup
    is_known            BOOLEAN      NOT NULL DEFAULT false, -- confirmed legitimate device
    device_name         VARCHAR(255),                        -- friendly name once known
    notes               TEXT,
    first_seen          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_seen           TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    scanned_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS lexi_network_devices_unknown_idx
    ON lexi_schema.network_devices(is_known)
    WHERE is_known = false;

CREATE INDEX IF NOT EXISTS lexi_network_devices_last_seen_idx
    ON lexi_schema.network_devices(last_seen DESC);

-- Scan audit log
CREATE TABLE IF NOT EXISTS lexi_schema.scan_log (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    scan_type       VARCHAR(50)  NOT NULL,   -- cert|port|access_log|cve|software|network_devices
    status          VARCHAR(20)  NOT NULL,   -- success|error|partial
    hosts_scanned   INT          NOT NULL DEFAULT 0,
    findings_count  INT          NOT NULL DEFAULT 0,
    duration_ms     INT,
    ran_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS lexi_scan_log_type_time_idx
    ON lexi_schema.scan_log(scan_type, ran_at DESC);


-- ─── LLM COST RATES (Jarvis analytics) ───────────────────────────────────────

CREATE TABLE IF NOT EXISTS jarvis_schema.llm_cost_rates (
    id                      UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_name           VARCHAR(50)  NOT NULL,
    model_id                VARCHAR(200) NOT NULL,
    input_cost_per_1k       DECIMAL(10,6) NOT NULL,         -- USD per 1k input tokens
    output_cost_per_1k      DECIMAL(10,6) NOT NULL,         -- USD per 1k output tokens
    effective_from          TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    notes                   TEXT,
    UNIQUE(provider_name, model_id)
);

-- Seed with current pricing (update when providers change rates)
INSERT INTO jarvis_schema.llm_cost_rates
    (provider_name, model_id, input_cost_per_1k, output_cost_per_1k, notes)
VALUES
    ('anthropic', 'claude-opus-4-6',             0.015000, 0.075000, 'Anthropic Opus 4.6'),
    ('anthropic', 'claude-sonnet-4-6',           0.003000, 0.015000, 'Anthropic Sonnet 4.6'),
    ('anthropic', 'claude-haiku-4-5-20251001',   0.000800, 0.004000, 'Anthropic Haiku 4.5'),
    ('anthropic', 'claude-haiku-4-5',            0.000800, 0.004000, 'Anthropic Haiku 4.5 (alias)'),
    ('google',    'gemini-1.5-pro',              0.001250, 0.005000, 'Google Gemini 1.5 Pro 2M context'),
    ('google',    'gemini-1.5-flash',            0.000075, 0.000300, 'Google Gemini 1.5 Flash'),
    ('google',    'gemini-2.0-flash',            0.000100, 0.000400, 'Google Gemini 2.0 Flash'),
    ('openai',    'gpt-4o',                      0.005000, 0.015000, 'OpenAI GPT-4o'),
    ('openai',    'gpt-4o-mini',                 0.000150, 0.000600, 'OpenAI GPT-4o-mini')
ON CONFLICT (provider_name, model_id) DO NOTHING;


-- ─── SEED: Agent registry entries ────────────────────────────────────────────

INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT
    'sam',
    'Sam',
    (SELECT id FROM jarvis_schema.departments WHERE name = 'IT Operations'),
    'DBA – MySQL, PostgreSQL and MariaDB performance, slow queries, connections, replication',
    'http://localhost:5007',
    '["mysql","postgresql","slow_queries","connections","replication","explain"]'::jsonb,
    '["database","db","mysql","postgres","mariadb","slow query","slow queries","query","table","replication","connection","explain","schema","index","bloat","vacuum"]'::jsonb,
    6
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name = 'sam');

INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT
    'nadia',
    'Nadia',
    (SELECT id FROM jarvis_schema.departments WHERE name = 'IT Operations'),
    'Network monitor – VPN tunnels, WAN failover, Wi-Fi inventory, latency, DNS health',
    'http://localhost:5008',
    '["ping","traceroute","dns","wifi","vpn","latency"]'::jsonb,
    '["network","interface","latency","ping","wifi","wi-fi","dns","failover","vpn","wan","traceroute","packet loss","bandwidth","route","gateway","subnet"]'::jsonb,
    7
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name = 'nadia');

INSERT INTO jarvis_schema.agents
    (name, display_name, department_id, description, base_url, capabilities, routing_keywords, sort_order)
SELECT
    'lexi',
    'Lexi',
    (SELECT id FROM jarvis_schema.departments WHERE name = 'IT Operations'),
    'Security monitor – TLS certificates, open ports, access anomalies, CVEs, rogue device detection',
    'http://localhost:5009',
    '["tls","port_scan","access_log","cve","network_scan"]'::jsonb,
    '["security","certificate","tls","ssl","cert","port","cve","vulnerability","access","anomaly","auth","login","brute force","device","rogue","firewall","scan","expir"]'::jsonb,
    8
WHERE NOT EXISTS (SELECT 1 FROM jarvis_schema.agents WHERE name = 'lexi');


-- ─── SEED: LLM routing rules ──────────────────────────────────────────────────

INSERT INTO jarvis_schema.model_routing_rules
    (rule_name, priority, agent_name, provider_name, model_id, reason)
VALUES
    -- Sam — DBA queries: Haiku primary (fast DB lookups), then fallbacks
    ('Sam – Haiku (primary)',             68, 'sam',   'anthropic', 'claude-haiku-4-5-20251001', 'Sam primary: Haiku for fast DB status and query analysis'),
    ('Sam – Gemini Flash (fallback 1)',   69, 'sam',   'google',    'gemini-2.0-flash',          'Sam fallback 1: Gemini Flash for DB queries'),
    ('Sam – GPT-4o-mini (fallback 2)',    70, 'sam',   'openai',    'gpt-4o-mini',               'Sam fallback 2: GPT-4o-mini for monitoring tasks'),

    -- Nadia — network monitoring: Haiku primary
    ('Nadia – Haiku (primary)',           71, 'nadia', 'anthropic', 'claude-haiku-4-5-20251001', 'Nadia primary: Haiku for fast network status lookups'),
    ('Nadia – Gemini Flash (fallback 1)', 72, 'nadia', 'google',    'gemini-2.0-flash',          'Nadia fallback 1: Gemini Flash'),
    ('Nadia – GPT-4o-mini (fallback 2)',  73, 'nadia', 'openai',    'gpt-4o-mini',               'Nadia fallback 2: GPT-4o-mini'),

    -- Lexi — security analysis: Sonnet primary (needs reasoning for CVE/anomaly assessment)
    ('Lexi – Sonnet (primary)',           74, 'lexi',  'anthropic', 'claude-sonnet-4-6',         'Lexi primary: Sonnet for security reasoning and CVE analysis'),
    ('Lexi – Gemini Pro (fallback 1)',    75, 'lexi',  'google',    'gemini-1.5-pro',            'Lexi fallback 1: Gemini Pro for security analysis'),
    ('Lexi – GPT-4o (fallback 2)',        76, 'lexi',  'openai',    'gpt-4o',                    'Lexi fallback 2: GPT-4o for security tasks')
ON CONFLICT DO NOTHING;

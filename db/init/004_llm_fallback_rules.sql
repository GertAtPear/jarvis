-- ─── Migration 004: LLM fallback routing rules + vault grants table ──────────
--
-- Run manually against the running DB:
--   podman exec mediahost-ai-postgres psql -U mediahostai -d mediahostai -f /path/to/004_llm_fallback_rules.sql
--
-- Or pipe the content:
--   podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/004_llm_fallback_rules.sql

-- ─── Agent-specific routing rules with per-provider fallback ─────────────────
--
-- Andrew rules (priority 25-27) match before the generic rules (priority 80-999).
-- SelectModelsAsync deduplicates by provider, returning up to 3 candidates:
--   [anthropic/sonnet, google/gemini-2.0-flash, openai/gpt-4o] for Andrew.
--
-- Eve rules (priority 55-57) similarly provide fallback for Eve's tasks.

INSERT INTO jarvis_schema.model_routing_rules
    (rule_name, priority, agent_name, provider_name, model_id, reason)
VALUES
    ('Andrew – Sonnet (primary)',            25, 'andrew', 'anthropic', 'claude-sonnet-4-6', 'Andrew primary: Sonnet handles multi-step tool-use well'),
    ('Andrew – Gemini Flash (fallback 1)',   26, 'andrew', 'google',    'gemini-2.0-flash',  'Andrew fallback 1: Gemini 2.0 Flash supports tool-use'),
    ('Andrew – GPT-4o (fallback 2)',         27, 'andrew', 'openai',    'gpt-4o',            'Andrew fallback 2: GPT-4o reliable for tool-use'),
    ('Eve – Sonnet (primary)',               55, 'eve',    'anthropic', 'claude-sonnet-4-6', 'Eve primary: Sonnet for scheduling and calendar reasoning'),
    ('Eve – Gemini Flash (fallback 1)',      56, 'eve',    'google',    'gemini-2.0-flash',  'Eve fallback 1: Gemini Flash'),
    ('Eve – GPT-4o-mini (fallback 2)',       57, 'eve',    'openai',    'gpt-4o-mini',       'Eve fallback 2: GPT-4o-mini cheaper for scheduling tasks')
ON CONFLICT DO NOTHING;

-- ─── Vault grants table ───────────────────────────────────────────────────────
--
-- Used by the ScopedVaultService grant-checking mechanism.
-- A grantor agent (e.g. SAM) can grant another agent (e.g. Rex) access to a
-- specific path prefix in the vault.
--
-- Example future grant: SAM grants Rex read access to /databases/mysql-prod
--   INSERT INTO jarvis_schema.vault_grants
--     (grantor_agent, grantee_agent, path_prefix, can_write)
--   VALUES ('sam', 'rex', '/databases/mysql-prod', false);

CREATE TABLE IF NOT EXISTS jarvis_schema.vault_grants (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    grantor_agent   VARCHAR(100) NOT NULL,
    grantee_agent   VARCHAR(100) NOT NULL,
    path_prefix     VARCHAR(500) NOT NULL,
    can_write       BOOLEAN      DEFAULT false,
    granted_at      TIMESTAMPTZ  DEFAULT NOW(),
    expires_at      TIMESTAMPTZ,              -- NULL = no expiry
    UNIQUE(grantee_agent, path_prefix)
);

CREATE INDEX IF NOT EXISTS vault_grants_grantee_idx
    ON jarvis_schema.vault_grants(grantee_agent);

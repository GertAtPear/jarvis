-- ============================================================================
-- Migration 007: Expand eve_schema.contacts with rich contact details
--
-- Adds: phone numbers, multiple emails, addresses, company, social links
-- Fixes: adds UNIQUE constraint on name so UpsertAsync works correctly
--        (previously ON CONFLICT (id) with a fresh Guid always inserted new rows)
-- ============================================================================

-- 1. Add all new contact detail columns (all nullable, non-breaking)
ALTER TABLE eve_schema.contacts
    ADD COLUMN IF NOT EXISTS phone_cell     VARCHAR(50),
    ADD COLUMN IF NOT EXISTS phone_work     VARCHAR(50),
    ADD COLUMN IF NOT EXISTS phone_home     VARCHAR(50),
    ADD COLUMN IF NOT EXISTS email_personal VARCHAR(500),
    ADD COLUMN IF NOT EXISTS email_work     VARCHAR(500),
    ADD COLUMN IF NOT EXISTS address_home   TEXT,
    ADD COLUMN IF NOT EXISTS address_work   TEXT,
    ADD COLUMN IF NOT EXISTS company        VARCHAR(500),
    ADD COLUMN IF NOT EXISTS contact_type   VARCHAR(20) DEFAULT 'person',
    ADD COLUMN IF NOT EXISTS website        VARCHAR(500),
    ADD COLUMN IF NOT EXISTS social_links   JSONB DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS extra          JSONB DEFAULT '{}';

-- 2. Add UNIQUE constraint on name to enable upsert-by-name
--    Pre-check before running:
--      SELECT name, count(*) FROM eve_schema.contacts GROUP BY name HAVING count(*) > 1;
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'contacts_name_uq' AND conrelid = 'eve_schema.contacts'::regclass
    ) THEN
        ALTER TABLE eve_schema.contacts ADD CONSTRAINT contacts_name_uq UNIQUE (name);
    END IF;
END$$;

-- 3. Indexes for common lookups
CREATE INDEX IF NOT EXISTS contacts_email_personal_idx ON eve_schema.contacts(email_personal);
CREATE INDEX IF NOT EXISTS contacts_email_work_idx     ON eve_schema.contacts(email_work);
CREATE INDEX IF NOT EXISTS contacts_phone_cell_idx     ON eve_schema.contacts(phone_cell);
CREATE INDEX IF NOT EXISTS contacts_company_idx        ON eve_schema.contacts(company);

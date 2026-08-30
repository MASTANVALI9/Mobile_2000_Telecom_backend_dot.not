-- ============================================================================
-- Mobile2000 Telecom Recharge Platform — Seed Data
-- Target: Microsoft SQL Server 2019+
-- ============================================================================
-- Prerequisite: 01_schema.sql must be executed first.
-- Idempotent: safe to re-run; uses MERGE to avoid duplicates.
-- ============================================================================

-- Seed telecom operators
MERGE INTO TelecomOperators AS target
USING (VALUES
    ('Jio',    'JIO',   1),
    ('Airtel', 'AIRTL', 1),
    ('Vi',     'VI',    1),
    ('BSNL',   'BSNL',  1)
) AS source (Name, Code, IsActive)
ON target.Name = source.Name
WHEN NOT MATCHED THEN
    INSERT (Name, Code, IsActive)
    VALUES (source.Name, source.Code, source.IsActive)
WHEN MATCHED THEN
    UPDATE SET Code = source.Code;

PRINT 'Seed data applied — 4 telecom operators upserted.';
GO

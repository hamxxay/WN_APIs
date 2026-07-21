-- ============================================================
-- WN_AmountFields Migration
-- Single source of truth for every monetary field in WorkNest.
-- Run once in SSMS against SAC400.
-- ============================================================
USE [SAC400]
GO

-- ── 1. Create table ───────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.WN_AmountFields') AND type = 'U')
BEGIN
    CREATE TABLE dbo.WN_AmountFields (
        Id       INT IDENTITY(1,1) PRIMARY KEY,
        Entity   NVARCHAR(100) NOT NULL,   -- e.g. 'Space', 'Booking'
        Field    NVARCHAR(100) NOT NULL,   -- exact property key in DTO / API response
        Label    NVARCHAR(200) NOT NULL,   -- human-readable label shown in UI
        Currency NVARCHAR(10)  NOT NULL DEFAULT 'PKR',
        CONSTRAINT UQ_WN_AmountFields UNIQUE (Entity, Field)
    );
    PRINT 'Table WN_AmountFields created.';
END
ELSE
    PRINT 'Table WN_AmountFields already exists.';
GO

-- ── 2. Seed data ──────────────────────────────────────────────────────────────
-- Use MERGE so re-running is safe.
MERGE dbo.WN_AmountFields AS target
USING (VALUES
    -- Space
    ('Space',       'pricePerHour',    'Price / Hour',         'PKR'),
    ('Space',       'pricePerDay',     'Price / Day',          'PKR'),
    ('Space',       'pricePerMonth',   'Price / Month',        'PKR'),
    -- SpaceConfig
    ('SpaceConfig', 'securityDeposit', 'Security Deposit',     'PKR'),
    ('SpaceConfig', 'pricePerHour',    'Config Price / Hour',  'PKR'),
    ('SpaceConfig', 'pricePerDay',     'Config Price / Day',   'PKR'),
    ('SpaceConfig', 'pricePerMonth',   'Config Price / Month', 'PKR'),
    -- Booking
    ('Booking',     'totalAmount',     'Booking Amount',       'PKR'),
    -- Payment
    ('Payment',     'amount',          'Payment Amount',       'PKR'),
    -- PricingPlan
    ('PricingPlan', 'price',           'Plan Price',           'PKR'),
    -- Membership
    ('Membership',  'planPrice',       'Membership Price',     'PKR')
) AS source (Entity, Field, Label, Currency)
ON target.Entity = source.Entity AND target.Field = source.Field
WHEN MATCHED THEN
    UPDATE SET Label = source.Label, Currency = source.Currency
WHEN NOT MATCHED THEN
    INSERT (Entity, Field, Label, Currency)
    VALUES (source.Entity, source.Field, source.Label, source.Currency);
GO

-- ── 3. WN_AmountFields_GetList ────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_AmountFields_GetList
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Entity, Field, Label, Currency
    FROM dbo.WN_AmountFields
    ORDER BY Entity, Field;
END
GO

-- ── 4. WN_AmountFields_GetByEntityField ───────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_AmountFields_GetByEntityField
    @Entity NVARCHAR(100),
    @Field  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Entity, Field, Label, Currency
    FROM dbo.WN_AmountFields
    WHERE Entity = @Entity AND Field = @Field;
END
GO

PRINT 'AmountFields migration completed successfully.';
GO

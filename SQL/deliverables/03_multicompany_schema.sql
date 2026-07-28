-- ============================================================
-- 03_multicompany_schema.sql
-- Stage 3: Multi-company schema changes.
--
-- Columns added:
--   WN_Locations : CompanyId INT NULL  (source of truth for both)
--                  BranchId  already exists (native column)
--   WN_Spaces    : CompanyId INT NULL  (denormalized from Location)
--                  BranchId  INT NULL  (denormalized from Location)
--   WN_Bookings  : CompanyId INT NULL  (snapshot at booking time)
--                  BranchId  INT NULL  (snapshot at booking time)
--   WN_BookingDetails : CompanyId INT NULL
--                       BranchId  INT NULL
--
-- Trigger trg_WN_Spaces_SyncCompanyBranch fires on WN_Spaces
-- INSERT/UPDATE and keeps both columns in sync from WN_Locations.
--
-- Backfill (Step 7) is GATED — do not run until Company A/B
-- → BranchId mapping is confirmed.
-- ============================================================

USE [SAC400];
GO

-- ── Step 1: Add CompanyId to WN_Locations ─────────────────────────────────────
-- BranchId already exists natively on this table.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Locations') AND name = 'CompanyId'
)
BEGIN
    ALTER TABLE dbo.WN_Locations ADD CompanyId INT NULL;
    PRINT 'Column CompanyId added to WN_Locations.';
END
ELSE
    PRINT 'WN_Locations.CompanyId already exists.';
GO

-- ── Step 2: Add CompanyId + BranchId to WN_Spaces ────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'CompanyId'
)
BEGIN
    ALTER TABLE dbo.WN_Spaces ADD CompanyId INT NULL;
    PRINT 'Column CompanyId added to WN_Spaces.';
END
ELSE
    PRINT 'WN_Spaces.CompanyId already exists.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'BranchId'
)
BEGIN
    ALTER TABLE dbo.WN_Spaces ADD BranchId INT NULL;
    PRINT 'Column BranchId added to WN_Spaces.';
END
ELSE
    PRINT 'WN_Spaces.BranchId already exists.';
GO

-- ── Step 3: Add CompanyId + BranchId to WN_Bookings ──────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'CompanyId'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings ADD CompanyId INT NULL;
    PRINT 'Column CompanyId added to WN_Bookings.';
END
ELSE
    PRINT 'WN_Bookings.CompanyId already exists.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'BranchId'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings ADD BranchId INT NULL;
    PRINT 'Column BranchId added to WN_Bookings.';
END
ELSE
    PRINT 'WN_Bookings.BranchId already exists.';
GO

-- ── Step 4: Add CompanyId + BranchId to WN_BookingDetails ────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'CompanyId'
)
BEGIN
    ALTER TABLE dbo.WN_BookingDetails ADD CompanyId INT NULL;
    PRINT 'Column CompanyId added to WN_BookingDetails.';
END
ELSE
    PRINT 'WN_BookingDetails.CompanyId already exists.';
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'BranchId'
)
BEGIN
    ALTER TABLE dbo.WN_BookingDetails ADD BranchId INT NULL;
    PRINT 'Column BranchId added to WN_BookingDetails.';
END
ELSE
    PRINT 'WN_BookingDetails.BranchId already exists.';
GO

-- ── Step 5: Indexes ───────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'IX_WN_Spaces_CompanyId'
)
    CREATE INDEX IX_WN_Spaces_CompanyId ON dbo.WN_Spaces (CompanyId);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'IX_WN_Spaces_BranchId'
)
    CREATE INDEX IX_WN_Spaces_BranchId ON dbo.WN_Spaces (BranchId);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'IX_WN_Bookings_CompanyId'
)
    CREATE INDEX IX_WN_Bookings_CompanyId ON dbo.WN_Bookings (CompanyId);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'IX_WN_Bookings_BranchId'
)
    CREATE INDEX IX_WN_Bookings_BranchId ON dbo.WN_Bookings (BranchId);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'IX_WN_BookingDetails_CompanyId'
)
    CREATE INDEX IX_WN_BookingDetails_CompanyId ON dbo.WN_BookingDetails (CompanyId);
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'IX_WN_BookingDetails_BranchId'
)
    CREATE INDEX IX_WN_BookingDetails_BranchId ON dbo.WN_BookingDetails (BranchId);
GO

PRINT 'All indexes created/verified.';
GO

-- ── Step 6: Trigger — sync CompanyId + BranchId on WN_Spaces ─────────────────
-- Fires on INSERT or UPDATE of WN_Spaces.
-- Set-based: handles multi-row inserted sets correctly.
-- Syncs both CompanyId and BranchId from WN_Locations via LocationId.
CREATE OR ALTER TRIGGER [dbo].[trg_WN_Spaces_SyncCompanyBranch]
ON dbo.WN_Spaces
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF (TRIGGER_NESTLEVEL() > 1) RETURN;

    IF EXISTS (SELECT 1 FROM inserted)
    BEGIN
        UPDATE s
        SET    s.CompanyId = l.CompanyId,
               s.BranchId  = l.BranchId
        FROM   dbo.WN_Spaces    s
        JOIN   inserted         i ON i.IdGUID = s.IdGUID
        JOIN   dbo.WN_Locations l ON l.IdGUID = i.LocationId;
    END
END
GO

PRINT 'Trigger trg_WN_Spaces_SyncCompanyBranch created/updated.';
GO

-- ── Step 7: Backfill — AWAITING CONFIRMATION, DO NOT RUN ─────────────────────
-- Fill in actual CompanyId values and BranchId ranges before running.
--
--   Company A CompanyId = ???   BranchId IN (???, ???)
--   Company B CompanyId = ???   BranchId IN (???)
--
-- 7a: Set WN_Locations.CompanyId from BranchId mapping
/*
UPDATE dbo.WN_Locations SET CompanyId = /* COMPANY_A_ID */ WHERE BranchId IN (/* BRANCH_IDS_A */);
UPDATE dbo.WN_Locations SET CompanyId = /* COMPANY_B_ID */ WHERE BranchId IN (/* BRANCH_IDS_B */);
*/

-- 7b: Cascade to WN_Spaces (trigger handles future rows automatically)
/*
UPDATE s
SET    s.CompanyId = l.CompanyId,
       s.BranchId  = l.BranchId
FROM   dbo.WN_Spaces    s
JOIN   dbo.WN_Locations l ON l.IdGUID = s.LocationId
WHERE  l.CompanyId IS NOT NULL
  AND (s.CompanyId IS NULL OR s.BranchId IS NULL);
*/

-- 7c: Cascade to WN_Bookings from WN_Spaces
/*
UPDATE b
SET    b.CompanyId = s.CompanyId,
       b.BranchId  = s.BranchId
FROM   dbo.WN_Bookings b
JOIN   dbo.WN_Spaces   s ON s.IdGUID = b.SpaceGuid
WHERE  s.CompanyId IS NOT NULL
  AND (b.CompanyId IS NULL OR b.BranchId IS NULL);

-- Unresolvable bookings (space hard-deleted):
SELECT COUNT(*) AS UnresolvableBookings
FROM dbo.WN_Bookings b
WHERE (b.CompanyId IS NULL OR b.BranchId IS NULL)
  AND b.SpaceGuid IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.WN_Spaces s WHERE s.IdGUID = b.SpaceGuid);
*/

-- 7d: Cascade to WN_BookingDetails from WN_Bookings → WN_Spaces → WN_Locations
/*
UPDATE bd
SET    bd.CompanyId = l.CompanyId,
       bd.BranchId  = l.BranchId
FROM   dbo.WN_BookingDetails bd
JOIN   dbo.WN_Bookings       b  ON b.IdGUID  = bd.BookingGuid
JOIN   dbo.WN_Spaces         s  ON s.IdGUID  = b.SpaceGuid
JOIN   dbo.WN_Locations      l  ON l.IdGUID  = s.LocationId
WHERE (bd.CompanyId IS NULL OR bd.BranchId IS NULL);
*/

-- ── Step 8: Tighten nullability after confirmed full backfill ─────────────────
-- Run ONLY after step 7 is complete and NULL counts below return 0.
/*
SELECT COUNT(*) AS NullCount_Locations  FROM dbo.WN_Locations    WHERE CompanyId IS NULL;
SELECT COUNT(*) AS NullCount_Spaces     FROM dbo.WN_Spaces        WHERE CompanyId IS NULL OR BranchId IS NULL;

ALTER TABLE dbo.WN_Locations ALTER COLUMN CompanyId INT NOT NULL;
ALTER TABLE dbo.WN_Spaces    ALTER COLUMN CompanyId INT NOT NULL;
ALTER TABLE dbo.WN_Spaces    ALTER COLUMN BranchId  INT NOT NULL;
-- WN_Bookings and WN_BookingDetails remain nullable for historical orphans.
*/

PRINT 'Stage 3 DDL complete. Steps 7 and 8 are gated — see comments above.';
GO

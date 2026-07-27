-- ══════════════════════════════════════════════════════════════════════════════
-- Fix WN_Bookings.ValidityDate: NCHAR(10) → DATETIME
-- Existing truncated values ("Jul 29 202") cannot be recovered — set to NULL.
-- All new bookings will store correctly after this fix.
-- ══════════════════════════════════════════════════════════════════════════════

-- Step 1: Add temp DATETIME column
ALTER TABLE dbo.WN_Bookings ADD ValidityDate_New DATETIME NULL;
GO

-- Step 2: Copy recoverable values (any that TRY_CAST can parse)
UPDATE dbo.WN_Bookings
SET ValidityDate_New = TRY_CAST(LTRIM(RTRIM(ValidityDate)) AS DATETIME)
WHERE ValidityDate IS NOT NULL;
GO

-- Step 3: Drop old NCHAR(10) column
ALTER TABLE dbo.WN_Bookings DROP COLUMN ValidityDate;
GO

-- Step 4: Rename new column
EXEC sp_rename 'dbo.WN_Bookings.ValidityDate_New', 'ValidityDate', 'COLUMN';
GO

-- Step 5: Verify
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'ValidityDate';

PRINT 'Done. ValidityDate is now DATETIME.';
GO

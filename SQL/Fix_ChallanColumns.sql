-- ══════════════════════════════════════════════════════════════════════════════
-- WorkNest — Fix ChallanNumber and Validity column types in WN_Bookings
-- Run this BEFORE Challan_Migration.sql if you get conversion errors
-- ══════════════════════════════════════════════════════════════════════════════

-- Drop and re-add ChallanNumber if it exists as wrong type (e.g. INT)
IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'ChallanNumber'
      AND DATA_TYPE != 'nvarchar'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings DROP COLUMN ChallanNumber;
    PRINT 'Dropped ChallanNumber (wrong type).';
END

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'ChallanNumber'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings ADD ChallanNumber NVARCHAR(50) NULL;
    PRINT 'Added ChallanNumber NVARCHAR(50).';
END
ELSE
    PRINT 'ChallanNumber already exists with correct type.';

-- Drop and re-add Validity if it exists as wrong type
IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'Validity'
      AND DATA_TYPE NOT IN ('datetime', 'datetime2', 'date')
)
BEGIN
    ALTER TABLE dbo.WN_Bookings DROP COLUMN Validity;
    PRINT 'Dropped Validity (wrong type).';
END

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'Validity'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings ADD Validity DATETIME NULL;
    PRINT 'Added Validity DATETIME.';
END
ELSE
    PRINT 'Validity already exists with correct type.';

-- Verify
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WN_Bookings'
  AND COLUMN_NAME IN ('ChallanNumber', 'Validity');

PRINT 'Column fix completed. Now run Challan_Migration.sql.';
GO

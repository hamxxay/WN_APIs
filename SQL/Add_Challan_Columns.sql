-- ══════════════════════════════════════════════════════════════════════════════
-- WorkNest — Add ChallanNumber and Validity columns to WN_Bookings
-- Run this FIRST before Challan_Migration.sql
-- ══════════════════════════════════════════════════════════════════════════════

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'ChallanNumber'
)
    ALTER TABLE dbo.WN_Bookings ADD ChallanNumber NVARCHAR(50) NULL;

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'Validity'
)
    ALTER TABLE dbo.WN_Bookings ADD Validity DATETIME NULL;

PRINT 'ChallanNumber and Validity columns ensured on WN_Bookings.';
GO

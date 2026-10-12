-- ========================================================
-- WorkNest Fee Receipts: Table Alter & Index Script
-- ========================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

-- 1. Alter ReceiptDate from datetime2 to date NOT NULL
IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'WN_FeeReceipts' AND COLUMN_NAME = 'ReceiptDate'
)
BEGIN
    ALTER TABLE dbo.WN_FeeReceipts ALTER COLUMN ReceiptDate date NOT NULL;
END
ELSE
BEGIN
    ALTER TABLE dbo.WN_FeeReceipts ADD ReceiptDate date NOT NULL;
END

-- 2. Add Remarks nvarchar(500) NULL if missing
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'WN_FeeReceipts' AND COLUMN_NAME = 'Remarks'
)
BEGIN
    ALTER TABLE dbo.WN_FeeReceipts ADD Remarks nvarchar(500) NULL;
END

-- 3. Add UpdatedById int NULL if missing
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'WN_FeeReceipts' AND COLUMN_NAME = 'UpdatedById'
)
BEGIN
    ALTER TABLE dbo.WN_FeeReceipts ADD UpdatedById int NULL;
END

-- 4. Add VoidReason nvarchar(500) NULL if missing
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'WN_FeeReceipts' AND COLUMN_NAME = 'VoidReason'
)
BEGIN
    ALTER TABLE dbo.WN_FeeReceipts ADD VoidReason nvarchar(500) NULL;
END

-- 5. Filtered unique index on InvoiceIdGUID WHERE Status = 1 (Active)
IF EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'UQ_WN_FeeReceipts_InvoiceIdGUID_Active' 
      AND object_id = OBJECT_ID('dbo.WN_FeeReceipts')
)
BEGIN
    DROP INDEX UQ_WN_FeeReceipts_InvoiceIdGUID_Active ON dbo.WN_FeeReceipts;
END

CREATE UNIQUE NONCLUSTERED INDEX UQ_WN_FeeReceipts_InvoiceIdGUID_Active
ON dbo.WN_FeeReceipts(InvoiceIdGUID)
WHERE Status = 1;

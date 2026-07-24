-- ============================================================
-- Migration: Rename account columns
--   WN_Bookings.AccountId        → BankAccountId
--   WN_Bookings.DepositAccountId → SecurityDepositAccountId
--   WN_Spaces.DepositAccountId   → SecurityDepositAccountId
--   WN_SpaceTypes.DepositAccountId → SecurityDepositAccountId
-- Run BEFORE re-running Challan_Migration.sql
-- ============================================================
USE [SAC400]
GO

-- ── WN_Bookings ───────────────────────────────────────────────────────────────
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'AccountId')
    EXEC sp_rename 'dbo.WN_Bookings.AccountId', 'BankAccountId', 'COLUMN';
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'DepositAccountId')
    EXEC sp_rename 'dbo.WN_Bookings.DepositAccountId', 'SecurityDepositAccountId', 'COLUMN';
GO

-- ── WN_Spaces ─────────────────────────────────────────────────────────────────
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'DepositAccountId')
    EXEC sp_rename 'dbo.WN_Spaces.DepositAccountId', 'SecurityDepositAccountId', 'COLUMN';
GO

-- ── WN_SpaceTypes ─────────────────────────────────────────────────────────────
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_SpaceTypes') AND name = 'DepositAccountId')
    EXEC sp_rename 'dbo.WN_SpaceTypes.DepositAccountId', 'SecurityDepositAccountId', 'COLUMN';
GO

PRINT 'Column rename migration completed.';
GO

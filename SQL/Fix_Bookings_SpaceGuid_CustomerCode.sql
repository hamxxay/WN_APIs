-- ============================================================
-- Fix: WN_Bookings_Insert
--   1. @SpaceGUID was incorrectly set from SpaceTypeId instead of IdGUID
--   2. CustomerCode was accepted but never stored
-- Run once in SSMS against SAC400
-- ============================================================
USE [SAC400]
GO

-- ── 1. Add CustomerCode column to WN_Bookings (nullable, backward compatible) ──
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'CustomerCode'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings ADD CustomerCode NVARCHAR(50) NULL;
    PRINT 'Column CustomerCode added to WN_Bookings.';
END
ELSE
    PRINT 'Column CustomerCode already exists on WN_Bookings.';
GO

-- ── 2. Fix WN_Bookings_Insert ─────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_Insert
    @UserId        INT,
    @SpaceId       INT,
    @StartDateTime DATETIME,
    @EndDateTime   DATETIME,
    @TotalAmount   DECIMAL(18,2),
    @Notes         NVARCHAR(MAX),
    @CustomerCode  NVARCHAR(50)  = NULL,
    @AccountId     INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserGUID       UNIQUEIDENTIFIER;
    DECLARE @SpaceGUID      UNIQUEIDENTIFIER;
    DECLARE @NewBookingGUID UNIQUEIDENTIFIER = NEWID();

    SELECT @UserGUID  = IdGUID FROM dbo.WN_Users  WHERE Id = @UserId;
    SELECT @SpaceGUID = IdGUID FROM dbo.WN_Spaces WHERE Id = @SpaceId;  -- FIXED: was SpaceTypeId

    INSERT INTO dbo.WN_Bookings
        (IdGUID, BookingDate, UserGuid, SpaceGuid,
         StartDateTime, EndDateTime, TotalAmount,
         BookingStatus, Status, Notes, CustomerCode, AccountId,
         CreatedOn, CreatedBy)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @CustomerCode, @AccountId,
         GETDATE(), @UserGUID);

    SELECT SCOPE_IDENTITY() AS NewId, @NewBookingGUID AS IdGUID;
END
GO

PRINT 'Fix applied successfully.';
GO

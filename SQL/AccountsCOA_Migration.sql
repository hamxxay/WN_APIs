-- ============================================================
-- AccountsCOA Integration Migration
-- Database : SAC400
-- Run once in SSMS against SAC400
-- ============================================================
USE [SAC400]
GO

-- ── 1. Add AccountId column to WN_Bookings (nullable, backward compatible) ──
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'AccountId'
)
BEGIN
    ALTER TABLE dbo.WN_Bookings ADD AccountId INT NULL;
    PRINT 'Column AccountId added to WN_Bookings.';
END
ELSE
    PRINT 'Column AccountId already exists on WN_Bookings.';
GO

-- ── 2. WN_AccountsCOA_GetList ─────────────────────────────────────────────────
-- Returns all accounts from dbo.AccountsCOA sorted alphabetically by Description.
CREATE OR ALTER PROCEDURE dbo.WN_AccountsCOA_GetList
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        AccountId,
        Description
    FROM dbo.AccountsCOA WITH (NOLOCK)
    ORDER BY Description ASC;
END
GO

-- ── 3. WN_AccountsCOA_GetById ─────────────────────────────────────────────────
-- Returns a single account by its primary key.
CREATE OR ALTER PROCEDURE dbo.WN_AccountsCOA_GetById
    @AccountId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        AccountId,
        Description
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE AccountId = @AccountId;
END
GO

-- ── 4. WN_Bookings_Insert — add @CustomerCode and @AccountId ─────────────────
-- Replaces the existing SP. All original logic preserved; two new optional
-- parameters added at the end so existing callers without these params still work.
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

    SELECT @UserGUID  = IdGUID      FROM dbo.WN_Users  WHERE Id = @UserId;
    SELECT @SpaceGUID = SpaceTypeId FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    INSERT INTO dbo.WN_Bookings
        (IdGUID, BookingDate, UserGuid, SpaceGuid,
         StartDateTime, EndDateTime, TotalAmount,
         BookingStatus, Status, Notes, AccountId,
         CreatedOn, CreatedBy)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @AccountId,
         GETDATE(), @UserGUID);

    SELECT SCOPE_IDENTITY() AS NewId, @NewBookingGUID AS IdGUID;
END
GO

-- ── 5. WN_Booking_Create — add @AccountId ────────────────────────────────────
-- Replaces the existing SP from wn_smart_booking_v3.sql.
-- All original logic is preserved exactly; @AccountId is the only addition.
CREATE OR ALTER PROCEDURE dbo.WN_Booking_Create
    @Email             NVARCHAR(255),
    @SpaceCategory     NVARCHAR(20),
    @StartDT           DATETIME,
    @EndDT             DATETIME,
    @Notes             NVARCHAR(MAX)    = '',
    @TotalAmount       DECIMAL(10,2)    = 0,
    @PaymentMethod     NVARCHAR(50)     = NULL,
    @PaymentRef        NVARCHAR(100)    = NULL,
    @Capacity          INT              = NULL,
    @AccountId         INT              = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId            INT;
    DECLARE @UserGuid          UNIQUEIDENTIFIER;
    DECLARE @SpaceId           INT;
    DECLARE @SpaceGuid         UNIQUEIDENTIFIER;
    DECLARE @SpaceTypeId       INT;
    DECLARE @STGuid            UNIQUEIDENTIFIER;
    DECLARE @BookingId         INT;
    DECLARE @BookingGuid       UNIQUEIDENTIFIER;
    DECLARE @AssignedSpaceId   INT;
    DECLARE @AssignedSpaceName NVARCHAR(255);
    DECLARE @AssignedSpaceCode NVARCHAR(20);

    SELECT @UserId = Id, @UserGuid = IdGUID
    FROM dbo.WN_Users
    WHERE Email = @Email;

    IF @UserId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               'User not found' AS errorMessage;
        RETURN;
    END

    SELECT @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               'Unknown SpaceCategory' AS errorMessage;
        RETURN;
    END

    SELECT @STGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    BEGIN TRANSACTION;

    SELECT TOP 1
        @SpaceId           = s.Id,
        @SpaceGuid         = s.IdGUID,
        @AssignedSpaceName = s.Name,
        @AssignedSpaceCode = s.Code
    FROM  dbo.WN_Spaces     s WITH (UPDLOCK)
    JOIN  dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    WHERE s.Status = 1
      AND s.SpaceTypeId = @STGuid
      AND (@Capacity IS NULL OR st.Capacity >= @Capacity)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk
            WHERE bk.SpaceGuid = s.IdGUID
              AND bk.BookingStatus IN (1, 4)
              AND @StartDT < bk.EndDateTime
              AND @EndDT   > bk.StartDateTime
          )
    ORDER BY TRY_CAST(s.Code AS INT) ASC, s.Id ASC;

    IF @SpaceId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               'No available space for the requested period' AS errorMessage;
        RETURN;
    END

    SET @BookingGuid = NEWID();

    INSERT INTO dbo.WN_Bookings
        (IdGUID, UserGuid, SpaceGuid, StartDateTime, EndDateTime,
         Notes, TotalAmount, BookingStatus, Status,
         AccountId, BookingDate, CreatedOn, CreatedBy)
    VALUES
        (@BookingGuid, @UserGuid, @SpaceGuid, @StartDT, @EndDT,
         @Notes, @TotalAmount, 1, 1,
         @AccountId, GETUTCDATE(), GETUTCDATE(), @UserGuid);

    SET @BookingId       = SCOPE_IDENTITY();
    SET @AssignedSpaceId = @SpaceId;

    IF @PaymentMethod IS NOT NULL AND @TotalAmount > 0
    BEGIN
        INSERT INTO dbo.WN_Payments
            (IdGUID, UserId, BookingId, Amount, Currency,
             PaymentMethod, PaymentStatus, TransactionRef, CreatedAt)
        VALUES
            (NEWID(), @UserGuid, @BookingGuid, @TotalAmount, 'PKR',
             @PaymentMethod, 'Pending', @PaymentRef, GETUTCDATE());
    END

    COMMIT TRANSACTION;

    SELECT
        @BookingId                         AS bookingId,
        CAST(@BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        @AssignedSpaceId                   AS assignedSpaceId,
        @AssignedSpaceName                 AS assignedSpaceName,
        @AssignedSpaceCode                 AS assignedSpaceCode,
        NULL                               AS errorMessage;
END
GO

PRINT 'AccountsCOA migration completed successfully.';
GO

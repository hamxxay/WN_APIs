-- ============================================================
-- Migration: Space Account Mapping
-- Adds RentAccountId and DepositAccountId to WN_Spaces.
-- Bookings automatically inherit accounts from the space.
-- Run once in SSMS against SAC400
-- ============================================================
USE [SAC400]
GO

-- ── 1. Add columns to WN_Spaces ──────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'RentAccountId')
    ALTER TABLE dbo.WN_Spaces ADD RentAccountId INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Spaces') AND name = 'DepositAccountId')
    ALTER TABLE dbo.WN_Spaces ADD DepositAccountId INT NULL;
GO

-- ── 2. WN_Spaces_Insert — accept and store account IDs ───────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Spaces_Insert
    @Name           NVARCHAR(255),
    @LocationId     UNIQUEIDENTIFIER,
    @SpaceTypeId    UNIQUEIDENTIFIER,
    @Code           NVARCHAR(50)     = NULL,
    @Description    NVARCHAR(MAX)    = NULL,
    @FloorId        INT              = NULL,
    @PricePerDay    DECIMAL(18,2)    = NULL,
    @PricePerHour   DECIMAL(18,2)    = NULL,
    @PricePerMonth  DECIMAL(18,2)    = NULL,
    @ImageUrl       NVARCHAR(500)    = NULL,
    @Amenities      NVARCHAR(MAX)    = NULL,
    @RentAccountId  INT              = NULL,
    @DepositAccountId INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Auto-null DepositAccountId if space type is not Private Office
    IF @DepositAccountId IS NOT NULL
    BEGIN
        DECLARE @SpaceTypeName NVARCHAR(100);
        SELECT @SpaceTypeName = Description FROM dbo.WN_SpaceTypes WHERE IdGUID = @SpaceTypeId;
        IF @SpaceTypeName NOT LIKE '%Private%'
            SET @DepositAccountId = NULL;
    END

    DECLARE @NewGuid UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.WN_Spaces
        (IdGUID, Name, LocationId, SpaceTypeId, Code, Description, FloorId,
         PricePerDay, PricePerHour, PricePerMonth, ImageUrl, Amenities,
         RentAccountId, DepositAccountId, Status, CreatedOn)
    VALUES
        (@NewGuid, @Name, @LocationId, @SpaceTypeId, @Code, @Description, @FloorId,
         @PricePerDay, @PricePerHour, @PricePerMonth, @ImageUrl, @Amenities,
         @RentAccountId, @DepositAccountId, 1, GETDATE());

    SELECT SCOPE_IDENTITY() AS Id, @NewGuid AS IdGUID;
END
GO

-- ── 3. WN_Spaces_Update — accept and store account IDs ───────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Spaces_Update
    @IdGUID         UNIQUEIDENTIFIER,
    @Name           NVARCHAR(255)    = NULL,
    @LocationId     UNIQUEIDENTIFIER = NULL,
    @SpaceTypeId    UNIQUEIDENTIFIER = NULL,
    @Code           NVARCHAR(50)     = NULL,
    @Description    NVARCHAR(MAX)    = NULL,
    @FloorId        INT              = NULL,
    @PricePerDay    DECIMAL(18,2)    = NULL,
    @PricePerHour   DECIMAL(18,2)    = NULL,
    @PricePerMonth  DECIMAL(18,2)    = NULL,
    @ImageUrl       NVARCHAR(500)    = NULL,
    @Amenities      NVARCHAR(MAX)    = NULL,
    @RentAccountId  INT              = NULL,
    @DepositAccountId INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Auto-null DepositAccountId if space type is not Private Office
    IF @DepositAccountId IS NOT NULL AND @SpaceTypeId IS NOT NULL
    BEGIN
        DECLARE @SpaceTypeName NVARCHAR(100);
        SELECT @SpaceTypeName = Description FROM dbo.WN_SpaceTypes WHERE IdGUID = @SpaceTypeId;
        IF @SpaceTypeName NOT LIKE '%Private%'
            SET @DepositAccountId = NULL;
    END

    UPDATE dbo.WN_Spaces SET
        Name             = ISNULL(@Name,          Name),
        LocationId       = ISNULL(@LocationId,    LocationId),
        SpaceTypeId      = ISNULL(@SpaceTypeId,   SpaceTypeId),
        Code             = ISNULL(@Code,          Code),
        Description      = ISNULL(@Description,   Description),
        FloorId          = ISNULL(@FloorId,        FloorId),
        PricePerDay      = ISNULL(@PricePerDay,    PricePerDay),
        PricePerHour     = ISNULL(@PricePerHour,   PricePerHour),
        PricePerMonth    = ISNULL(@PricePerMonth,  PricePerMonth),
        ImageUrl         = ISNULL(@ImageUrl,       ImageUrl),
        Amenities        = ISNULL(@Amenities,      Amenities),
        RentAccountId    = ISNULL(@RentAccountId,  RentAccountId),
        DepositAccountId = @DepositAccountId   -- allow explicit NULL to clear it
    WHERE IdGUID = @IdGUID;
END
GO

-- ── 4. WN_Spaces_GetList — include account IDs in result ─────────────────────
-- (Only alter if the SP exists; adjust SELECT to add the two columns)
-- NOTE: Run this only if your WN_Spaces_GetList does not already return these columns.
-- If you manage this SP separately, just add RentAccountId, DepositAccountId to its SELECT.
PRINT 'Add RentAccountId and DepositAccountId to WN_Spaces_GetList SELECT manually if needed.';
GO

-- ── 5. WN_Bookings_Insert — auto-read accounts from space ────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_Insert
    @UserId        INT,
    @SpaceId       INT,
    @StartDateTime DATETIME,
    @EndDateTime   DATETIME,
    @TotalAmount   DECIMAL(18,2),
    @Notes         NVARCHAR(MAX),
    @CustomerCode  NVARCHAR(50)  = NULL,
    @AccountId     INT           = NULL   -- kept for backward compat, ignored (read from space)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserGUID        UNIQUEIDENTIFIER;
    DECLARE @SpaceGUID       UNIQUEIDENTIFIER;
    DECLARE @RentAccountId   INT;
    DECLARE @DepositAccountId INT;
    DECLARE @NewBookingGUID  UNIQUEIDENTIFIER = NEWID();

    SELECT @UserGUID  = IdGUID FROM dbo.WN_Users  WHERE Id = @UserId;
    SELECT @SpaceGUID = IdGUID, @RentAccountId = RentAccountId, @DepositAccountId = DepositAccountId
    FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    INSERT INTO dbo.WN_Bookings
        (IdGUID, BookingDate, UserGuid, SpaceGuid,
         StartDateTime, EndDateTime, TotalAmount,
         BookingStatus, Status, Notes, CustomerCode,
         AccountId, DepositAccountId,
         CreatedOn, CreatedBy)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @CustomerCode,
         @RentAccountId, @DepositAccountId,
         GETDATE(), @UserGUID);

    SELECT SCOPE_IDENTITY() AS NewId, @NewBookingGUID AS IdGUID,
           @RentAccountId AS RentAccountId, @DepositAccountId AS DepositAccountId;
END
GO

-- ── 6. WN_Booking_Create — auto-read accounts from space ─────────────────────
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
    @AccountId         INT              = NULL   -- kept for backward compat, ignored
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
    DECLARE @RentAccountId     INT;
    DECLARE @DepositAccountId  INT;

    SELECT @UserId = Id, @UserGuid = IdGUID FROM dbo.WN_Users WHERE Email = @Email;

    IF @UserId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               'User not found' AS errorMessage;
        RETURN;
    END

    SELECT @SpaceTypeId = SpaceTypeId FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;

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
        @AssignedSpaceCode = s.Code,
        @RentAccountId     = s.RentAccountId,
        @DepositAccountId  = s.DepositAccountId
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
         AccountId, DepositAccountId, BookingDate, CreatedOn, CreatedBy)
    VALUES
        (@BookingGuid, @UserGuid, @SpaceGuid, @StartDT, @EndDT,
         @Notes, @TotalAmount, 1, 1,
         @RentAccountId, @DepositAccountId, GETUTCDATE(), GETUTCDATE(), @UserGuid);

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
        @RentAccountId                     AS rentAccountId,
        @DepositAccountId                  AS depositAccountId,
        NULL                               AS errorMessage;
END
GO

-- ── 7. Add DepositAccountId column to WN_Bookings ────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Bookings') AND name = 'DepositAccountId')
    ALTER TABLE dbo.WN_Bookings ADD DepositAccountId INT NULL;
GO

PRINT 'Space account mapping migration completed successfully.';
GO

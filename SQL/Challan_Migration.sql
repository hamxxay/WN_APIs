-- ══════════════════════════════════════════════════════════════════════════════
-- WorkNest — Challan Number & Validity wiring
-- Columns ChallanNumber and Validity already exist in WN_Bookings.
-- This script updates the Insert SP to auto-populate them and adds a lookup SP.
-- Run once in SSMS against your WorkNest database.
-- ══════════════════════════════════════════════════════════════════════════════

-- ── 1. WN_Bookings_Insert — generate ChallanNumber + Validity ────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_Insert
    @UserId        INT,
    @SpaceId       INT,
    @StartDateTime DATETIME,
    @EndDateTime   DATETIME,
    @TotalAmount   DECIMAL(18,2),
    @Notes         NVARCHAR(MAX),
    @CustomerCode  NVARCHAR(50)  = NULL,
    @AccountId     INT           = NULL   -- kept for backward compat, ignored
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserGUID         UNIQUEIDENTIFIER;
    DECLARE @SpaceGUID        UNIQUEIDENTIFIER;
    DECLARE @RentAccountId    INT;
    DECLARE @DepositAccountId INT;
    DECLARE @NewBookingGUID   UNIQUEIDENTIFIER = NEWID();
    DECLARE @SpaceName        NVARCHAR(255);
    DECLARE @SpaceCode        NVARCHAR(50);
    DECLARE @SpaceTypeName    NVARCHAR(100);
    DECLARE @UserEmail        NVARCHAR(255);
    DECLARE @UserName         NVARCHAR(255);
    DECLARE @SecurityDeposit  DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber    NVARCHAR(50);
    DECLARE @Validity         DATETIME;
    DECLARE @NewId            INT;
    DECLARE @DatePart         NVARCHAR(8);
    DECLARE @SeqNum           INT;

    SELECT @UserGUID = IdGUID, @UserEmail = Email, @UserName = Name
    FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT @SpaceGUID = IdGUID, @RentAccountId = RentAccountId,
           @DepositAccountId = DepositAccountId,
           @SpaceName = Name, @SpaceCode = Code
    FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    SELECT TOP 1 @SpaceTypeName = sc.SpaceCategory
    FROM dbo.WN_SpaceConfig sc
    JOIN dbo.WN_SpaceTypes st ON st.Id = sc.SpaceTypeId
    JOIN dbo.WN_Spaces s ON s.SpaceTypeId = st.IdGUID
    WHERE s.Id = @SpaceId;

    IF @DepositAccountId IS NOT NULL
        SELECT TOP 1 @SecurityDeposit = ISNULL(SecurityDeposit, 0)
        FROM dbo.WN_SpaceConfig
        WHERE SpaceCategory = @SpaceTypeName;

    -- Generate challan: WN-YYYYMMDD-NNNNNN (sequential per day)
    SET @DatePart = CONVERT(NVARCHAR(8), GETDATE(), 112);
    SELECT @SeqNum = COUNT(*) + 1
    FROM dbo.WN_Bookings
    WHERE CONVERT(NVARCHAR(8), ISNULL(BookingDate, CreatedOn), 112) = @DatePart;
    SET @ChallanNumber = 'WN-' + @DatePart + '-' + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @Validity = DATEADD(DAY, 5, GETDATE());

    -- Add ChallanNumber and Validity columns if they don't exist
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'ChallanNumber')
        ALTER TABLE dbo.WN_Bookings ADD ChallanNumber NVARCHAR(50) NULL;
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'Validity')
        ALTER TABLE dbo.WN_Bookings ADD Validity DATETIME NULL;

    INSERT INTO dbo.WN_Bookings
        (IdGUID, BookingDate, UserGuid, SpaceGuid,
         StartDateTime, EndDateTime, TotalAmount,
         BookingStatus, Status, Notes, CustomerCode,
         AccountId, DepositAccountId,
         ChallanNumber, Validity,
         CreatedOn, CreatedBy)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @CustomerCode,
         @RentAccountId, @DepositAccountId,
         @ChallanNumber, @Validity,
         GETDATE(), @UserGUID);

    SET @NewId = SCOPE_IDENTITY();

    EXEC dbo.WN_BookingDetails_Insert
        @BookingGuid      = @NewBookingGUID,
        @CustomerCode     = @CustomerCode,
        @CustomerName     = @UserName,
        @CustomerEmail    = @UserEmail,
        @SpaceName        = @SpaceName,
        @SpaceCode        = @SpaceCode,
        @SpaceCategory    = @SpaceTypeName,
        @StartDateTime    = @StartDateTime,
        @EndDateTime      = @EndDateTime,
        @RentAmount       = @TotalAmount,
        @SecurityDeposit  = @SecurityDeposit,
        @RentAccountId    = @RentAccountId,
        @DepositAccountId = @DepositAccountId,
        @Notes            = @Notes;

    SELECT @NewId            AS NewId,
           @NewBookingGUID   AS IdGUID,
           @RentAccountId    AS RentAccountId,
           @DepositAccountId AS DepositAccountId,
           @ChallanNumber    AS ChallanNumber,
           @Validity         AS Validity,
           @SecurityDeposit  AS SecurityDeposit;
END
GO

-- ── 2. WN_Booking_Create — generate ChallanNumber + Validity ─────────────────
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
    DECLARE @UserName          NVARCHAR(255);
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
    DECLARE @SecurityDeposit   DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber     NVARCHAR(50);
    DECLARE @Validity          DATETIME;
    DECLARE @DatePart          NVARCHAR(8);
    DECLARE @SeqNum            INT;

    SELECT @UserId = Id, @UserGuid = IdGUID, @UserName = Name
    FROM dbo.WN_Users WHERE Email = @Email;

    IF @UserId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS challanNumber, NULL AS validity, NULL AS securityDeposit,
               'User not found' AS errorMessage;
        RETURN;
    END

    SELECT @SpaceTypeId = SpaceTypeId, @SecurityDeposit = ISNULL(SecurityDeposit, 0)
    FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS challanNumber, NULL AS validity, NULL AS securityDeposit,
               'Unknown SpaceCategory' AS errorMessage;
        RETURN;
    END

    SELECT @STGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    -- Generate challan
    SET @DatePart = CONVERT(NVARCHAR(8), GETDATE(), 112);
    SELECT @SeqNum = COUNT(*) + 1
    FROM dbo.WN_Bookings
    WHERE CONVERT(NVARCHAR(8), ISNULL(BookingDate, CreatedOn), 112) = @DatePart;
    SET @ChallanNumber = 'WN-' + @DatePart + '-' + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @Validity = DATEADD(DAY, 5, GETDATE());

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
               NULL AS challanNumber, NULL AS validity, NULL AS securityDeposit,
               'No available space for the requested period' AS errorMessage;
        RETURN;
    END

    SET @BookingGuid = NEWID();

    -- Add ChallanNumber and Validity columns if they don't exist
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'ChallanNumber')
        ALTER TABLE dbo.WN_Bookings ADD ChallanNumber NVARCHAR(50) NULL;
    IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WN_Bookings' AND COLUMN_NAME = 'Validity')
        ALTER TABLE dbo.WN_Bookings ADD Validity DATETIME NULL;

    INSERT INTO dbo.WN_Bookings
        (IdGUID, UserGuid, SpaceGuid, StartDateTime, EndDateTime,
         Notes, TotalAmount, BookingStatus, Status,
         AccountId, DepositAccountId,
         ChallanNumber, Validity,
         BookingDate, CreatedOn, CreatedBy)
    VALUES
        (@BookingGuid, @UserGuid, @SpaceGuid, @StartDT, @EndDT,
         @Notes, @TotalAmount, 1, 1,
         @RentAccountId, @DepositAccountId,
         @ChallanNumber, @Validity,
         GETUTCDATE(), GETUTCDATE(), @UserGuid);

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

    EXEC dbo.WN_BookingDetails_Insert
        @BookingGuid      = @BookingGuid,
        @CustomerName     = @UserName,
        @CustomerEmail    = @Email,
        @SpaceName        = @AssignedSpaceName,
        @SpaceCode        = @AssignedSpaceCode,
        @SpaceCategory    = @SpaceCategory,
        @StartDateTime    = @StartDT,
        @EndDateTime      = @EndDT,
        @RentAmount       = @TotalAmount,
        @SecurityDeposit  = @SecurityDeposit,
        @RentAccountId    = @RentAccountId,
        @DepositAccountId = @DepositAccountId,
        @PaymentMethod    = @PaymentMethod,
        @Notes            = @Notes;

    COMMIT TRANSACTION;

    SELECT
        @BookingId                         AS bookingId,
        CAST(@BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        @AssignedSpaceId                   AS assignedSpaceId,
        @AssignedSpaceName                 AS assignedSpaceName,
        @AssignedSpaceCode                 AS assignedSpaceCode,
        @RentAccountId                     AS rentAccountId,
        @DepositAccountId                  AS depositAccountId,
        @SecurityDeposit                   AS securityDeposit,
        @ChallanNumber                     AS challanNumber,
        @Validity                          AS validity,
        NULL                               AS errorMessage;
END
GO

-- ── 3. WN_Bookings_GetByChallan ───────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_GetByChallan
    @ChallanNumber NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        b.Id,
        CAST(b.IdGUID        AS NVARCHAR(36)) AS idGuid,
        b.ChallanNumber,
        b.Validity,
        b.TotalAmount,
        b.BookingStatus,
        b.StartDateTime,
        b.EndDateTime,
        b.Notes,
        b.CustomerCode,
        -- space
        s.Name          AS spaceName,
        s.Code          AS spaceCode,
        st.Name         AS spaceTypeName,
        l.Name          AS locationName,
        -- user
        u.Name          AS customerName,
        u.Email         AS customerEmail,
        -- booking details amounts
        bd.RentAmount,
        bd.SecurityDeposit,
        bd.TotalAmount  AS totalCharged,
        bd.PaymentMethod,
        bd.PaymentStatus,
        -- accounts
        ra.Description  AS rentAccountName,
        da.Description  AS depositAccountName
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces      s  WITH (NOLOCK) ON s.IdGUID    = b.SpaceGuid
    LEFT JOIN dbo.WN_SpaceTypes  st WITH (NOLOCK) ON st.IdGUID   = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations   l  WITH (NOLOCK) ON l.IdGUID    = s.LocationId
    LEFT JOIN dbo.WN_Users       u  WITH (NOLOCK) ON u.IdGUID    = b.UserGuid
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.AccountsCOA    ra WITH (NOLOCK) ON ra.AccountId = bd.RentAccountId
    LEFT JOIN dbo.AccountsCOA    da WITH (NOLOCK) ON da.AccountId = bd.DepositAccountId
    WHERE b.ChallanNumber = @ChallanNumber;
END
GO

-- ── 4. WN_Bookings_GetByGuid — include challan fields ────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_GetByGuid
    @IdGUID NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        b.Id,
        CAST(b.IdGUID        AS NVARCHAR(36)) AS idGuid,
        b.ChallanNumber,
        b.Validity,
        b.TotalAmount,
        b.BookingStatus,
        b.StartDateTime,
        b.EndDateTime,
        b.Notes,
        b.CustomerCode,
        s.Name          AS spaceName,
        s.Code          AS spaceCode,
        st.Name         AS spaceTypeName,
        l.Name          AS locationName,
        u.Name          AS customerName,
        u.Email         AS customerEmail,
        bd.RentAmount,
        bd.SecurityDeposit,
        bd.TotalAmount  AS totalCharged,
        bd.PaymentMethod,
        bd.PaymentStatus,
        ra.Description  AS rentAccountName,
        da.Description  AS depositAccountName
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces      s  WITH (NOLOCK) ON s.IdGUID    = b.SpaceGuid
    LEFT JOIN dbo.WN_SpaceTypes  st WITH (NOLOCK) ON st.IdGUID   = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations   l  WITH (NOLOCK) ON l.IdGUID    = s.LocationId
    LEFT JOIN dbo.WN_Users       u  WITH (NOLOCK) ON u.IdGUID    = b.UserGuid
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.AccountsCOA    ra WITH (NOLOCK) ON ra.AccountId = bd.RentAccountId
    LEFT JOIN dbo.AccountsCOA    da WITH (NOLOCK) ON da.AccountId = bd.DepositAccountId
    WHERE CAST(b.IdGUID AS NVARCHAR(36)) = @IdGUID;
END
GO

PRINT 'Challan migration completed successfully.';
GO

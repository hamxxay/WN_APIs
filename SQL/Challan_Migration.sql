-- ══════════════════════════════════════════════════════════════════════════════
-- WorkNest — Fix Challan SPs to use correct column name ValidityDate
-- Run in SSMS against your WorkNest database.
-- ══════════════════════════════════════════════════════════════════════════════

-- ── 1. WN_Bookings_Insert ─────────────────────────────────────────────────────
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

    DECLARE @UserGUID         UNIQUEIDENTIFIER;
    DECLARE @SpaceGUID        UNIQUEIDENTIFIER;
    DECLARE @RentAccountId           INT;
    DECLARE @SecurityDepositAccountId INT;
    DECLARE @NewBookingGUID           UNIQUEIDENTIFIER = NEWID();
    DECLARE @SpaceName        NVARCHAR(255);
    DECLARE @SpaceCode        NVARCHAR(50);
    DECLARE @SpaceTypeName    NVARCHAR(100);
    DECLARE @UserEmail        NVARCHAR(255);
    DECLARE @UserName         NVARCHAR(255);
    DECLARE @SecurityDeposit  DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber    NVARCHAR(50);
    DECLARE @ValidityDate     DATETIME;
    DECLARE @NewId            INT;
    DECLARE @DatePart         NVARCHAR(8);
    DECLARE @SeqNum           INT;

    SELECT @UserGUID = IdGUID, @UserEmail = Email, @UserName = Name
    FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT @SpaceGUID = IdGUID, @RentAccountId = RentAccountId,
           @SecurityDepositAccountId = SecurityDepositAccountId,
           @SpaceName = Name, @SpaceCode = Code
    FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    SELECT TOP 1 @SpaceTypeName = sc.SpaceCategory
    FROM dbo.WN_SpaceConfig sc
    JOIN dbo.WN_SpaceTypes st ON st.Id = sc.SpaceTypeId
    JOIN dbo.WN_Spaces s ON s.SpaceTypeId = st.IdGUID
    WHERE s.Id = @SpaceId;

    IF @SecurityDepositAccountId IS NOT NULL
        SELECT TOP 1 @SecurityDeposit = ISNULL(SecurityDeposit, 0)
        FROM dbo.WN_SpaceConfig
        WHERE SpaceCategory = @SpaceTypeName;

    SET @DatePart = CONVERT(NVARCHAR(8), GETDATE(), 112);
    SELECT @SeqNum = COUNT(*) + 1
    FROM dbo.WN_Bookings
    WHERE CONVERT(NVARCHAR(8), ISNULL(BookingDate, CreatedOn), 112) = @DatePart;
    SET @ChallanNumber = 'WN-' + @DatePart + '-' + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @ValidityDate  = DATEADD(DAY, 5, GETDATE());

    INSERT INTO dbo.WN_Bookings
        (IdGUID, BookingDate, UserGuid, SpaceGuid,
         StartDateTime, EndDateTime, TotalAmount,
         BookingStatus, Status, Notes, CustomerCode,
         BankAccountId, SecurityDepositAccountId,
         ChallanNumber, ValidityDate,
         CreatedOn, CreatedBy)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @CustomerCode,
         @RentAccountId, @SecurityDepositAccountId,
         @ChallanNumber, @ValidityDate,
         GETDATE(), @UserGUID);

    SET @NewId = SCOPE_IDENTITY();

    -- Insert RoomRent fee line
    EXEC dbo.WN_BookingDetails_InsertLine
        @BookingGuid = @NewBookingGUID,
        @FeeType     = 'RoomRent',
        @Amount      = @TotalAmount,
        @AccountId   = @RentAccountId,
        @CreatedBy   = @UserEmail;

    -- Insert SecurityDeposit fee line only if applicable
    IF @SecurityDeposit > 0 AND @SecurityDepositAccountId IS NOT NULL
        EXEC dbo.WN_BookingDetails_InsertLine
            @BookingGuid = @NewBookingGUID,
            @FeeType     = 'SecurityDeposit',
            @Amount      = @SecurityDeposit,
            @AccountId   = @SecurityDepositAccountId,
            @CreatedBy   = @UserEmail;

    SELECT @NewId                    AS NewId,
           @NewBookingGUID           AS IdGUID,
           @RentAccountId            AS RentAccountId,
           @SecurityDepositAccountId AS SecurityDepositAccountId,
           @ChallanNumber    AS ChallanNumber,
           @ValidityDate     AS Validity,
           @SecurityDeposit  AS SecurityDeposit;
END
GO

-- ── 2. WN_Booking_Create ──────────────────────────────────────────────────────
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
    DECLARE @RentAccountId            INT;
    DECLARE @SecurityDepositAccountId INT;
    DECLARE @SecurityDeposit   DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber     NVARCHAR(50);
    DECLARE @ValidityDate      DATETIME;
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

    SET @DatePart = CONVERT(NVARCHAR(8), GETDATE(), 112);
    SELECT @SeqNum = COUNT(*) + 1
    FROM dbo.WN_Bookings
    WHERE CONVERT(NVARCHAR(8), ISNULL(BookingDate, CreatedOn), 112) = @DatePart;
    SET @ChallanNumber = 'WN-' + @DatePart + '-' + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @ValidityDate  = DATEADD(DAY, 5, GETDATE());

    BEGIN TRANSACTION;

    SELECT TOP 1
        @SpaceId           = s.Id,
        @SpaceGuid         = s.IdGUID,
        @AssignedSpaceName = s.Name,
        @AssignedSpaceCode = s.Code,
        @RentAccountId            = s.RentAccountId,
        @SecurityDepositAccountId = s.SecurityDepositAccountId
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

    INSERT INTO dbo.WN_Bookings
        (IdGUID, UserGuid, SpaceGuid, StartDateTime, EndDateTime,
         Notes, TotalAmount, BookingStatus, Status,
         BankAccountId, SecurityDepositAccountId,
         ChallanNumber, ValidityDate,
         BookingDate, CreatedOn, CreatedBy)
    VALUES
        (@BookingGuid, @UserGuid, @SpaceGuid, @StartDT, @EndDT,
         @Notes, @TotalAmount, 1, 1,
         @RentAccountId, @SecurityDepositAccountId,
         @ChallanNumber, @ValidityDate,
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

    -- Insert RoomRent fee line
    EXEC dbo.WN_BookingDetails_InsertLine
        @BookingGuid = @BookingGuid,
        @FeeType     = 'RoomRent',
        @Amount      = @TotalAmount,
        @AccountId   = @RentAccountId,
        @CreatedBy   = @Email;

    -- Insert SecurityDeposit fee line only if applicable
    IF @SecurityDeposit > 0 AND @SecurityDepositAccountId IS NOT NULL
        EXEC dbo.WN_BookingDetails_InsertLine
            @BookingGuid = @BookingGuid,
            @FeeType     = 'SecurityDeposit',
            @Amount      = @SecurityDeposit,
            @AccountId   = @SecurityDepositAccountId,
            @CreatedBy   = @Email;

    COMMIT TRANSACTION;

    SELECT
        @BookingId                         AS bookingId,
        CAST(@BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        @AssignedSpaceId                   AS assignedSpaceId,
        @AssignedSpaceName                 AS assignedSpaceName,
        @AssignedSpaceCode                 AS assignedSpaceCode,
        @RentAccountId                     AS rentAccountId,
        @SecurityDepositAccountId          AS securityDepositAccountId,
        @SecurityDeposit                   AS securityDeposit,
        @ChallanNumber                     AS challanNumber,
        @ValidityDate                      AS validity,
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
        CAST(b.IdGUID AS NVARCHAR(36)) AS idGuid,
        b.ChallanNumber,
        b.ValidityDate,
        b.TotalAmount,
        b.BookingStatus,
        b.StartDateTime,
        b.EndDateTime,
        b.Notes,
        b.CustomerCode,
        s.Name         AS spaceName,
        s.Code         AS spaceCode,
        st.Name        AS spaceTypeName,
        l.Name         AS locationName,
        u.Name         AS customerName,
        u.Email        AS customerEmail
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces     s  WITH (NOLOCK) ON s.IdGUID  = b.SpaceGuid
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.IdGUID = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.IdGUID  = s.LocationId
    LEFT JOIN dbo.WN_Users      u  WITH (NOLOCK) ON u.IdGUID  = b.UserGuid
    WHERE b.ChallanNumber = @ChallanNumber;
END
GO

-- ── 4. WN_Bookings_GetByGuid ──────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Bookings_GetByGuid
    @IdGUID NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        b.Id,
        CAST(b.IdGUID AS NVARCHAR(36)) AS idGuid,
        b.ChallanNumber,
        b.ValidityDate,
        b.TotalAmount,
        b.BookingStatus,
        b.StartDateTime,
        b.EndDateTime,
        b.Notes,
        b.CustomerCode,
        s.Name         AS spaceName,
        s.Code         AS spaceCode,
        st.Name        AS spaceTypeName,
        l.Name         AS locationName,
        u.Name         AS customerName,
        u.Email        AS customerEmail
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces     s  WITH (NOLOCK) ON s.IdGUID  = b.SpaceGuid
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.IdGUID = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.IdGUID  = s.LocationId
    LEFT JOIN dbo.WN_Users      u  WITH (NOLOCK) ON u.IdGUID  = b.UserGuid
    WHERE CAST(b.IdGUID AS NVARCHAR(36)) = @IdGUID;
END
GO

-- ── 5. WN_Payments_GetMyList ──────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Payments_GetMyList
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserGuid UNIQUEIDENTIFIER;
    SELECT @UserGuid = IdGUID FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT
        p.Id,
        CAST(p.IdGUID AS NVARCHAR(36)) AS IdGuid,
        p.Amount,
        p.PaymentMethod,
        p.PaymentStatus,
        p.TransactionRef,
        p.CreatedAt                          AS PaidAt,
        b.ChallanNumber,
        b.ValidityDate                       AS Validity,
        b.StartDateTime,
        b.EndDateTime,
        s.Name                               AS SpaceName
    FROM dbo.WN_Payments p WITH (NOLOCK)
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK)
        ON CAST(b.IdGUID AS NVARCHAR(36)) = CAST(p.BookingId AS NVARCHAR(36))
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK)
        ON CAST(s.IdGUID AS NVARCHAR(36)) = CAST(b.SpaceGuid AS NVARCHAR(36))
    WHERE (
        p.UserId = @UserGuid
        OR CAST(p.UserId AS NVARCHAR(36)) = CAST(@UserId AS NVARCHAR(10))
    )
    AND ISNULL(p.PaymentStatus, '') <> 'Deleted'
    ORDER BY p.CreatedAt DESC;
END
GO

PRINT 'All Challan SPs updated to use ValidityDate column.';
GO

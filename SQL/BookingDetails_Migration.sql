-- ============================================================
-- Migration: WN_BookingDetails
-- Central table that stores all rent and deposit amounts per booking.
-- All other tables (WN_Bookings, WN_Payments) reference this for amounts.
-- Run once in SSMS against SAC400
-- ============================================================
USE [SAC400]
GO

-- ── 1. Create WN_BookingDetails table ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND type = 'U')
BEGIN
    CREATE TABLE dbo.WN_BookingDetails (
        Id                INT IDENTITY(1,1) PRIMARY KEY,
        IdGUID            UNIQUEIDENTIFIER  NOT NULL DEFAULT NEWID(),
        BookingGuid       UNIQUEIDENTIFIER  NOT NULL,   -- FK → WN_Bookings.IdGUID
        CustomerCode      NVARCHAR(50)      NULL,
        CustomerName      NVARCHAR(255)     NULL,
        CustomerEmail     NVARCHAR(255)     NULL,
        SpaceName         NVARCHAR(255)     NULL,
        SpaceCode         NVARCHAR(50)      NULL,
        SpaceCategory     NVARCHAR(50)      NULL,
        StartDateTime     DATETIME          NULL,
        EndDateTime       DATETIME          NULL,
        RentAmount        DECIMAL(18,2)     NOT NULL DEFAULT 0,
        SecurityDeposit   DECIMAL(18,2)     NOT NULL DEFAULT 0,
        TotalAmount       DECIMAL(18,2)     NOT NULL DEFAULT 0,   -- RentAmount + SecurityDeposit
        RentAccountId     INT               NULL,
        DepositAccountId  INT               NULL,
        PaymentMethod     NVARCHAR(50)      NULL,
        PaymentStatus     NVARCHAR(50)      NOT NULL DEFAULT 'Pending',
        Notes             NVARCHAR(MAX)     NULL,
        CreatedOn         DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        Status            INT               NOT NULL DEFAULT 1
    );
    PRINT 'Table WN_BookingDetails created.';
END
ELSE
    PRINT 'Table WN_BookingDetails already exists.';
GO

-- ── 2. WN_BookingDetails_GetList ─────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_GetList
    @Page   INT = 1,
    @Limit  INT = 100,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        bd.Id,
        CAST(bd.IdGUID       AS NVARCHAR(36)) AS idGuid,
        CAST(bd.BookingGuid  AS NVARCHAR(36)) AS bookingGuid,
        bd.CustomerCode,
        bd.CustomerName,
        bd.CustomerEmail,
        bd.SpaceName,
        bd.SpaceCode,
        bd.SpaceCategory,
        bd.StartDateTime,
        bd.EndDateTime,
        bd.RentAmount,
        bd.SecurityDeposit,
        bd.TotalAmount,
        bd.RentAccountId,
        bd.DepositAccountId,
        ra.Description  AS RentAccountName,
        da.Description  AS DepositAccountName,
        bd.PaymentMethod,
        bd.PaymentStatus,
        bd.Notes,
        bd.CreatedOn
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA ra WITH (NOLOCK) ON ra.AccountId = bd.RentAccountId
    LEFT JOIN dbo.AccountsCOA da WITH (NOLOCK) ON da.AccountId = bd.DepositAccountId
    WHERE bd.Status = 1
      AND (@Search IS NULL OR @Search = ''
           OR bd.CustomerName  LIKE '%' + @Search + '%'
           OR bd.CustomerEmail LIKE '%' + @Search + '%'
           OR bd.SpaceName     LIKE '%' + @Search + '%'
           OR bd.SpaceCode     LIKE '%' + @Search + '%'
           OR bd.CustomerCode  LIKE '%' + @Search + '%')
    ORDER BY bd.CreatedOn DESC
    OFFSET (@Page - 1) * @Limit ROWS
    FETCH NEXT @Limit ROWS ONLY;
END
GO

-- ── 3. WN_BookingDetails_Insert ───────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_Insert
    @BookingGuid      UNIQUEIDENTIFIER,
    @CustomerCode     NVARCHAR(50)   = NULL,
    @CustomerName     NVARCHAR(255)  = NULL,
    @CustomerEmail    NVARCHAR(255)  = NULL,
    @SpaceName        NVARCHAR(255)  = NULL,
    @SpaceCode        NVARCHAR(50)   = NULL,
    @SpaceCategory    NVARCHAR(50)   = NULL,
    @StartDateTime    DATETIME       = NULL,
    @EndDateTime      DATETIME       = NULL,
    @RentAmount       DECIMAL(18,2)  = 0,
    @SecurityDeposit  DECIMAL(18,2)  = 0,
    @RentAccountId    INT            = NULL,
    @DepositAccountId INT            = NULL,
    @PaymentMethod    NVARCHAR(50)   = NULL,
    @Notes            NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Upsert: update if booking already has a detail row, else insert
    IF EXISTS (SELECT 1 FROM dbo.WN_BookingDetails WHERE BookingGuid = @BookingGuid)
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            CustomerCode     = ISNULL(@CustomerCode,    CustomerCode),
            CustomerName     = ISNULL(@CustomerName,    CustomerName),
            CustomerEmail    = ISNULL(@CustomerEmail,   CustomerEmail),
            SpaceName        = ISNULL(@SpaceName,       SpaceName),
            SpaceCode        = ISNULL(@SpaceCode,       SpaceCode),
            SpaceCategory    = ISNULL(@SpaceCategory,   SpaceCategory),
            StartDateTime    = ISNULL(@StartDateTime,   StartDateTime),
            EndDateTime      = ISNULL(@EndDateTime,     EndDateTime),
            RentAmount       = @RentAmount,
            SecurityDeposit  = @SecurityDeposit,
            TotalAmount      = @RentAmount + @SecurityDeposit,
            RentAccountId    = ISNULL(@RentAccountId,   RentAccountId),
            DepositAccountId = @DepositAccountId,
            PaymentMethod    = ISNULL(@PaymentMethod,   PaymentMethod),
            Notes            = ISNULL(@Notes,           Notes)
        WHERE BookingGuid = @BookingGuid;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, CustomerCode, CustomerName, CustomerEmail,
             SpaceName, SpaceCode, SpaceCategory, StartDateTime, EndDateTime,
             RentAmount, SecurityDeposit, TotalAmount,
             RentAccountId, DepositAccountId, PaymentMethod, Notes)
        VALUES
            (NEWID(), @BookingGuid, @CustomerCode, @CustomerName, @CustomerEmail,
             @SpaceName, @SpaceCode, @SpaceCategory, @StartDateTime, @EndDateTime,
             @RentAmount, @SecurityDeposit, @RentAmount + @SecurityDeposit,
             @RentAccountId, @DepositAccountId, @PaymentMethod, @Notes);
    END
END
GO

-- ── 4. Update WN_Bookings_Insert to also populate WN_BookingDetails ───────────
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

    SELECT @UserGUID = IdGUID, @UserEmail = Email, @UserName = Name
    FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT @SpaceGUID = IdGUID, @RentAccountId = RentAccountId,
           @DepositAccountId = DepositAccountId,
           @SpaceName = Name, @SpaceCode = Code
    FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    -- Resolve space category from SpaceConfig
    SELECT TOP 1 @SpaceTypeName = sc.SpaceCategory
    FROM dbo.WN_SpaceConfig sc
    JOIN dbo.WN_SpaceTypes st ON st.Id = sc.SpaceTypeId
    JOIN dbo.WN_Spaces s ON s.SpaceTypeId = st.IdGUID
    WHERE s.Id = @SpaceId;

    -- Get security deposit from SpaceConfig
    IF @DepositAccountId IS NOT NULL
        SELECT TOP 1 @SecurityDeposit = ISNULL(SecurityDeposit, 0)
        FROM dbo.WN_SpaceConfig
        WHERE SpaceCategory = @SpaceTypeName;

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

    -- Populate BookingDetails
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

    SELECT SCOPE_IDENTITY() AS NewId, @NewBookingGUID AS IdGUID,
           @RentAccountId AS RentAccountId, @DepositAccountId AS DepositAccountId;
END
GO

-- ── 5. Update WN_Booking_Create (smart booking) to also populate WN_BookingDetails ──
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

    SELECT @UserId = Id, @UserGuid = IdGUID, @UserName = Name
    FROM dbo.WN_Users WHERE Email = @Email;

    IF @UserId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               'User not found' AS errorMessage;
        RETURN;
    END

    SELECT @SpaceTypeId = SpaceTypeId, @SecurityDeposit = ISNULL(SecurityDeposit, 0)
    FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;

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

    -- Populate BookingDetails
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
        NULL                               AS errorMessage;
END
GO

PRINT 'BookingDetails migration completed successfully.';
GO

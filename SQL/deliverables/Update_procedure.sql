-- ============================================================
-- Update_procedure.sql
-- Stage 4: Add CompanyId + BranchId propagation to booking
--          write paths and optional filtering to read paths.
--
-- Rules:
--   - CREATE OR ALTER PROCEDURE throughout
--   - No existing parameter removed, reordered, or renamed
--   - New params appended at end with defaults only
--   - No existing result column removed or renamed
--   - CompanyId and BranchId NEVER accepted as input on write
--     paths — always derived server-side from WN_Spaces
-- ============================================================

USE [SAC400];
GO

-- ============================================================
-- WRITE PATH 1: WN_Booking_Create
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_Create]
    @Email             NVARCHAR(255),
    @SpaceCategory     NVARCHAR(20),
    @StartDT           DATETIME,
    @EndDT             DATETIME,
    @Notes             NVARCHAR(MAX)  = '',
    @TotalAmount       DECIMAL(10,2)  = 0,
    @PaymentMethod     NVARCHAR(50)   = NULL,
    @PaymentRef        NVARCHAR(100)  = NULL,
    @Capacity          INT            = NULL,
    @AccountId         INT            = NULL
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
    DECLARE @CompanyId         INT;
    DECLARE @BranchId          INT;

    SELECT @UserId = Id, @UserGuid = IdGUID, @UserName = Name
    FROM dbo.WN_Users WHERE Email = @Email;

    IF @UserId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS rentAccountId, NULL AS securityDepositAccountId,
               NULL AS securityDeposit, NULL AS challanNumber, NULL AS validity,
               'User not found' AS errorMessage,
               NULL AS companyId, NULL AS branchId;
        RETURN;
    END

    SELECT @SpaceTypeId = SpaceTypeId, @SecurityDeposit = ISNULL(SecurityDeposit, 0)
    FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS rentAccountId, NULL AS securityDepositAccountId,
               NULL AS securityDeposit, NULL AS challanNumber, NULL AS validity,
               'Unknown SpaceCategory' AS errorMessage,
               NULL AS companyId, NULL AS branchId;
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
        @SpaceId                  = s.Id,
        @SpaceGuid                = s.IdGUID,
        @AssignedSpaceName        = s.Name,
        @AssignedSpaceCode        = s.Code,
        @RentAccountId            = s.RentAccountId,
        @SecurityDepositAccountId = s.SecurityDepositAccountId,
        @CompanyId                = s.CompanyId,
        @BranchId                 = s.BranchId
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
               NULL AS rentAccountId, NULL AS securityDepositAccountId,
               NULL AS securityDeposit, NULL AS challanNumber, NULL AS validity,
               'No available space for the requested period' AS errorMessage,
               NULL AS companyId, NULL AS branchId;
        RETURN;
    END

    SET @BookingGuid = NEWID();

    INSERT INTO dbo.WN_Bookings
        (IdGUID, UserGuid, SpaceGuid, StartDateTime, EndDateTime,
         Notes, TotalAmount, BookingStatus, Status,
         BankAccountId, SecurityDepositAccountId,
         ChallanNumber, ValidityDate,
         BookingDate, CreatedOn, CreatedBy,
         CompanyId, BranchId)
    VALUES
        (@BookingGuid, @UserGuid, @SpaceGuid, @StartDT, @EndDT,
         @Notes, @TotalAmount, 1, 1,
         @RentAccountId, @SecurityDepositAccountId,
         @ChallanNumber, @ValidityDate,
         GETUTCDATE(), GETUTCDATE(), @UserGuid,
         @CompanyId, @BranchId);

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

    EXEC dbo.WN_BookingDetails_InsertLine
        @BookingGuid = @BookingGuid,
        @FeeType     = 'RoomRent',
        @Amount      = @TotalAmount,
        @AccountId   = @RentAccountId,
        @CreatedBy   = @Email;

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
        NULL                               AS errorMessage,
        @CompanyId                         AS companyId,
        @BranchId                          AS branchId;
END
GO

-- ============================================================
-- WRITE PATH 2: WN_Bookings_Insert
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_Insert]
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

    DECLARE @UserGUID                 UNIQUEIDENTIFIER;
    DECLARE @SpaceGUID                UNIQUEIDENTIFIER;
    DECLARE @RentAccountId            INT;
    DECLARE @SecurityDepositAccountId INT;
    DECLARE @NewBookingGUID           UNIQUEIDENTIFIER = NEWID();
    DECLARE @SpaceTypeName            NVARCHAR(100);
    DECLARE @UserEmail                NVARCHAR(255);
    DECLARE @SecurityDeposit          DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber            NVARCHAR(50);
    DECLARE @ValidityDate             DATETIME;
    DECLARE @NewId                    INT;
    DECLARE @DatePart                 NVARCHAR(8);
    DECLARE @SeqNum                   INT;
    DECLARE @CompanyId                INT;
    DECLARE @BranchId                 INT;

    SELECT @UserGUID = IdGUID, @UserEmail = Email
    FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT @SpaceGUID                = IdGUID,
           @RentAccountId            = RentAccountId,
           @SecurityDepositAccountId = SecurityDepositAccountId,
           @CompanyId                = CompanyId,
           @BranchId                 = BranchId
    FROM dbo.WN_Spaces WHERE Id = @SpaceId;

    SELECT TOP 1 @SpaceTypeName = sc.SpaceCategory
    FROM dbo.WN_SpaceConfig sc
    JOIN dbo.WN_SpaceTypes  st ON st.Id       = sc.SpaceTypeId
    JOIN dbo.WN_Spaces      s  ON s.SpaceTypeId = st.IdGUID
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
         CreatedOn, CreatedBy,
         CompanyId, BranchId)
    VALUES
        (@NewBookingGUID, GETDATE(), @UserGUID, @SpaceGUID,
         @StartDateTime, @EndDateTime, @TotalAmount,
         1, 1, @Notes, @CustomerCode,
         @RentAccountId, @SecurityDepositAccountId,
         @ChallanNumber, @ValidityDate,
         GETDATE(), @UserGUID,
         @CompanyId, @BranchId);

    SET @NewId = SCOPE_IDENTITY();

    EXEC dbo.WN_BookingDetails_InsertLine
        @BookingGuid = @NewBookingGUID,
        @FeeType     = 'RoomRent',
        @Amount      = @TotalAmount,
        @AccountId   = @RentAccountId,
        @CreatedBy   = @UserEmail;

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
           @ChallanNumber            AS ChallanNumber,
           @ValidityDate             AS Validity,
           @SecurityDeposit          AS SecurityDeposit,
           @CompanyId                AS CompanyId,
           @BranchId                 AS BranchId;
END
GO

-- ============================================================
-- WRITE PATH 3: WN_CreateBookingWithAutoAssignment
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_CreateBookingWithAutoAssignment]
    @Email             NVARCHAR(255),
    @SpaceType         NVARCHAR(100),
    @StartDateTime     DATETIME,
    @EndDateTime       DATETIME,
    @Notes             NVARCHAR(MAX)    = '',
    @TotalAmount       DECIMAL(10,2)    = 0,
    @PaymentMethod     NVARCHAR(50)     = NULL,
    @PaymentRef        NVARCHAR(100)    = NULL,
    @BookingId         INT              OUTPUT,
    @BookingGuid       UNIQUEIDENTIFIER OUTPUT,
    @AssignedSpaceId   INT              OUTPUT,
    @AssignedSpaceName NVARCHAR(255)    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;

    BEGIN TRY
        DECLARE @UserId      INT;
        DECLARE @UserGuid    UNIQUEIDENTIFIER;
        DECLARE @SpaceId     INT;
        DECLARE @SpaceGuid   UNIQUEIDENTIFIER;
        DECLARE @SpaceTypeId UNIQUEIDENTIFIER;
        DECLARE @CompanyId   INT;
        DECLARE @BranchId    INT;

        SELECT @UserId = Id, @UserGuid = IdGUID
        FROM dbo.WN_Users WITH (NOLOCK)
        WHERE Email = @Email AND Status = 1;

        IF @UserId IS NULL
        BEGIN
            RAISERROR('User not found or inactive', 16, 1);
            RETURN;
        END

        SELECT @SpaceTypeId = IdGUID
        FROM dbo.WN_SpaceTypes WITH (NOLOCK)
        WHERE Description = @SpaceType AND Status = 1;

        IF @SpaceTypeId IS NULL
        BEGIN
            RAISERROR('Invalid space type', 16, 1);
            RETURN;
        END

        SELECT TOP 1
            @SpaceId           = Id,
            @SpaceGuid         = IdGUID,
            @AssignedSpaceName = Name,
            @CompanyId         = CompanyId,
            @BranchId          = BranchId
        FROM (
            SELECT
                s.Id, s.IdGUID, s.Name, s.Code,
                s.CompanyId, s.BranchId,
                CASE
                    WHEN @SpaceType LIKE '%Private%' AND s.Code LIKE '30%' THEN 1
                    WHEN @SpaceType LIKE '%Shared%'  AND s.Code LIKE '31%' THEN 1
                    WHEN @SpaceType LIKE '%Meeting%' AND s.Code LIKE '32%' THEN 1
                    ELSE 2
                END AS Priority,
                CASE
                    WHEN ISNUMERIC(SUBSTRING(s.Code, 3, LEN(s.Code)-2)) = 1
                    THEN CAST(SUBSTRING(s.Code, 3, LEN(s.Code)-2) AS INT)
                    ELSE 999
                END AS CodeNumber
            FROM dbo.WN_Spaces s WITH (UPDLOCK)
            WHERE s.SpaceTypeId = @SpaceTypeId
              AND s.Status = 1
              AND s.IdGUID NOT IN (
                    SELECT DISTINCT b.SpaceGuid
                    FROM dbo.WN_Bookings b WITH (NOLOCK)
                    WHERE b.BookingStatus IN (1, 4)
                      AND (
                            (@StartDateTime >= b.StartDateTime AND @StartDateTime <  b.EndDateTime) OR
                            (@EndDateTime   >  b.StartDateTime AND @EndDateTime   <= b.EndDateTime) OR
                            (@StartDateTime <= b.StartDateTime AND @EndDateTime   >= b.EndDateTime)
                          )
                )
        ) AS AvailableSpaces
        ORDER BY Priority ASC, CodeNumber ASC;

        IF @SpaceId IS NULL
        BEGIN
            RAISERROR('No available spaces for the requested time period', 16, 1);
            RETURN;
        END

        SET @BookingGuid = NEWID();

        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, UserGuid, SpaceGuid,
            StartDateTime, EndDateTime, Notes, TotalAmount,
            BookingStatus, Status, CreatedOn, CreatedBy,
            CompanyId, BranchId
        ) VALUES (
            @BookingGuid, GETDATE(), @UserGuid, @SpaceGuid,
            @StartDateTime, @EndDateTime, @Notes, @TotalAmount,
            1, 1, GETDATE(), @UserGuid,
            @CompanyId, @BranchId
        );

        SET @BookingId       = SCOPE_IDENTITY();
        SET @AssignedSpaceId = @SpaceId;

        IF @PaymentMethod IS NOT NULL AND @TotalAmount > 0
        BEGIN
            DECLARE @PaymentGuid UNIQUEIDENTIFIER = NEWID();
            INSERT INTO dbo.WN_Payments (
                IdGUID, UserId, BookingId, Amount, Currency,
                PaymentMethod, TransactionRef, PaymentStatus,
                CreatedAt, PaidAt
            ) VALUES (
                @PaymentGuid, @UserGuid, @BookingGuid, @TotalAmount, 'PKR',
                @PaymentMethod, @PaymentRef, 'Pending',
                GETDATE(), NULL
            );
        END

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        DECLARE @ErrorMessage  NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT            = ERROR_SEVERITY();
        DECLARE @ErrorState    INT            = ERROR_STATE();
        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END
GO

-- ============================================================
-- WRITE PATH 4: WN_BookingDetails_InsertLine
-- Resolves CompanyId + BranchId server-side via
-- BookingGuid → WN_Bookings → WN_Spaces → WN_Locations
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_InsertLine]
    @BookingGuid UNIQUEIDENTIFIER,
    @FeeType     NVARCHAR(50),
    @Amount      DECIMAL(18,2),
    @AccountId   INT           = NULL,
    @CreatedBy   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CompanyId INT;
    DECLARE @BranchId  INT;

    SELECT @CompanyId = s.CompanyId,
           @BranchId  = s.BranchId
    FROM   dbo.WN_Bookings  b
    JOIN   dbo.WN_Spaces    s ON s.IdGUID = b.SpaceGuid
    WHERE  b.IdGUID = @BookingGuid;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_BookingDetails
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0
    )
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            Amount    = @Amount,
            AccountId = ISNULL(@AccountId, AccountId),
            CompanyId = ISNULL(@CompanyId, CompanyId),
            BranchId  = ISNULL(@BranchId,  BranchId)
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, FeeType, Amount, AccountId,
             CreatedOn, CreatedBy, CompanyId, BranchId)
        VALUES
            (NEWID(), @BookingGuid, @FeeType, @Amount, @AccountId,
             GETDATE(), @CreatedBy, @CompanyId, @BranchId);
    END
END
GO

-- ============================================================
-- READ PATH 1: WN_Booking_GetAvailableSpaces
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_GetAvailableSpaces]
    @SpaceCategory NVARCHAR(20),
    @StartDT       DATETIME,
    @EndDT         DATETIME,
    @Capacity      INT = NULL,
    @CompanyId     INT = NULL,
    @BranchId      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SpaceTypeId INT;
    DECLARE @STGuid      UNIQUEIDENTIFIER;

    SELECT @SpaceTypeId = SpaceTypeId FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;
    IF @SpaceTypeId IS NULL BEGIN RAISERROR('Unknown SpaceCategory',16,1); RETURN; END
    SELECT @STGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    SELECT
        s.Id,
        s.IdGUID,
        s.Name,
        s.Code,
        s.PricePerDay,
        s.PricePerHour,
        st.Description          AS SpaceType,
        st.Capacity             AS Capacity,
        l.Name                  AS LocationName,
        TRY_CAST(s.Code AS INT) AS CodeNumber
    FROM  dbo.WN_Spaces     s
    JOIN  dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    JOIN  dbo.WN_Locations  l  ON l.IdGUID  = s.LocationId
    WHERE s.Status = 1
      AND s.SpaceTypeId = @STGuid
      AND (@Capacity  IS NULL OR st.Capacity >= @Capacity)
      AND (@CompanyId IS NULL OR s.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR s.BranchId   = @BranchId)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk
            WHERE bk.SpaceGuid = s.IdGUID
              AND bk.BookingStatus IN (1,4)
              AND @StartDT < bk.EndDateTime
              AND @EndDT   > bk.StartDateTime
          )
    ORDER BY TRY_CAST(s.Code AS INT) ASC, s.Id ASC;
END
GO

-- ============================================================
-- READ PATH 2: WN_Booking_AssignClosestSpace
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_AssignClosestSpace]
    @SpaceCategory NVARCHAR(20),
    @StartDT       DATETIME,
    @EndDT         DATETIME,
    @Capacity      INT = NULL,
    @CompanyId     INT = NULL,
    @BranchId      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SpaceTypeId INT;
    DECLARE @STGuid      UNIQUEIDENTIFIER;

    SELECT @SpaceTypeId = SpaceTypeId FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;
    IF @SpaceTypeId IS NULL BEGIN RAISERROR('Unknown SpaceCategory',16,1); RETURN; END
    SELECT @STGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    SELECT TOP 1
        s.Id, s.IdGUID, s.Name, s.Code, st.Capacity, l.Name AS LocationName
    FROM  dbo.WN_Spaces     s
    JOIN  dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    JOIN  dbo.WN_Locations  l  ON l.IdGUID  = s.LocationId
    WHERE s.Status = 1
      AND s.SpaceTypeId = @STGuid
      AND (@Capacity  IS NULL OR st.Capacity >= @Capacity)
      AND (@CompanyId IS NULL OR s.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR s.BranchId   = @BranchId)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk
            WHERE bk.SpaceGuid = s.IdGUID
              AND bk.BookingStatus IN (1,4)
              AND @StartDT < bk.EndDateTime
              AND @EndDT   > bk.StartDateTime
          )
    ORDER BY TRY_CAST(s.Code AS INT) ASC, s.Id ASC;
END
GO

-- ============================================================
-- READ PATH 3: WN_Spaces_GetList
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetList]
    @CompanyId INT = NULL,
    @BranchId  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id            AS id,
        s.IdGUID        AS idGuid,
        s.Name          AS name,
        s.Code          AS code,
        s.Description   AS description,
        s.FloorId       AS floorId,
        f.FloorName     AS floorName,
        s.PricePerDay   AS pricePerDay,
        s.PricePerHour  AS pricePerHour,
        s.PricePerMonth AS pricePerMonth,
        s.ImageUrl      AS imageUrl,
        s.Amenities     AS amenities,
        s.Status        AS status,
        s.LocationId    AS locationId,
        l.IdGUID        AS locationIdGuid,
        l.Name          AS locationName,
        s.SpaceTypeId   AS spaceTypeId,
        st.IdGUID       AS spaceTypeIdGuid,
        st.Description  AS spaceTypeName,
        st.Capacity     AS capacity,
        CASE s.Status
            WHEN 1 THEN 'Available'
            WHEN 0 THEN 'Inactive'
            ELSE 'Unknown'
        END             AS spaceStatus
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON s.LocationId  = l.IdGUID
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON s.SpaceTypeId = st.IdGUID
    LEFT JOIN dbo.WN_Floors     f  WITH (NOLOCK) ON s.FloorId     = f.Id
    WHERE s.Status = 1
      AND (@CompanyId IS NULL OR s.CompanyId = @CompanyId)
      AND (@BranchId  IS NULL OR s.BranchId  = @BranchId)
    ORDER BY s.Id;
END
GO

-- ============================================================
-- READ PATH 4: WN_Spaces_GetVacant
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetVacant]
    @CompanyId INT = NULL,
    @BranchId  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id,
        s.IdGUID,
        s.Name,
        s.Code,
        s.Status,
        s.PricePerDay,
        s.PricePerHour,
        s.PricePerMonth,
        l.Name         AS LocationName,
        st.Description AS SpaceTypeName
    FROM dbo.WN_Spaces s
    LEFT JOIN dbo.WN_Locations  l  ON l.IdGUID  = s.LocationId
    LEFT JOIN dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    WHERE s.Status = 1
      AND (@CompanyId IS NULL OR s.CompanyId = @CompanyId)
      AND (@BranchId  IS NULL OR s.BranchId  = @BranchId)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings b
          WHERE b.SpaceGuid = s.IdGUID
            AND b.BookingStatus = 1
            AND b.StartDateTime <= GETDATE()
            AND b.EndDateTime   >= GETDATE()
      )
    ORDER BY TRY_CAST(s.Code AS INT);
END
GO

-- ============================================================
-- READ PATH 5: WN_Locations_GetList
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_GetList]
    @CompanyId INT = NULL,
    @BranchId  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id            AS Id,
        l.IdGUID        AS IdGuid,
        l.Name          AS Name,
        l.Address       AS Address,
        l.CityId        AS CityId,
        c.Name          AS CityName,
        l.OpeningTime   AS OpeningTime,
        l.ClosingTime   AS ClosingTime,
        l.Status        AS Status,
        l.BranchId      AS BranchId,
        l.CompanyId     AS CompanyId,
        b.[Description] AS BranchName,
                b.[Code]        AS BranchCode
    FROM dbo.WN_Locations l WITH (NOLOCK)
    LEFT JOIN dbo.WN_Cities           c WITH (NOLOCK) ON l.CityId   = c.Id
    LEFT JOIN SAC400.dbo.Branches     b WITH (NOLOCK) ON l.BranchId = b.Id
    WHERE l.Status = 1
      AND (@CompanyId IS NULL OR l.CompanyId = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId  = @BranchId);
END
GO

-- ============================================================
-- READ PATH 6: WN_Bookings_GetList
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetList]
    @CompanyId INT = NULL,
    @BranchId  INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.IdGUID        AS IdGuid,
           b.Id            AS Id,
           u.Email         AS UserEmail,
           s.Name          AS SpaceName,
           b.StartDateTime AS StartDateTime,
           b.EndDateTime   AS EndDateTime,
           b.TotalAmount   AS TotalAmount,
           b.Notes         AS Notes,
           b.BookingDate   AS CreatedAt,
           b.CompanyId     AS CompanyId,
           b.BranchId      AS BranchId,
           CASE b.BookingStatus
               WHEN 1 THEN 'Pending'
               WHEN 2 THEN 'Cancelled'
               WHEN 3 THEN 'Rejected'
               WHEN 4 THEN 'Confirmed'
               ELSE 'Confirmed'
           END AS BookingStatus
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users  u WITH (NOLOCK) ON b.UserGuid  = u.IdGUID
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON b.SpaceGuid = s.IdGUID
    WHERE b.Status = 1
      AND (@CompanyId IS NULL OR b.CompanyId = @CompanyId)
      AND (@BranchId  IS NULL OR b.BranchId  = @BranchId)
    ORDER BY b.BookingDate DESC;
END
GO

-- ============================================================
-- READ PATH 7: WN_BookingDetails_GetByBooking
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_GetByBooking]
    @BookingGuid NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        bd.Id,
        CAST(bd.IdGUID      AS NVARCHAR(36)) AS idGuid,
        CAST(bd.BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        bd.FeeType,
        bd.Amount,
        bd.AccountId,
        a.Description AS accountName,
        bd.CreatedOn,
        bd.CompanyId,
        bd.BranchId
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA a WITH (NOLOCK) ON a.Id = bd.AccountId
    WHERE CAST(bd.BookingGuid AS NVARCHAR(36)) = @BookingGuid
      AND bd.IsDeleted = 0
    ORDER BY bd.Id;
END
GO

-- ============================================================
-- READ PATH 8: WN_BookingDetails_GetList
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_GetList]
    @Page      INT           = 1,
    @Limit     INT           = 100,
    @FeeType   NVARCHAR(50)  = NULL,
    @AccountId INT           = NULL,
    @Search    NVARCHAR(255) = NULL,
    @CompanyId INT           = NULL,
    @BranchId  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        bd.Id,
        CAST(bd.IdGUID      AS NVARCHAR(36)) AS idGuid,
        CAST(bd.BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        bd.FeeType,
        bd.Amount,
        bd.AccountId,
        a.Description AS accountName,
        b.ChallanNumber,
        bd.CreatedOn,
        bd.CompanyId,
        bd.BranchId
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA  a WITH (NOLOCK) ON a.Id     = bd.AccountId
    LEFT JOIN dbo.WN_Bookings  b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
    WHERE bd.IsDeleted = 0
      AND (@FeeType   IS NULL OR bd.FeeType   = @FeeType)
      AND (@AccountId IS NULL OR bd.AccountId = @AccountId)
      AND (@CompanyId IS NULL OR bd.CompanyId = @CompanyId)
      AND (@BranchId  IS NULL OR bd.BranchId  = @BranchId)
      AND (@Search    IS NULL OR @Search = ''
           OR CAST(bd.BookingGuid AS NVARCHAR(36)) LIKE '%' + @Search + '%'
           OR b.ChallanNumber LIKE '%' + @Search + '%')
    ORDER BY bd.CreatedOn DESC
    OFFSET (@Page - 1) * @Limit ROWS
    FETCH NEXT @Limit ROWS ONLY;
END
GO


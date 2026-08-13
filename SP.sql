
-- ── 3. WN_AccountsCOA_GetById ─────────────────────────────────────────────────
-- Returns a single account by its primary key.
CREATE   PROCEDURE [dbo].[WN_AccountsCOA_GetById]
    @AccountId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
      Id,
        Description
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE Id = @AccountId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AccountsCOA_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 2. WN_AccountsCOA_GetList ─────────────────────────────────────────────────
-- Returns all accounts from dbo.AccountsCOA sorted alphabetically by Description.
CREATE   PROCEDURE [dbo].[WN_AccountsCOA_GetList]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Description
    FROM dbo.AccountsCOA WITH (NOLOCK)
    ORDER BY Description ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Amenities_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Amenities_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Icon, IsActive
    FROM dbo.WN_Amenities WITH (NOLOCK)
    WHERE IsActive = 1
    ORDER BY Name ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Amenities_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Amenities_Insert]
    @Name NVARCHAR(100),
    @Icon NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.WN_Amenities WHERE Name = @Name)
    BEGIN
        SELECT Id FROM dbo.WN_Amenities WHERE Name = @Name;
        RETURN;
    END
    INSERT INTO dbo.WN_Amenities (Name, Icon) VALUES (@Name, @Icon);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AmountFields_GetByEntityField]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 4. WN_AmountFields_GetByEntityField ───────────────────────────────────────
CREATE   PROCEDURE [dbo].[WN_AmountFields_GetByEntityField]
    @Entity NVARCHAR(100),
    @Field  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Entity, Field, Label, Currency
    FROM dbo.WN_AmountFields
    WHERE Entity = @Entity AND Field = @Field;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AmountFields_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 3. WN_AmountFields_GetList ────────────────────────────────────────────────
CREATE   PROCEDURE [dbo].[WN_AmountFields_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Entity, Field, Label, Currency
    FROM dbo.WN_AmountFields
    ORDER BY Entity, Field;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BillingPeriods_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_BillingPeriods_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Code, Label, DurationDays, SortOrder
    FROM dbo.WN_BillingPeriods WITH (NOLOCK)
    WHERE IsActive = 1
    ORDER BY SortOrder ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_AssignClosestSpace]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.2  WN_Booking_AssignClosestSpace
-- Filter via l.CompanyId / l.BranchId (Space → Location)
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_Booking_AssignClosestSpace]
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

    SELECT @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN RAISERROR('Unknown SpaceCategory',16,1); RETURN; END

    SELECT @STGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    SELECT TOP 1
        s.Id, s.IdGUID, s.Name, s.Code, st.Capacity, l.Name AS LocationName
    FROM  dbo.WN_Spaces     s
    JOIN  dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    JOIN  dbo.WN_Locations  l  ON l.IdGUID  = s.LocationId
    WHERE s.Status = 1
      AND s.SpaceTypeId = @STGuid
      AND (@Capacity  IS NULL OR st.Capacity >= @Capacity)
      AND (@CompanyId IS NULL OR l.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
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
/****** Object:  StoredProcedure [dbo].[WN_Booking_CheckOverlap]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- WN_Booking_CheckOverlap
-- Resolves SpaceGuid from WN_Spaces then checks WN_Bookings
-- ============================================================
CREATE PROCEDURE [dbo].[WN_Booking_CheckOverlap]
    @SpaceNumericId   INT,
    @StartDT          DATETIME,
    @EndDT            DATETIME,
    @ExcludeBookingId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SG UNIQUEIDENTIFIER;
    SELECT @SG = IdGUID FROM dbo.WN_Spaces WHERE Id = @SpaceNumericId;
    IF @SG IS NULL BEGIN SELECT 0 AS IsOverlapping; RETURN; END
    IF EXISTS (
        SELECT 1 FROM dbo.WN_Bookings bk
        WHERE bk.SpaceGuid = @SG
          AND bk.BookingStatus IN (1,4)
          AND (@ExcludeBookingId IS NULL OR bk.Id != @ExcludeBookingId)
          AND @StartDT < bk.EndDateTime
          AND @EndDT   > bk.StartDateTime
    )
        SELECT 1 AS IsOverlapping;
    ELSE
        SELECT 0 AS IsOverlapping;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_Create]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- STEP 2: ALTER dbo.WN_Booking_Create
-- Only the challan generation block is changed.
-- Everything else is byte-for-byte identical to the original.
-- ============================================================
CREATE PROCEDURE [dbo].[WN_Booking_Create]
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
    DECLARE @Today             DATE;
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

    -- ── Challan generation (concurrency-safe daily counter) ──────────────────
    SET @Today = CAST(GETDATE() AS DATE);

    BEGIN TRANSACTION;

    -- Atomically increment (or seed) today's counter under exclusive lock.
    -- UPDLOCK prevents two sessions reading the same row before either writes.
    -- HOLDLOCK (SERIALIZABLE) prevents phantom inserts between the read and write.
    UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
    SET    LastNumber = LastNumber + 1
    WHERE  CounterDate = @Today;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
        SELECT @Today, 1
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
            WHERE CounterDate = @Today
        );

    SELECT @SeqNum = LastNumber
    FROM   dbo.WN_ChallanCounter WITH (NOLOCK)
    WHERE  CounterDate = @Today;

    SET @ChallanNumber = 'WN-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                       + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @ValidityDate  = DATEADD(DAY, 5, GETDATE());
    -- ── End challan generation ───────────────────────────────────────────────

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
        NULL                               AS errorMessage;
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_GetAvailableSpaces]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


-- ============================================================
-- SECTION 4: Stored Procedures (CREATE OR ALTER — re-runnable)
-- ============================================================

-- ------------------------------------------------------------
-- 4.1  WN_Booking_GetAvailableSpaces
-- Filter via l.CompanyId / l.BranchId (Space → Location)
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_Booking_GetAvailableSpaces]
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

    SELECT @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN RAISERROR('Unknown SpaceCategory',16,1); RETURN; END

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
      AND (@CompanyId IS NULL OR l.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
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
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_GetByBooking]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.4  WN_BookingDetails_GetByBooking
-- SELECT derives CompanyId/BranchId via
-- BookingDetail → Booking → Space → Location
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_BookingDetails_GetByBooking]
    @BookingIdentifier NVARCHAR(50),
    @UserEmail         NVARCHAR(256) = NULL
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
        a.Description  AS accountName,
        bd.CreatedOn,
        l.CompanyId    AS CompanyId,
        l.BranchId     AS BranchId
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA   a  WITH (NOLOCK) ON a.Id        = bd.AccountId
    LEFT JOIN dbo.WN_Bookings   b  WITH (NOLOCK) ON b.IdGUID    = bd.BookingGuid
    LEFT JOIN dbo.WN_Spaces     s  WITH (NOLOCK) ON s.IdGUID    = b.SpaceGuid
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.IdGUID    = s.LocationId
    LEFT JOIN dbo.WN_Users      u  WITH (NOLOCK) ON u.Id        = b.UserId
    WHERE (
        TRY_CAST(@BookingIdentifier AS UNIQUEIDENTIFIER) IS NOT NULL AND b.IdGUID = TRY_CAST(@BookingIdentifier AS UNIQUEIDENTIFIER)
        OR TRY_CAST(@BookingIdentifier AS INT) IS NOT NULL AND b.Id = TRY_CAST(@BookingIdentifier AS INT)
    )
      AND (@UserEmail IS NULL OR u.Email = @UserEmail)
      AND bd.IsDeleted = 0
    ORDER BY bd.Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.5  WN_BookingDetails_GetList
-- SELECT + WHERE derive CompanyId/BranchId via
-- BookingDetail → Booking → Space → Location
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_BookingDetails_GetList]
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
        a.Description  AS accountName,
        b.ChallanNumber,
        bd.CreatedOn,
        l.CompanyId    AS CompanyId,
        l.BranchId     AS BranchId
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA  a  WITH (NOLOCK) ON a.Id        = bd.AccountId
    LEFT JOIN dbo.WN_Bookings  b  WITH (NOLOCK) ON b.IdGUID    = bd.BookingGuid
    LEFT JOIN dbo.WN_Spaces    s  WITH (NOLOCK) ON s.IdGUID    = b.SpaceGuid
    LEFT JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.IdGUID    = s.LocationId
    WHERE bd.IsDeleted = 0
      AND (@FeeType   IS NULL OR bd.FeeType   = @FeeType)
      AND (@AccountId IS NULL OR bd.AccountId = @AccountId)
      AND (@CompanyId IS NULL OR l.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
      AND (@Search    IS NULL OR @Search = ''
           OR CAST(bd.BookingGuid AS NVARCHAR(36)) LIKE '%' + @Search + '%'
           OR b.ChallanNumber LIKE '%' + @Search + '%')
    ORDER BY bd.CreatedOn DESC
    OFFSET (@Page - 1) * @Limit ROWS
    FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_GetTotalByBooking]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 6. WN_BookingDetails_GetTotalByBooking ───────────────────────────────────
-- Returns the sum of all fee lines for a booking (used for payment total).
CREATE   PROCEDURE [dbo].[WN_BookingDetails_GetTotalByBooking]
    @BookingGuid NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ISNULL(SUM(Amount), 0) AS TotalAmount
    FROM dbo.WN_BookingDetails WITH (NOLOCK)
    WHERE CAST(BookingGuid AS NVARCHAR(36)) = @BookingGuid
      AND IsDeleted = 0;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 3. WN_BookingDetails_Insert ───────────────────────────────────────────────
CREATE   PROCEDURE [dbo].[WN_BookingDetails_Insert]
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
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_InsertLine]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.6  WN_BookingDetails_InsertLine
-- CompanyId/BranchId columns removed from table.
-- No longer stored — derived at read time via join chain.
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_BookingDetails_InsertLine]
    @BookingGuid UNIQUEIDENTIFIER,
    @FeeType     NVARCHAR(50),
    @Amount      DECIMAL(18,2),
    @AccountId   INT           = NULL,
    @CreatedBy   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_BookingDetails
        WHERE BookingGuid = @BookingGuid
          AND FeeType     = @FeeType
          AND IsDeleted   = 0
    )
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            Amount    = @Amount,
            AccountId = ISNULL(@AccountId, AccountId)
        WHERE BookingGuid = @BookingGuid
          AND FeeType     = @FeeType
          AND IsDeleted   = 0;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy)
        VALUES
            (NEWID(), @BookingGuid, @FeeType, @Amount, @AccountId, GETDATE(), @CreatedBy);
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingLines_GetByBooking]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 8: BOOKING LINES
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_BookingLines_GetByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        bl.Id, bl.BookingId, bl.ChargeTypeId,
        ct.Code AS ChargeTypeCode, ct.Label AS ChargeTypeLabel,
        bl.Description, bl.Quantity, bl.UnitPrice,
        bl.DiscountAmount, bl.TaxRate, bl.TaxAmount, bl.LineTotal,
        bl.AccountId, bl.CreatedOn
    FROM dbo.WN_BookingLines bl WITH (NOLOCK)
    JOIN dbo.WN_ChargeTypes  ct WITH (NOLOCK) ON ct.Id = bl.ChargeTypeId
    WHERE bl.BookingId = @BookingId AND bl.IsDeleted = 0
    ORDER BY bl.Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingLines_GetTotal]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_BookingLines_GetTotal]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ISNULL(SUM(LineTotal), 0)      AS GrandTotal,
        ISNULL(SUM(TaxAmount), 0)      AS TaxTotal,
        ISNULL(SUM(DiscountAmount), 0) AS DiscountTotal
    FROM dbo.WN_BookingLines WITH (NOLOCK)
    WHERE BookingId = @BookingId AND IsDeleted = 0;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingLines_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_BookingLines_Insert]
    @BookingId      INT,
    @ChargeTypeId   TINYINT,
    @Description    NVARCHAR(200) = NULL,
    @Quantity       DECIMAL(10,4) = 1,
    @UnitPrice      DECIMAL(18,4),
    @DiscountAmount DECIMAL(18,4) = 0,
    @TaxRate        DECIMAL(6,4)  = 0,
    @AccountId      INT           = NULL,
    @CreatedById    INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_BookingLines
        (BookingId, ChargeTypeId, Description, Quantity, UnitPrice,
         DiscountAmount, TaxRate, AccountId, CreatedById)
    VALUES
        (@BookingId, @ChargeTypeId, @Description, @Quantity, @UnitPrice,
         @DiscountAmount, @TaxRate, @AccountId, @CreatedById);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Cancel]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_Cancel]
    @Id           INT,
    @UserEmail    NVARCHAR(256) = NULL,
    @CancelReason NVARCHAR(500) = NULL,
    @UpdatedById  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @UserEmail IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM dbo.WN_Bookings b
        JOIN dbo.WN_Users u ON u.Id = b.UserId
        WHERE b.Id = @Id AND u.Email = @UserEmail
    )
    BEGIN
        RAISERROR('Booking not found or access denied.', 16, 1); RETURN;
    END

    UPDATE dbo.WN_Bookings SET
        BookingStatusId = 3,
        CancelReason    = @CancelReason,
        UpdatedOn       = SYSUTCDATETIME(),
        UpdatedById     = @UpdatedById
    WHERE Id = @Id AND IsDeleted = 0;

    UPDATE dbo.WN_Challans SET StatusId = 4
    WHERE BookingId = @Id AND StatusId = 1;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_CheckOverlap]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 7: BOOKINGS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Bookings_CheckOverlap]
    @SpaceId          INT,
    @StartOn          DATETIME2,
    @EndOn            DATETIME2,
    @ExcludeBookingId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM dbo.WN_Bookings
        WHERE SpaceId         = @SpaceId
          AND IsDeleted       = 0
          AND BookingStatusId IN (1, 2)
          AND (@ExcludeBookingId IS NULL OR Id <> @ExcludeBookingId)
          AND @StartOn        < EndOn
          AND @EndOn          > StartOn
    )
        SELECT 1 AS IsOverlapping;
    ELSE
        SELECT 0 AS IsOverlapping;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetAvailableSpaces]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetAvailableSpaces]
    @SpaceTypeId INT,
    @StartOn     DATETIME2,
    @EndOn       DATETIME2,
    @Capacity    INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode
    FROM dbo.WN_Spaces    s  WITH (NOLOCK)
    JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = s.LocationIdInt
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive        = 1
      AND s.SpaceTypeIdInt  = @SpaceTypeId
      AND (@Capacity        IS NULL OR s.Capacity >= @Capacity)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (1, 2)
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
      )
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetAvailableSpacesForReassignment]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetAvailableSpacesForReassignment]
    @SpaceTypeId      INT,
    @StartOn          DATETIME2,
    @EndOn            DATETIME2,
    @ExcludeBookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit
    FROM dbo.WN_Spaces    s  WITH (NOLOCK)
    JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = s.LocationIdInt
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit
        FROM dbo.WN_SpacePricing sp
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive       = 1
      AND s.SpaceTypeIdInt = @SpaceTypeId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (1, 2)
            AND bk.Id             <> @ExcludeBookingId
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
      )
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetByChallan]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 5.4  WN_Bookings_GetByChallan
-- Added CompanyName + BranchName for challan booking card
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_Bookings_GetByChallan]
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
        s.Name              AS spaceName,
        s.Code              AS spaceCode,
        st.Description      AS spaceTypeName,
        l.Name              AS locationName,
        l.CompanyId         AS companyId,
        co.CompanyName      AS companyName,
        l.BranchId          AS branchId,
        br.[Description]    AS branchName,
        u.Name              AS customerName,
        u.Email             AS customerEmail
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces       s  WITH (NOLOCK) ON s.IdGUID    = b.SpaceGuid
    LEFT JOIN dbo.WN_SpaceTypes   st WITH (NOLOCK) ON st.IdGUID   = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations    l  WITH (NOLOCK) ON l.IdGUID    = s.LocationId
    LEFT JOIN dbo.Company         co WITH (NOLOCK) ON l.CompanyId = co.Id
    LEFT JOIN dbo.Branches        br WITH (NOLOCK) ON l.BranchId  = br.Id
    LEFT JOIN dbo.WN_Users        u  WITH (NOLOCK) ON u.IdGUID    = b.UserGuid
    WHERE b.ChallanNumber = @ChallanNumber;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetByGuid]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 5.3  WN_Bookings_GetByGuid
-- Added CompanyName + BranchName for single booking card
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_Bookings_GetByGuid]
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
        s.Name              AS spaceName,
        s.Code              AS spaceCode,
        st.Description      AS spaceTypeName,
        l.Name              AS locationName,
        l.CompanyId         AS companyId,
        co.CompanyName      AS companyName,
        l.BranchId          AS branchId,
        br.[Description]    AS branchName,
        u.Name              AS customerName,
        u.Email             AS customerEmail
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces       s  WITH (NOLOCK) ON s.IdGUID    = b.SpaceGuid
    LEFT JOIN dbo.WN_SpaceTypes   st WITH (NOLOCK) ON st.IdGUID   = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations    l  WITH (NOLOCK) ON l.IdGUID    = s.LocationId
    LEFT JOIN dbo.Company         co WITH (NOLOCK) ON l.CompanyId = co.Id
    LEFT JOIN dbo.Branches        br WITH (NOLOCK) ON l.BranchId  = br.Id
    LEFT JOIN dbo.WN_Users        u  WITH (NOLOCK) ON u.IdGUID    = b.UserGuid
    WHERE CAST(b.IdGUID AS NVARCHAR(36)) = @IdGUID;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetByPublicId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetByPublicId]
    @PublicId  UNIQUEIDENTIFIER,
    @UserEmail NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        v.BookingId, v.BookingPublicId,
        v.StartOn, v.EndOn,
        v.BookingStatusId, v.BookingStatusCode, v.BookingStatusLabel,
        v.Notes, v.CancelReason, v.BookedOn,
        v.UserId, v.UserPublicId, v.UserName, v.UserEmail,
        v.SpaceId, v.SpacePublicId, v.SpaceCode, v.SpaceName, v.SpaceCapacity,
        v.SpaceTypeId, v.SpaceTypeName,
        v.LocationId, v.LocationName,
        v.BranchId, v.BranchName,
        v.CompanyId, v.CompanyName,
        v.SeatPrice, v.RoomPrice, v.SecurityDeposit,
        v.BillingPeriodCode, v.BillingPeriodLabel,
        v.ChallanNumber, v.ChallanValidUntil, v.ChallanStatusId
    FROM dbo.VW_WN_BookingSummary v
    WHERE v.BookingPublicId = @PublicId
      AND (@UserEmail IS NULL OR v.UserEmail = @UserEmail);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetBySpaceGuid]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_Bookings_GetBySpaceGuid]
    @SpaceGuid UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            b.Id,
            b.IdGUID,
            b.UserGuid,
            b.SpaceGuid,
            b.StartDateTime,
            b.EndDateTime,
            b.TotalAmount,
            b.Notes,
            b.BookingStatus,
            b.Status,
            b.CreatedOn,
            s.Name                                              AS SpaceName,
            DATEDIFF(DAY, b.StartDateTime, b.EndDateTime)       AS ReservedDays
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        INNER JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.IdGUID = b.SpaceGuid
        WHERE b.SpaceGuid = @SpaceGuid;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetCalendar]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetCalendar]
    @SpaceId INT,
    @Year    INT,
    @Month   INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Start DATETIME2 = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @End   DATETIME2 = DATEADD(MONTH, 1, @Start);
    SELECT
        b.Id AS BookingId, b.PublicId AS BookingPublicId,
        b.StartOn, b.EndOn,
        bs.Code  AS BookingStatusCode,
        bs.Label AS BookingStatusLabel,
        u.Name   AS UserName,
        u.Email  AS UserEmail
    FROM dbo.WN_Bookings        b  WITH (NOLOCK)
    JOIN dbo.WN_BookingStatuses bs WITH (NOLOCK) ON bs.Id = b.BookingStatusId
    JOIN dbo.WN_Users           u  WITH (NOLOCK) ON u.Id  = b.UserId
    WHERE b.SpaceId   = @SpaceId
      AND b.IsDeleted = 0
      AND b.StartOn   < @End
      AND b.EndOn     > @Start
    ORDER BY b.StartOn;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        v.BookingId, v.BookingPublicId,
        v.StartOn, v.EndOn,
        v.BookingStatusId, v.BookingStatusCode, v.BookingStatusLabel,
        v.Notes, v.CancelReason, v.BookedOn,
        v.UserId, v.UserPublicId, v.UserName, v.UserEmail,
        v.SpaceId, v.SpacePublicId, v.SpaceCode, v.SpaceName, v.SpaceCapacity,
        v.SpaceTypeId, v.SpaceTypeName,
        v.LocationId, v.LocationName,
        v.BranchId, v.BranchName,
        v.CompanyId, v.CompanyName,
        v.SeatPrice, v.RoomPrice, v.SecurityDeposit,
        v.BillingPeriodCode, v.BillingPeriodLabel,
        v.ChallanNumber, v.ChallanValidUntil, v.ChallanStatusId,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.VW_WN_BookingSummary v
    WHERE (@Search IS NULL OR @Search = ''
           OR v.UserName     LIKE '%' + @Search + '%'
           OR v.UserEmail    LIKE '%' + @Search + '%'
           OR v.SpaceCode    LIKE '%' + @Search + '%'
           OR v.SpaceName    LIKE '%' + @Search + '%'
           OR v.ChallanNumber LIKE '%' + @Search + '%')
    ORDER BY v.BookedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetListByUserId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 14. WN_Bookings_GetListByUserId ──────────────────────
CREATE   PROCEDURE [dbo].[WN_Bookings_GetListByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserGUID UNIQUEIDENTIFIER;
    SELECT @UserGUID = IdGUID FROM dbo.WN_Users WITH (NOLOCK) WHERE Id = @UserId;

    SELECT b.IdGUID         AS IdGuid,
           b.Id             AS Id,
           s.Name           AS SpaceName,
           b.StartDateTime  AS StartDateTime,
           b.EndDateTime    AS EndDateTime,
           b.TotalAmount    AS TotalAmount,
           b.Notes          AS Notes,
           b.BookingDate    AS CreatedAt,
           CASE b.BookingStatus
               WHEN 1 THEN 'Pending'
               WHEN 2 THEN 'Cancelled'
               WHEN 3 THEN 'Rejected'
               WHEN 4 THEN 'Confirmed'
               ELSE 'Confirmed'
           END AS BookingStatus
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON b.SpaceGuid = s.IdGUID
    WHERE b.UserGuid = @UserGUID AND b.Status = 1
    ORDER BY b.BookingDate DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetMyList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetMyList]
    @UserEmail NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        v.BookingId, v.BookingPublicId,
        v.StartOn, v.EndOn,
        v.BookingStatusCode, v.BookingStatusLabel,
        v.SpaceCode, v.SpaceName, v.SpaceCapacity,
        v.SpaceTypeName, v.LocationName,
        v.SeatPrice, v.RoomPrice,
        v.BillingPeriodCode, v.BillingPeriodLabel,
        v.ChallanNumber, v.ChallanValidUntil,
        v.BookedOn

    FROM dbo.VW_WN_BookingSummary v
    WHERE v.UserEmail = @UserEmail
    ORDER BY v.BookedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetRecent]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetRecent]
    @Top INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
        v.BookingId, v.BookingPublicId,
        v.StartOn, v.EndOn,
        v.BookingStatusCode, v.BookingStatusLabel,
        v.UserName, v.UserEmail,
        v.SpaceCode, v.SpaceName,
        v.LocationName, v.CompanyName,
        v.RoomPrice, v.BillingPeriodCode,
        v.ChallanNumber, v.BookedOn
    FROM dbo.VW_WN_BookingSummary v
    ORDER BY v.BookedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetSmartAvailable]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_GetSmartAvailable]
    @CategoryCode NVARCHAR(30),
    @StartOn      DATETIME2,
    @EndOn        DATETIME2,
    @Capacity     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.SpaceTypeIdInt AS SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationIdInt
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId       = s.Id
          AND sp.IsActive      = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive  = 1
      AND sc.Code     = @CategoryCode
      AND (@Capacity  IS NULL OR s.Capacity >= @Capacity)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (1, 2)
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
      )
    ORDER BY s.Capacity ASC, TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_Insert]
    @UserId            INT,
    @SpaceId           INT,
    @PricingId         INT,           -- Resolved internally for security
    @StartOn           DATETIME2,
    @EndOn             DATETIME2,
    @Notes             NVARCHAR(MAX) = NULL,
    @CreatedById       INT           = NULL,
    @UserEmail         NVARCHAR(256) = NULL,
    @CustomerEmail     NVARCHAR(255) = NULL,
    @CustomerFirstName NVARCHAR(100) = NULL,
    @CustomerLastName  NVARCHAR(100) = NULL,
    @CustomerPhone     NVARCHAR(50)  = NULL,
    @CustomerCnic      NVARCHAR(50)  = NULL,
    @CustomerAddress   NVARCHAR(500) = NULL,
    @CustomerCityId    INT           = NULL,
    @CustomerNotes     NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        -- Validate space & capacity
        DECLARE @Capacity SMALLINT;
        DECLARE @CategoryCode NVARCHAR(50);
        DECLARE @SpaceGuid UNIQUEIDENTIFIER;
        DECLARE @SpaceCode NVARCHAR(50);
        DECLARE @SpaceName NVARCHAR(255);

        SELECT 
            @Capacity = s.Capacity,
            @CategoryCode = sc.Code,
            @SpaceGuid = s.PublicId,
            @SpaceCode = s.Code,
            @SpaceName = s.Name
        FROM dbo.WN_Spaces s
        JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeIdInt
        JOIN dbo.WN_SpaceCategories sc ON sc.Id = st.CategoryId
        WHERE s.Id = @SpaceId AND s.IsActive = 1;

        IF @Capacity IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'Space not found or inactive.' AS ErrorMessage;
            RETURN;
        END

        IF @Capacity <= 0
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'Space capacity must be greater than 0.' AS ErrorMessage;
            RETURN;
        END

        -- Determine intended billing period code based on space category
        DECLARE @BillingPeriodCode NVARCHAR(20);
        IF @CategoryCode = 'PrivateOffice' OR @CategoryCode = 'SharedSpace'
            SET @BillingPeriodCode = 'Monthly';
        ELSE
            SET @BillingPeriodCode = 'Hourly';

        -- Resolve the active pricing row internally based on space category and dates
        DECLARE @ResolvedPricingId INT;
        SELECT TOP 1 @ResolvedPricingId = sp.Id
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId       = @SpaceId
          AND sp.IsActive      = 1
          AND bp.Code          = @BillingPeriodCode
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC;

        IF @ResolvedPricingId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'No active ' + @BillingPeriodCode + ' pricing found for this space. Please configure pricing first.' AS ErrorMessage;
            RETURN;
        END

        -- Check for overlapping bookings
        IF EXISTS (
            SELECT 1 FROM dbo.WN_Bookings WITH (UPDLOCK)
            WHERE SpaceId         = @SpaceId
              AND IsDeleted       = 0
              AND BookingStatusId IN (1, 2)
              AND @StartOn        < EndOn
              AND @EndOn          > StartOn
        )
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'Space is not available for the requested period.' AS ErrorMessage;
            RETURN;
        END

        -- Resolve/Create Customer linked to @UserId
        DECLARE @CustomerCode NVARCHAR(20) = NULL;

        SELECT TOP 1 @CustomerCode = Code
        FROM dbo.WN_Customers WITH (UPDLOCK)
        WHERE UserId = @UserId;

        IF @CustomerCode IS NULL AND (@CustomerEmail IS NOT NULL OR @UserEmail IS NOT NULL)
        BEGIN
            DECLARE @EmailToFind NVARCHAR(255) = ISNULL(@CustomerEmail, @UserEmail);
            
            SELECT TOP 1 @CustomerCode = Code
            FROM dbo.WN_Customers WITH (UPDLOCK)
            WHERE Email = @EmailToFind;

            IF @CustomerCode IS NOT NULL
            BEGIN
                UPDATE dbo.WN_Customers
                SET UserId = @UserId
                WHERE Code = @CustomerCode;
            END
        END

        IF @CustomerCode IS NULL
        BEGIN
            DECLARE @FName NVARCHAR(100) = ISNULL(@CustomerFirstName, 'Customer');
            DECLARE @LName NVARCHAR(100) = @CustomerLastName;
            DECLARE @Email NVARCHAR(255) = ISNULL(@CustomerEmail, @UserEmail);
            DECLARE @Phone NVARCHAR(50)  = @CustomerPhone;

            IF @Email IS NULL
            BEGIN
                SELECT @Email = Email, @FName = ISNULL(@FName, Name)
                FROM dbo.WN_Users WITH (NOLOCK)
                WHERE Id = @UserId;
            END

            DECLARE @NewCustId INT;
            
            INSERT INTO dbo.WN_Customers (
                UserId, FirstName, LastName, Email, PhoneNumber, CNIC, Address, CityId, IsActive, CreatedAt, Notes
            )
            VALUES (
                @UserId, @FName, @LName, @Email, @Phone, @CustomerCnic, @CustomerAddress, @CustomerCityId, 1, SYSUTCDATETIME(), @CustomerNotes
            );

            SET @NewCustId = SCOPE_IDENTITY();
            SELECT @CustomerCode = Code FROM dbo.WN_Customers WHERE Id = @NewCustId;
        END

        -- Read pricing values from resolved pricing row
        DECLARE @SeatPrice        DECIMAL(18,4);
        DECLARE @SecurityDeposit  DECIMAL(18,4);
        DECLARE @RentAccountId    INT;
        DECLARE @DepositAccountId INT;

        SELECT
            @SeatPrice        = sp.SeatPrice,
            @SecurityDeposit  = sp.SecurityDeposit,
            @RentAccountId    = sp.RentAccountId,
            @DepositAccountId = sp.DepositAccountId
        FROM dbo.WN_SpacePricing sp
        WHERE sp.Id = @ResolvedPricingId;

        -- Validate SeatPrice
        IF @SeatPrice IS NULL OR @SeatPrice < 0
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'Pricing configuration has invalid SeatPrice.' AS ErrorMessage;
            RETURN;
        END

        -- Calculate Duration and Rent Amount
        DECLARE @Duration DECIMAL(18,2) = 1.0;
        DECLARE @RentAmount DECIMAL(18,2) = 0.0;
        
        IF @BillingPeriodCode = 'Monthly'
        BEGIN
            DECLARE @Months INT = DATEDIFF(month, @StartOn, @EndOn);
            IF @Months <= 0 SET @Months = 1;
            SET @Duration = CAST(@Months AS DECIMAL(18,2));
            
            IF @CategoryCode = 'PrivateOffice'
                SET @RentAmount = @SeatPrice * @Capacity * @Duration;
            ELSE
                SET @RentAmount = @SeatPrice * @Duration;
        END
        ELSE
        BEGIN
            DECLARE @Minutes INT = DATEDIFF(minute, @StartOn, @EndOn);
            DECLARE @Hours DECIMAL(18,2) = CEILING(CAST(@Minutes AS DECIMAL(18,2)) / 60.0);
            IF @Hours <= 0 SET @Hours = 1.0;
            SET @Duration = @Hours;
            SET @RentAmount = @SeatPrice * @Duration;
        END

        DECLARE @TotalAmount DECIMAL(18,2) = @RentAmount + ISNULL(@SecurityDeposit, 0);

        -- Generate challan number
        DECLARE @Today      DATE = CAST(SYSUTCDATETIME() AS DATE);
        DECLARE @SeqNum     INT;
        DECLARE @ChallanNum NVARCHAR(50);

        UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
        SET LastNumber = LastNumber + 1
        WHERE CounterDate = @Today;

        IF @@ROWCOUNT = 0
            INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
            SELECT @Today, 1
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
                WHERE CounterDate = @Today
            );

        SELECT @SeqNum = LastNumber
        FROM dbo.WN_ChallanCounter WITH (NOLOCK)
        WHERE CounterDate = @Today;

        SET @ChallanNum = 'WN-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                        + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);

        DECLARE @ChallanValidUntil DATETIME = DATEADD(DAY, 5, @Today);
        DECLARE @UserGuid UNIQUEIDENTIFIER;
        SELECT @UserGuid = PublicId FROM dbo.WN_Users WHERE Id = @UserId;
        DECLARE @CreatedByGuid UNIQUEIDENTIFIER = NULL;
        IF @CreatedById IS NOT NULL
            SELECT @CreatedByGuid = PublicId FROM dbo.WN_Users WHERE Id = @CreatedById;

        -- Insert booking record
        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, UserGuid, CustomerCode, SpaceGuid,
            StartDateTime, EndDateTime, TotalAmount, BookingStatus,
            BankAccountId, SecurityDepositAccountId, Status, Notes, RejectReason,
            CreatedOn, UpdatedOn, CreatedBy, UpdatedBy, TransactionDate,
            ChallanNumber, ValidityDate, UserId, SpaceId, PricingId,
            StartOn, EndOn, BookingStatusId, CancelReason, IsDeleted, CreatedById, UpdatedById
        )
        VALUES (
            NEWID(), SYSUTCDATETIME(), @UserGuid, @CustomerCode, @SpaceGuid,
            @StartOn, @EndOn, @TotalAmount, 1,
            @RentAccountId, @DepositAccountId, 1, @Notes, NULL,
            SYSUTCDATETIME(), NULL, @CreatedByGuid, NULL, SYSUTCDATETIME(),
            @ChallanNum, @ChallanValidUntil, @UserId, @SpaceId, @ResolvedPricingId,
            @StartOn, @EndOn, 1, NULL, 0, @CreatedById, NULL
        );

        DECLARE @BookingId INT = SCOPE_IDENTITY();

        -- Insert Challan
        INSERT INTO dbo.WN_Challans
            (BookingId, ChallanNumber, ValidUntil, StatusId, CreatedById)
        VALUES
            (@BookingId, @ChallanNum, @ChallanValidUntil, 1, @CreatedById);

        -- Insert Rent BookingLine
        -- For private rooms, UnitPrice is SeatPrice × Capacity. For others, it is SeatPrice.
        DECLARE @UnitPrice DECIMAL(18,2);
        IF @CategoryCode = 'PrivateOffice'
            SET @UnitPrice = @SeatPrice * @Capacity;
        ELSE
            SET @UnitPrice = @SeatPrice;

        INSERT INTO dbo.WN_BookingLines
            (BookingId, ChargeTypeId, Description, Quantity, UnitPrice,
             DiscountAmount, TaxRate, AccountId, CreatedById)
        VALUES
            (@BookingId, 1, 'Room Rent', @Duration, @UnitPrice,
             0, 0, @RentAccountId, @CreatedById);

        -- Insert Security Deposit BookingLine
        IF @SecurityDeposit > 0
            INSERT INTO dbo.WN_BookingLines
                (BookingId, ChargeTypeId, Description, Quantity, UnitPrice,
                 DiscountAmount, TaxRate, AccountId, CreatedById)
            -- Quantity 1 is correct for SecurityDeposit
            VALUES
                (@BookingId, 2, 'Security Deposit', 1, @SecurityDeposit,
                 0, 0, @DepositAccountId, @CreatedById);

        -- Insert BookingDetails (RoomRent and SecurityDeposit lines)
        DECLARE @BookingGuid UNIQUEIDENTIFIER;
        SELECT @BookingGuid = PublicId FROM dbo.WN_Bookings WHERE Id = @BookingId;

        DECLARE @CreatedByEmail NVARCHAR(100);
        SELECT @CreatedByEmail = Email FROM dbo.WN_Users WHERE Id = @UserId;
        DECLARE @Creator NVARCHAR(100) = ISNULL(@UserEmail, @CreatedByEmail);

        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy, IsDeleted, CustomerCode,
             CustomerName, CustomerEmail, SpaceName, SpaceCode, SpaceCategory, StartDateTime, EndDateTime,
             RentAmount, SecurityDeposit, TotalAmount, RentAccountId, DepositAccountId, Notes)
        VALUES
            (NEWID(), @BookingGuid, 'RoomRent', @RentAmount, @RentAccountId, SYSUTCDATETIME(), @Creator, 0, @CustomerCode,
             ISNULL(@CustomerFirstName + ' ' + @CustomerLastName, 'Customer'), @Creator, @SpaceName, @SpaceCode, @CategoryCode, @StartOn, @EndOn,
             @RentAmount, @SecurityDeposit, @TotalAmount, @RentAccountId, @DepositAccountId, @Notes);

        IF @SecurityDeposit > 0
            INSERT INTO dbo.WN_BookingDetails
                (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy, IsDeleted, CustomerCode,
                 CustomerName, CustomerEmail, SpaceName, SpaceCode, SpaceCategory, StartDateTime, EndDateTime,
                 RentAmount, SecurityDeposit, TotalAmount, RentAccountId, DepositAccountId, Notes)
            VALUES
                (NEWID(), @BookingGuid, 'SecurityDeposit', @SecurityDeposit, @DepositAccountId, SYSUTCDATETIME(), @Creator, 0, @CustomerCode,
                 ISNULL(@CustomerFirstName + ' ' + @CustomerLastName, 'Customer'), @Creator, @SpaceName, @SpaceCode, @CategoryCode, @StartOn, @EndOn,
                 @RentAmount, @SecurityDeposit, @TotalAmount, @RentAccountId, @DepositAccountId, @Notes);

        COMMIT TRANSACTION;

        SELECT
            @BookingId  AS BookingId,
            b.PublicId  AS BookingPublicId,
            @ChallanNum AS ChallanNumber,
            @ChallanValidUntil AS ChallanValidUntil,
            NULL        AS ErrorMessage
        FROM dbo.WN_Bookings b WHERE b.Id = @BookingId;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @Msg NVARCHAR(4000) = ERROR_MESSAGE();
        SELECT NULL AS BookingId, NULL AS BookingPublicId,
               NULL AS ChallanNumber, NULL AS ChallanValidUntil,
               @Msg AS ErrorMessage;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_InsertSmart]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_InsertSmart]
    @UserEmail         NVARCHAR(256),
    @CategoryCode      NVARCHAR(30),
    @StartOn           DATETIME2,
    @EndOn             DATETIME2,
    @Capacity          INT           = NULL,
    @Notes             NVARCHAR(MAX) = NULL,
    @CreatedById       INT           = NULL,
    @CustomerEmail     NVARCHAR(255) = NULL,
    @CustomerFirstName NVARCHAR(100) = NULL,
    @CustomerLastName  NVARCHAR(100) = NULL,
    @CustomerPhone     NVARCHAR(50)  = NULL,
    @CustomerCnic      NVARCHAR(50)  = NULL,
    @CustomerAddress   NVARCHAR(500) = NULL,
    @CustomerCityId    INT           = NULL,
    @CustomerNotes     NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @UserId    INT;
        DECLARE @SpaceId   INT;
        DECLARE @PricingId INT;

        SELECT @UserId = Id FROM dbo.WN_Users WITH (NOLOCK)
        WHERE Email = @UserEmail AND IsActive = 1;

        IF @UserId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'User not found or inactive.' AS ErrorMessage;
            RETURN;
        END

        SELECT TOP 1 @SpaceId = s.Id
        FROM dbo.WN_Spaces          s  WITH (UPDLOCK)
        JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
        JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
        WHERE s.IsActive  = 1
          AND sc.Code     = @CategoryCode
          AND (@Capacity  IS NULL OR s.Capacity >= @Capacity)
          AND NOT EXISTS (
              SELECT 1 FROM dbo.WN_Bookings bk
              WHERE bk.SpaceId         = s.Id
                AND bk.IsDeleted       = 0
                AND bk.BookingStatusId IN (1, 2)
                AND @StartOn           < bk.EndOn
                AND @EndOn             > bk.StartOn
          )
        ORDER BY s.Capacity ASC, TRY_CAST(s.Code AS INT), s.Code;

        IF @SpaceId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'No available space for the requested period.' AS ErrorMessage;
            RETURN;
        END

        SELECT TOP 1 @PricingId = sp.Id
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId       = @SpaceId
          AND sp.IsActive      = 1
          AND bp.Code          = 'Monthly'
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC;

        IF @PricingId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'No active pricing found for selected space.' AS ErrorMessage;
            RETURN;
        END

        COMMIT TRANSACTION;

        EXEC dbo.WN_Bookings_Insert
            @UserId            = @UserId,
            @SpaceId           = @SpaceId,
            @PricingId         = @PricingId,
            @StartOn           = @StartOn,
            @EndOn             = @EndOn,
            @Notes             = @Notes,
            @CreatedById       = @CreatedById,
            @UserEmail         = @UserEmail,
            @CustomerEmail     = @CustomerEmail,
            @CustomerFirstName = @CustomerFirstName,
            @CustomerLastName  = @CustomerLastName,
            @CustomerPhone     = @CustomerPhone,
            @CustomerCnic      = @CustomerCnic,
            @CustomerAddress   = @CustomerAddress,
            @CustomerCityId    = @CustomerCityId,
            @CustomerNotes     = @CustomerNotes;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @Msg NVARCHAR(4000) = ERROR_MESSAGE();
        SELECT NULL AS BookingId, NULL AS BookingPublicId,
               NULL AS ChallanNumber, NULL AS ChallanValidUntil,
               @Msg AS ErrorMessage;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Reassign]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_Reassign]
    @Id           INT,
    @NewSpaceId   INT,
    @NewPricingId INT,
    @UserEmail    NVARCHAR(256) = NULL,
    @UpdatedById  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @StartOn DATETIME2, @EndOn DATETIME2;
        SELECT @StartOn = StartOn, @EndOn = EndOn
        FROM dbo.WN_Bookings WHERE Id = @Id AND IsDeleted = 0;

        IF @StartOn IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Booking not found.', 16, 1); RETURN;
        END

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Bookings WITH (UPDLOCK)
            WHERE SpaceId         = @NewSpaceId
              AND IsDeleted       = 0
              AND BookingStatusId IN (1, 2)
              AND Id             <> @Id
              AND @StartOn        < EndOn
              AND @EndOn          > StartOn
        )
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('New space is not available for the booking period.', 16, 1); RETURN;
        END

        UPDATE dbo.WN_Bookings SET
            SpaceId     = @NewSpaceId,
            PricingId   = @NewPricingId,
            UpdatedOn   = SYSUTCDATETIME(),
            UpdatedById = @UpdatedById
        WHERE Id = @Id;

        COMMIT TRANSACTION;
        SELECT @@ROWCOUNT AS AffectedRows;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_Update]
    @Id          INT,
    @StartOn     DATETIME2     = NULL,
    @EndOn       DATETIME2     = NULL,
    @Notes       NVARCHAR(MAX) = NULL,
    @UpdatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Bookings SET
        StartOn     = ISNULL(@StartOn, StartOn),
        EndOn       = ISNULL(@EndOn,   EndOn),
        Notes       = ISNULL(@Notes,   Notes),
        UpdatedOn   = SYSUTCDATETIME(),
        UpdatedById = @UpdatedById
    WHERE Id = @Id AND IsDeleted = 0;
    SELECT @@ROWCOUNT AS AffectedRows;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Bookings_UpdateStatus]
    @Id              INT,
    @BookingStatusId TINYINT,
    @UpdatedById     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Bookings SET
        BookingStatusId = @BookingStatusId,
        UpdatedOn       = SYSUTCDATETIME(),
        UpdatedById     = @UpdatedById
    WHERE Id = @Id AND IsDeleted = 0;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookTour_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 20. WN_BookTour_Insert ────────────────────────────────
CREATE   PROCEDURE [dbo].[WN_BookTour_Insert]
    @Name        NVARCHAR(255),
    @Email       NVARCHAR(256),
    @Message     NVARCHAR(MAX),
    @PhoneNumber NVARCHAR(20),
    @UserId      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NewGUID  UNIQUEIDENTIFIER = NEWID();
    DECLARE @UserGUID UNIQUEIDENTIFIER = NULL;

    IF @UserId IS NOT NULL
        SELECT @UserGUID = IdGUID FROM dbo.WN_Users WITH (NOLOCK) WHERE Id = @UserId;

    INSERT INTO dbo.WN_BookTour (IdGUID, Name, Email, Message, PhoneNumber, CreatedOn, CreatedBy)
    VALUES (@NewGUID, @Name, @Email, @Message, @PhoneNumber, GETDATE(), @UserGUID);

    SELECT SCOPE_IDENTITY() AS NewId, @NewGUID AS NewIdGuid;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Branches_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Branches_GetList]
    @CompanyId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Id, b.PublicId, b.[Description] AS Name, b.Code, b.CompanyId,
           co.CompanyName AS CompanyName, b.CityId, ci.Name AS CityName, b.IsActive
    FROM dbo.Branches  b  WITH (NOLOCK)
    JOIN dbo.Company co WITH (NOLOCK) ON co.Id = b.CompanyId
    LEFT JOIN dbo.WN_Cities ci WITH (NOLOCK) ON ci.Id = b.CityId
    WHERE b.IsActive = 1
      AND (@CompanyId IS NULL OR b.CompanyId = @CompanyId)
    ORDER BY b.[Description] ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challan_ExtendValidity]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_Challan_ExtendValidity]
    @BookingId      INT,
    @NewExpiryDate  DATE,
    @UpdatedBy      NVARCHAR(200),
    @Remarks        NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldExpiryDate DATE;
    SELECT @OldExpiryDate = ValidityDate FROM dbo.WN_Bookings WHERE Id = @BookingId;

    IF @OldExpiryDate IS NULL
    BEGIN
        RAISERROR('Booking not found.', 16, 1);
        RETURN;
    END

    IF @NewExpiryDate <= @OldExpiryDate
    BEGIN
        RAISERROR('New expiry date must be after the current expiry date.', 16, 1);
        RETURN;
    END

    UPDATE dbo.WN_Bookings
    SET ValidityDate = @NewExpiryDate,
        UpdatedOn    = GETUTCDATE()
    WHERE Id = @BookingId;

    -- Also update the linked payment voucher expiry if table has ExpiryDate column
    -- (safe: only runs if column exists)
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'WN_Payments' AND COLUMN_NAME = 'ExpiryDate'
    )
    BEGIN
        UPDATE dbo.WN_Payments
        SET ExpiryDate = @NewExpiryDate
        WHERE CAST(BookingId AS NVARCHAR(36)) IN (
            SELECT CAST(IdGUID AS NVARCHAR(36)) FROM dbo.WN_Bookings WHERE Id = @BookingId
        );
    END

    -- Audit log (safe: only if table exists)
    IF OBJECT_ID('dbo.WN_ChallanValidityLog', 'U') IS NOT NULL
    BEGIN
        INSERT INTO dbo.WN_ChallanValidityLog
            (BookingId, OldExpiryDate, NewExpiryDate, UpdatedBy, UpdatedAt, Remarks)
        VALUES
            (@BookingId, @OldExpiryDate, @NewExpiryDate, @UpdatedBy, GETUTCDATE(), @Remarks);
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challan_Search]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_Challan_Search]
    @Query NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        b.Id                                            AS bookingId,
        CAST(b.IdGUID AS NVARCHAR(36))                  AS bookingGuid,
        b.ChallanNumber                                 AS challanNumber,
        b.ValidityDate                                  AS currentExpiryDate,
        b.BookingStatus                                 AS bookingStatus,
        p.TransactionRef                                AS voucherNumber,
        p.PaymentStatus                                 AS voucherStatus,
        u.Name                                          AS customerName,
        s.Name                                          AS spaceName,
        b.StartDateTime                                 AS startDateTime,
        b.EndDateTime                                   AS endDateTime,
        b.TotalAmount                                   AS totalAmount
    FROM dbo.WN_Bookings b
    LEFT JOIN dbo.WN_Users u    ON u.IdGUID = b.UserGuid
    LEFT JOIN dbo.WN_Spaces s   ON s.IdGUID = b.SpaceGuid
    LEFT JOIN dbo.WN_Payments p ON CAST(p.BookingId AS NVARCHAR(36)) = CAST(b.IdGUID AS NVARCHAR(36))
    WHERE
        CAST(b.Id AS NVARCHAR(20)) = @Query
        OR b.ChallanNumber         = @Query
        OR p.TransactionRef        = @Query
        OR b.ChallanNumber LIKE '%' + @Query + '%'
    ORDER BY b.Id DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_ExtendValidity]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Challans_ExtendValidity]
    @ChallanId     INT,
    @NewValidUntil DATE,
    @UpdatedById   INT,
    @Remarks       NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OldValidUntil DATE;
    SELECT @OldValidUntil = ValidUntil FROM dbo.WN_Challans WHERE Id = @ChallanId;

    IF @OldValidUntil IS NULL
    BEGIN RAISERROR('Challan not found.', 16, 1); RETURN; END
    IF @NewValidUntil <= @OldValidUntil
    BEGIN RAISERROR('New validity date must be after current validity date.', 16, 1); RETURN; END

    UPDATE dbo.WN_Challans SET ValidUntil = @NewValidUntil WHERE Id = @ChallanId;

    INSERT INTO dbo.WN_ChallanAuditLog
        (ChallanId, OldValidUntil, NewValidUntil, UpdatedById, Remarks)
    VALUES
        (@ChallanId, @OldValidUntil, @NewValidUntil, @UpdatedById, @Remarks);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_GetByBooking]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 9: CHALLANS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Challans_GetByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.Id, c.PublicId, c.BookingId, c.ChallanNumber,
        c.IssuedOn, c.ValidUntil, c.StatusId, c.Notes, c.CreatedOn
    FROM dbo.WN_Challans c WITH (NOLOCK)
    WHERE c.BookingId = @BookingId
    ORDER BY c.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_Search]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Challans_Search]
    @Query NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        c.Id, c.PublicId, c.ChallanNumber,
        c.IssuedOn, c.ValidUntil, c.StatusId,
        v.BookingId, v.BookingPublicId,
        v.UserName, v.UserEmail,
        v.SpaceName, v.SpaceCode,
        v.StartOn, v.EndOn,
        v.RoomPrice, v.BillingPeriodCode,
        v.BookingStatusCode
    FROM dbo.WN_Challans          c  WITH (NOLOCK)
    JOIN dbo.VW_WN_BookingSummary v  ON v.BookingId = c.BookingId
    WHERE c.ChallanNumber = @Query
       OR CAST(c.BookingId AS NVARCHAR(20)) = @Query
       OR c.ChallanNumber LIKE '%' + @Query + '%'
    ORDER BY c.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Cities_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 2: LOOKUP TABLES
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Cities_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ci.Id, ci.PublicId, ci.Name, ci.CountryId,
           co.Name AS CountryName
    FROM dbo.WN_Cities    ci WITH (NOLOCK)
    LEFT JOIN dbo.WN_Countries co WITH (NOLOCK) ON co.Id = ci.CountryId
    WHERE ci.IsActive = 1
    ORDER BY ci.Name ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Companies_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Companies_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, PublicId, CompanyName AS Name, LegalName, Email, Phone, IsActive
    FROM dbo.Company WITH (NOLOCK)
    WHERE IsActive = 1
    ORDER BY CompanyName ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Contacts_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.WN_Contacts WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 10: CONTACTS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Contacts_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        c.Id, c.PublicId, c.ContactType,
        c.UserId, c.Name, c.Email, c.PhoneNumber,
        c.Message, c.StatusId, c.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Contacts c WITH (NOLOCK)
    WHERE (@Search IS NULL OR @Search = ''
           OR c.Name  LIKE '%' + @Search + '%'
           OR c.Email LIKE '%' + @Search + '%')
    ORDER BY c.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_GetRecent]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Contacts_GetRecent]
    @Top INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
        Id, PublicId, ContactType, Name, Email,
        PhoneNumber, Message, StatusId, CreatedOn
    FROM dbo.WN_Contacts WITH (NOLOCK)
    ORDER BY CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Contacts_Insert]
    @ContactType NVARCHAR(20)  = 'Contact',
    @UserId      INT           = NULL,
    @Name        NVARCHAR(200),
    @Email       NVARCHAR(256),
    @PhoneNumber NVARCHAR(50)  = NULL,
    @Message     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Contacts
        (ContactType, UserId, Name, Email, PhoneNumber, Message, StatusId)
    VALUES
        (@ContactType, @UserId, @Name, @Email, @PhoneNumber, @Message, 1);
    SELECT Id, PublicId FROM dbo.WN_Contacts WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Contacts_UpdateStatus]
    @Id          INT,
    @StatusId    TINYINT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Contacts SET
        StatusId    = @StatusId,
        UpdatedOn   = SYSUTCDATETIME(),
        UpdatedById = @UpdatedById
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_CreateBookingWithAutoAssignment]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.7  WN_CreateBookingWithAutoAssignment
-- Removed reading s.CompanyId/s.BranchId into local vars.
-- Removed CompanyId/BranchId from WN_Bookings INSERT.
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_CreateBookingWithAutoAssignment]
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

        SELECT @UserId = Id, @UserGuid = IdGUID
        FROM dbo.WN_Users WITH (NOLOCK)
        WHERE Email = @Email AND Status = 1;

        IF @UserId IS NULL
        BEGIN
            RAISERROR('User not found or inactive', 16, 1); RETURN;
        END

        SELECT @SpaceTypeId = IdGUID
        FROM dbo.WN_SpaceTypes WITH (NOLOCK)
        WHERE Description = @SpaceType AND Status = 1;

        IF @SpaceTypeId IS NULL
        BEGIN
            RAISERROR('Invalid space type', 16, 1); RETURN;
        END

        SELECT TOP 1
            @SpaceId           = Id,
            @SpaceGuid         = IdGUID,
            @AssignedSpaceName = Name
        FROM (
            SELECT
                s.Id, s.IdGUID, s.Name, s.Code,
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
            RAISERROR('No available spaces for the requested time period', 16, 1); RETURN;
        END

        SET @BookingGuid = NEWID();

        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, UserGuid, SpaceGuid,
            StartDateTime, EndDateTime, Notes, TotalAmount,
            BookingStatus, Status, CreatedOn, CreatedBy
        ) VALUES (
            @BookingGuid, GETDATE(), @UserGuid, @SpaceGuid,
            @StartDateTime, @EndDateTime, @Notes, @TotalAmount,
            1, 1, GETDATE(), @UserGuid
        );

        SET @BookingId       = SCOPE_IDENTITY();
        SET @AssignedSpaceId = @SpaceId;

        IF @PaymentMethod IS NOT NULL AND @TotalAmount > 0
        BEGIN
            INSERT INTO dbo.WN_Payments (
                IdGUID, UserId, BookingId, Amount, Currency,
                PaymentMethod, TransactionRef, PaymentStatus, CreatedAt, PaidAt
            ) VALUES (
                NEWID(), @UserGuid, @BookingGuid, @TotalAmount, 'PKR',
                @PaymentMethod, @PaymentRef, 'Pending', GETDATE(), NULL
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
/****** Object:  StoredProcedure [dbo].[WN_Customers_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── WN_Customers_Delete ───────────────────────────────────────────────────────
CREATE PROCEDURE [dbo].[WN_Customers_Delete]
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Customers
    SET IsActive = 0, UpdatedAt = GETDATE()
    WHERE IdGUID = @IdGUID;
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetByGuid]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── WN_Customers_GetByGuid ────────────────────────────────────────────────────
CREATE PROCEDURE [dbo].[WN_Customers_GetByGuid]
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Name AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.WN_Cities ci ON ci.Id = c.CityId
    WHERE c.IdGUID = @IdGUID;
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── WN_Customers_GetList ──────────────────────────────────────────────────────
CREATE PROCEDURE [dbo].[WN_Customers_GetList]
    @Search NVARCHAR(255) = NULL,
    @Page   INT = 1,
    @Limit  INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Name AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.WN_Cities ci ON ci.Id = c.CityId
    WHERE (@Search IS NULL OR
           c.FirstName    LIKE '%' + @Search + '%' OR
           c.LastName     LIKE '%' + @Search + '%' OR
           c.Email        LIKE '%' + @Search + '%' OR
           c.Code         LIKE '%' + @Search + '%' OR
           c.PhoneNumber  LIKE '%' + @Search + '%')
    ORDER BY c.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── WN_Customers_Insert ───────────────────────────────────────────────────────
CREATE PROCEDURE [dbo].[WN_Customers_Insert]
    @FirstName      NVARCHAR(100),
    @LastName       NVARCHAR(100)  = NULL,
    @Email          NVARCHAR(255),
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CityId         INT            = NULL,
    @Notes          NVARCHAR(1000) = NULL,
    @CreatedBy      NVARCHAR(255)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Customers
        (FirstName, LastName, Email, PhoneNumber, CnicOrPassport, Address, CityId, Notes, CreatedBy)
    VALUES
        (@FirstName, @LastName, @Email, @PhoneNumber, @CnicOrPassport, @Address, @CityId, @Notes, @CreatedBy);

    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Name AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.WN_Cities ci ON ci.Id = c.CityId
    WHERE c.Id = SCOPE_IDENTITY();
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Search]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── WN_Customers_Search ───────────────────────────────────────────────────────
CREATE PROCEDURE [dbo].[WN_Customers_Search]
    @Query NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 20
           c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Name AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.WN_Cities ci ON ci.Id = c.CityId
    WHERE c.IsActive = 1 AND (
           c.FirstName   LIKE '%' + @Query + '%' OR
           c.LastName    LIKE '%' + @Query + '%' OR
           c.Email       LIKE '%' + @Query + '%' OR
           c.Code        LIKE '%' + @Query + '%' OR
           c.PhoneNumber LIKE '%' + @Query + '%')
    ORDER BY c.FirstName;
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── WN_Customers_Update ───────────────────────────────────────────────────────
CREATE PROCEDURE [dbo].[WN_Customers_Update]
    @IdGUID         UNIQUEIDENTIFIER,
    @FirstName      NVARCHAR(100)  = NULL,
    @LastName       NVARCHAR(100)  = NULL,
    @Email          NVARCHAR(255)  = NULL,
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CityId         INT            = NULL,
    @Notes          NVARCHAR(1000) = NULL,
    @IsActive       BIT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Customers SET
        FirstName      = ISNULL(@FirstName,      FirstName),
        LastName       = ISNULL(@LastName,        LastName),
        Email          = ISNULL(@Email,           Email),
        PhoneNumber    = ISNULL(@PhoneNumber,     PhoneNumber),
        CnicOrPassport = ISNULL(@CnicOrPassport,  CnicOrPassport),
        Address        = ISNULL(@Address,         Address),
        CityId         = ISNULL(@CityId,          CityId),
        Notes          = ISNULL(@Notes,           Notes),
        IsActive       = ISNULL(@IsActive,        IsActive),
        UpdatedAt      = GETDATE()
    WHERE IdGUID = @IdGUID;
END

GO
/****** Object:  StoredProcedure [dbo].[WN_Dashboard_GetSummary]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 17: DASHBOARD
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Dashboard_GetSummary]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalSpaces      INT;
    DECLARE @OccupiedNow      INT;
    DECLARE @AvailableNow     INT;
    DECLARE @TotalUsers       INT;
    DECLARE @ActiveMembers    INT;
    DECLARE @RevenueThisMonth DECIMAL(18,4);
    DECLARE @BookingsToday    INT;

    SELECT @TotalSpaces = COUNT(*) FROM dbo.WN_Spaces WHERE IsActive = 1;

    SELECT @OccupiedNow = COUNT(DISTINCT SpaceId)
    FROM dbo.WN_Bookings
    WHERE IsDeleted       = 0
      AND BookingStatusId IN (1, 2)
      AND StartOn         <= SYSUTCDATETIME()
      AND EndOn           >= SYSUTCDATETIME();

    SET @AvailableNow = @TotalSpaces - ISNULL(@OccupiedNow, 0);

    SELECT @TotalUsers = COUNT(*) FROM dbo.WN_Users WHERE IsActive = 1;

    -- Active members: check new table first, fall back to old
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StatusId')
    BEGIN
        DECLARE @sqlM NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM dbo.WN_Memberships WHERE StatusId = 1 AND StartOn <= SYSUTCDATETIME() AND (EndOn IS NULL OR EndOn >= SYSUTCDATETIME());';
        EXEC sp_executesql @sqlM, N'@cnt INT OUTPUT', @ActiveMembers OUTPUT;
    END
    ELSE
        SELECT @ActiveMembers = COUNT(*)
        FROM dbo.WN_MemberShips_Temp
        WHERE Status   = 'Active'
          AND IsActive = 1;

    SELECT @RevenueThisMonth = ISNULL(SUM(Amount), 0)
    FROM dbo.WN_Payments
    WHERE StatusId    = 2
      AND YEAR(PaidOn)  = YEAR(SYSUTCDATETIME())
      AND MONTH(PaidOn) = MONTH(SYSUTCDATETIME());

    SELECT @BookingsToday = COUNT(*)
    FROM dbo.WN_Bookings
    WHERE IsDeleted = 0
      AND CAST(CreatedOn AS DATE) = CAST(SYSUTCDATETIME() AS DATE);

    -- Summary row
    SELECT
        @TotalSpaces      AS TotalSpaces,
        @OccupiedNow      AS OccupiedNow,
        @AvailableNow     AS AvailableNow,
        @TotalUsers       AS TotalUsers,
        @ActiveMembers    AS ActiveMembers,
        @RevenueThisMonth AS RevenueThisMonth,
        @BookingsToday    AS BookingsToday;

    -- Booking counts by status
    SELECT
        bs.Code  AS StatusCode,
        bs.Label AS StatusLabel,
        COUNT(b.Id) AS BookingCount
    FROM dbo.WN_BookingStatuses bs
    LEFT JOIN dbo.WN_Bookings   b ON b.BookingStatusId = bs.Id AND b.IsDeleted = 0
    GROUP BY bs.Id, bs.Code, bs.Label
    ORDER BY bs.Id;

    -- Recent 5 bookings
    SELECT TOP 5
        v.BookingId, v.BookingPublicId,
        v.UserName, v.UserEmail,
        v.SpaceCode, v.SpaceName,
        v.StartOn, v.EndOn,
        v.BookingStatusCode, v.BookingStatusLabel,
        v.RoomPrice, v.BookedOn
    FROM dbo.VW_WN_BookingSummary v
    ORDER BY v.BookedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_EmailOtps_GetActive]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_EmailOtps_GetActive]
    @Email   NVARCHAR(256),
    @Purpose NVARCHAR(60)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 Id, Email, Purpose, CodeHash, CreatedOn, ExpiresOn, IdentityToken
    FROM dbo.WN_EmailOtps WITH (NOLOCK)
    WHERE Email     = @Email
      AND Purpose   = @Purpose
      AND UsedOn    IS NULL
      AND ExpiresOn > SYSUTCDATETIME()
    ORDER BY CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_EmailOtps_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_EmailOtps_Insert]
    @Email         NVARCHAR(256),
    @Purpose       NVARCHAR(60),
    @CodeHash      NVARCHAR(500),
    @ExpiresOn     DATETIME2,
    @IdentityToken NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- Invalidate any existing unused OTPs for same email+purpose
    UPDATE dbo.WN_EmailOtps SET UsedOn = SYSUTCDATETIME()
    WHERE Email   = @Email
      AND Purpose = @Purpose
      AND UsedOn  IS NULL;

    INSERT INTO dbo.WN_EmailOtps (Email, Purpose, CodeHash, ExpiresOn, IdentityToken)
    VALUES (@Email, @Purpose, @CodeHash, @ExpiresOn, @IdentityToken);

    SELECT Id FROM dbo.WN_EmailOtps WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_EmailOtps_MarkUsed]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_EmailOtps_MarkUsed]
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_EmailOtps SET UsedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Floors_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Floors_GetList]
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT f.Id, f.LocationId, l.Name AS LocationName,
           f.Name, f.FloorNumber, f.IsActive
    FROM dbo.WN_Floors    f  WITH (NOLOCK)
    JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = f.LocationId
    WHERE f.IsActive = 1
      AND (@LocationId IS NULL OR f.LocationId = @LocationId)
    ORDER BY f.LocationId, f.FloorNumber;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Floors_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Floors_Insert]
    @LocationId  INT,
    @Name        NVARCHAR(100),
    @FloorNumber SMALLINT = 0,
    @CreatedById INT      = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Floors (LocationId, Name, FloorNumber, CreatedById)
    VALUES (@LocationId, @Name, @FloorNumber, @CreatedById);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_GalleryImages_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_GalleryImages SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_GetAll]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_GalleryImages_GetAll]
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        g.Id, g.PublicId, g.LocationId, l.Name AS LocationName,
        g.SpaceId, s.Name AS SpaceName,
        g.Title, g.Description, g.ImageUrl, g.SortOrder, g.IsActive
    FROM dbo.WN_GalleryImages g  WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = g.LocationId
    LEFT JOIN dbo.WN_Spaces    s WITH (NOLOCK) ON s.Id = g.SpaceId
    WHERE g.IsActive = 1
      AND (@LocationId IS NULL OR g.LocationId = @LocationId)
    ORDER BY g.SortOrder ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 6: GALLERY
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_GalleryImages_GetList]
    @Page       INT = 1,
    @Limit      INT = 20,
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        g.Id, g.PublicId, g.LocationId, l.Name AS LocationName,
        g.SpaceId, s.Name AS SpaceName,
        g.Title, g.Description, g.ImageUrl, g.SortOrder, g.IsActive,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_GalleryImages g  WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = g.LocationId
    LEFT JOIN dbo.WN_Spaces    s WITH (NOLOCK) ON s.Id = g.SpaceId
    WHERE g.IsActive = 1
      AND (@LocationId IS NULL OR g.LocationId = @LocationId)
    ORDER BY g.SortOrder ASC, g.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_GalleryImages_Insert]
    @LocationId  INT           = NULL,
    @SpaceId     INT           = NULL,
    @Title       NVARCHAR(150) = NULL,
    @Description NVARCHAR(1000)= NULL,
    @ImageUrl    NVARCHAR(500),
    @SortOrder   INT           = 0,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_GalleryImages
        (LocationId, SpaceId, Title, Description, ImageUrl, SortOrder, IsActive, CreatedById)
    VALUES
        (@LocationId, @SpaceId, @Title, @Description, @ImageUrl, @SortOrder, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_GalleryImages WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_GalleryImages_Update]
    @Id          INT,
    @Title       NVARCHAR(150)  = NULL,
    @Description NVARCHAR(1000) = NULL,
    @ImageUrl    NVARCHAR(500)  = NULL,
    @SortOrder   INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_GalleryImages SET
        Title       = ISNULL(@Title,       Title),
        Description = ISNULL(@Description, Description),
        ImageUrl    = ISNULL(@ImageUrl,    ImageUrl),
        SortOrder   = ISNULL(@SortOrder,   SortOrder)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GetAvailableSpaces]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_GetAvailableSpaces]
    @SpaceType NVARCHAR(100),
    @StartDateTime DATETIME,
    @EndDateTime DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @SpaceTypeId UNIQUEIDENTIFIER;
    
    -- Get space type GUID
    SELECT @SpaceTypeId = IdGUID 
    FROM dbo.WN_SpaceTypes WITH (NOLOCK)
    WHERE Description = @SpaceType AND Status = 1;
    
    IF @SpaceTypeId IS NULL
    BEGIN
        RAISERROR('Invalid space type', 16, 1);
        RETURN;
    END
    
    -- Get available spaces with naming convention priority
    SELECT 
        s.Id,
        s.IdGUID,
        s.Name,
        s.Code,
        s.PricePerDay,
        s.PricePerHour,
        st.Description AS SpaceType,
        l.Name AS LocationName,
        -- Priority based on naming convention
        CASE 
            WHEN @SpaceType LIKE '%Private%' AND s.Code LIKE '30%' THEN 1
            WHEN @SpaceType LIKE '%Shared%' AND s.Code LIKE '31%' THEN 1  
            WHEN @SpaceType LIKE '%Meeting%' AND s.Code LIKE '32%' THEN 1
            ELSE 2
        END AS Priority,
        -- Extract numeric part for ordering within same prefix
        CASE 
            WHEN ISNUMERIC(SUBSTRING(s.Code, 3, LEN(s.Code)-2)) = 1 
            THEN CAST(SUBSTRING(s.Code, 3, LEN(s.Code)-2) AS INT)
            ELSE 999
        END AS CodeNumber
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    INNER JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON s.SpaceTypeId = st.IdGUID
    INNER JOIN dbo.WN_Locations l WITH (NOLOCK) ON s.LocationId = l.IdGUID
    WHERE s.SpaceTypeId = @SpaceTypeId 
        AND s.Status = 1
        AND s.IdGUID NOT IN (
            -- Exclude spaces with conflicting bookings (compare GUIDs)
            SELECT DISTINCT b.SpaceGuid
            FROM dbo.WN_Bookings b WITH (NOLOCK)
            WHERE b.BookingStatus IN (1, 4) -- Confirmed or Completed
                AND (
                    (@StartDateTime >= b.StartDateTime AND @StartDateTime < b.EndDateTime) OR
                    (@EndDateTime > b.StartDateTime AND @EndDateTime <= b.EndDateTime) OR
                    (@StartDateTime <= b.StartDateTime AND @EndDateTime >= b.EndDateTime)
                )
        )
    ORDER BY Priority ASC, CodeNumber ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_IntegrationLog_GetPending]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_IntegrationLog_GetPending]
    @System     NVARCHAR(50) = NULL,
    @EntityType NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 100
        Id, EntityType, EntityId, System, EventType,
        ExternalRef, RequestBody, StatusId, CreatedOn
    FROM dbo.WN_IntegrationLog WITH (NOLOCK)
    WHERE StatusId IN (1, 3)
      AND (@System     IS NULL OR System     = @System)
      AND (@EntityType IS NULL OR EntityType = @EntityType)
    ORDER BY CreatedOn ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_IntegrationLog_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 18: INTEGRATION LOG
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_IntegrationLog_Insert]
    @EntityType   NVARCHAR(50),
    @EntityId     INT,
    @System       NVARCHAR(50),
    @EventType    NVARCHAR(50),
    @ExternalRef  NVARCHAR(200) = NULL,
    @RequestBody  NVARCHAR(MAX) = NULL,
    @ResponseBody NVARCHAR(MAX) = NULL,
    @PaidAmount   DECIMAL(18,4) = NULL,
    @StatusId     TINYINT       = 1
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_IntegrationLog
        (EntityType, EntityId, System, EventType,
         ExternalRef, RequestBody, ResponseBody,
         PaidAmount, StatusId)
    VALUES
        (@EntityType, @EntityId, @System, @EventType,
         @ExternalRef, @RequestBody, @ResponseBody,
         @PaidAmount, @StatusId);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_IntegrationLog_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_IntegrationLog_UpdateStatus]
    @Id            INT,
    @StatusId      TINYINT,
    @ResponseBody  NVARCHAR(MAX) = NULL,
    @PaidAmount    DECIMAL(18,4) = NULL,
    @ProcessedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_IntegrationLog SET
        StatusId      = @StatusId,
        ResponseBody  = ISNULL(@ResponseBody, ResponseBody),
        PaidAmount    = ISNULL(@PaidAmount,   PaidAmount),
        ProcessedById = @ProcessedById,
        ProcessedOn   = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoices_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 12: INVOICES
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Invoices_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        i.Id, i.PublicId, i.InvoiceNumber,
        i.UserId, u.Name AS UserName, u.Email AS UserEmail,
        i.BookingId, i.MembershipId,
        i.IssuedOn, i.DueOn,
        i.SubTotal, i.DiscountTotal, i.TaxTotal,
        i.GrandTotal, i.PaidTotal, i.BalanceDue,
        i.CurrencyCode, i.StatusId, i.Notes, i.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    JOIN dbo.WN_Users    u WITH (NOLOCK) ON u.Id = i.UserId
    WHERE (@Search IS NULL OR @Search = ''
           OR u.Name          LIKE '%' + @Search + '%'
           OR u.Email         LIKE '%' + @Search + '%'
           OR i.InvoiceNumber LIKE '%' + @Search + '%')
    ORDER BY i.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoices_GetSummary]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Invoices_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        i.Id, i.PublicId, i.InvoiceNumber,
        i.UserId, u.Name AS UserName, u.Email AS UserEmail,
        i.BookingId, i.MembershipId,
        i.IssuedOn, i.DueOn,
        i.SubTotal, i.DiscountTotal, i.TaxTotal,
        i.GrandTotal, i.PaidTotal, i.BalanceDue,
        i.CurrencyCode, i.StatusId, i.Notes, i.CreatedOn,
        (SELECT il.Id, ct.Code AS ChargeTypeCode, ct.Label AS ChargeTypeLabel,
                il.Description, il.Quantity, il.UnitPrice,
                il.DiscountAmount, il.TaxRate, il.TaxAmount, il.LineTotal
         FROM dbo.WN_InvoiceLines il
         JOIN dbo.WN_ChargeTypes  ct ON ct.Id = il.ChargeTypeId
         WHERE il.InvoiceId = i.Id
         ORDER BY il.SortOrder
         FOR JSON PATH) AS InvoiceLines
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    JOIN dbo.WN_Users    u WITH (NOLOCK) ON u.Id = i.UserId
    WHERE i.Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Locations_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Locations SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_GetAll]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Locations_GetAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id, l.PublicId, l.Name, l.Address,
        l.CityId, ci.Name AS CityName,
        l.BranchId, br.[Description] AS BranchName,
        br.CompanyId, co.CompanyName AS CompanyName,
        l.OpeningTime, l.ClosingTime, l.IsActive
    FROM dbo.WN_Locations l  WITH (NOLOCK)
    JOIN dbo.Branches     br WITH (NOLOCK) ON br.Id = l.BranchId
    JOIN dbo.Company      co WITH (NOLOCK) ON co.Id = br.CompanyId
    JOIN dbo.WN_Cities    ci WITH (NOLOCK) ON ci.Id = l.CityId
    WHERE l.IsActive = 1
    ORDER BY l.Name ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 3: LOCATIONS & FLOORS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Locations_GetList]
    @Page      INT           = 1,
    @Limit     INT           = 50,
    @Search    NVARCHAR(255) = NULL,
    @BranchId  INT           = NULL,
    @CompanyId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        l.Id, l.PublicId, l.Name, l.Address,
        l.CityId, ci.Name AS CityName,
        l.BranchId, br.[Description] AS BranchName,
        br.CompanyId, co.CompanyName AS CompanyName,
        l.OpeningTime, l.ClosingTime,
        l.Latitude, l.Longitude, l.IsActive,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Locations l  WITH (NOLOCK)
    JOIN dbo.Branches     br WITH (NOLOCK) ON br.Id = l.BranchId
    JOIN dbo.Company      co WITH (NOLOCK) ON co.Id = br.CompanyId
    JOIN dbo.WN_Cities    ci WITH (NOLOCK) ON ci.Id = l.CityId
    WHERE l.IsActive = 1
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
      AND (@CompanyId IS NULL OR br.CompanyId = @CompanyId)
      AND (@Search    IS NULL OR @Search = ''
           OR l.Name    LIKE '%' + @Search + '%'
           OR l.Address LIKE '%' + @Search + '%')
    ORDER BY l.Name ASC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Locations_Insert]
    @BranchId    INT,
    @Name        NVARCHAR(200),
    @Address     NVARCHAR(500) = NULL,
    @CityId      INT,
    @OpeningTime TIME(0)       = '08:00:00',
    @ClosingTime TIME(0)       = '20:00:00',
    @Latitude    DECIMAL(10,7) = NULL,
    @Longitude   DECIMAL(10,7) = NULL,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Locations
        (BranchId, Name, Address, CityId, OpeningTime, ClosingTime,
         Latitude, Longitude, IsActive, CreatedById)
    VALUES
        (@BranchId, @Name, @Address, @CityId, @OpeningTime, @ClosingTime,
         @Latitude, @Longitude, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_Locations WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Locations_Update]
    @Id          INT,
    @Name        NVARCHAR(200) = NULL,
    @Address     NVARCHAR(500) = NULL,
    @CityId      INT           = NULL,
    @OpeningTime TIME(0)       = NULL,
    @ClosingTime TIME(0)       = NULL,
    @Latitude    DECIMAL(10,7) = NULL,
    @Longitude   DECIMAL(10,7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Locations SET
        Name        = ISNULL(@Name,        Name),
        Address     = ISNULL(@Address,     Address),
        CityId      = ISNULL(@CityId,      CityId),
        OpeningTime = ISNULL(@OpeningTime, OpeningTime),
        ClosingTime = ISNULL(@ClosingTime, ClosingTime),
        Latitude    = ISNULL(@Latitude,    Latitude),
        Longitude   = ISNULL(@Longitude,   Longitude)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_MembershipPlans_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlans SET IsActive = 0, UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_GetAll]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 14: MEMBERSHIP PLANS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_MembershipPlans_GetAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        mp.Id, mp.PublicId, mp.Name, mp.Description,
        mp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        mp.Price, mp.IncludesHours, mp.CurrencyCode, mp.IsActive, mp.CreatedOn
    FROM dbo.WN_MembershipPlans mp WITH (NOLOCK)
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = mp.BillingPeriodId
    WHERE mp.IsActive = 1
    ORDER BY mp.Price ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_MembershipPlans_GetList]
    @Page  INT = 1,
    @Limit INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        mp.Id, mp.PublicId, mp.Name, mp.Description,
        mp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        mp.Price, mp.IncludesHours, mp.CurrencyCode, mp.IsActive, mp.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_MembershipPlans mp WITH (NOLOCK)
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = mp.BillingPeriodId
    WHERE mp.IsActive = 1
    ORDER BY mp.Price ASC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_GetSummary]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_MembershipPlans_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        mp.Id, mp.PublicId, mp.Name, mp.Description,
        mp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        mp.Price, mp.IncludesHours, mp.CurrencyCode, mp.IsActive, mp.CreatedOn,
        (SELECT f.Id, f.PublicId, f.FeatureName, f.FeatureValue, f.SortOrder
         FROM dbo.WN_MembershipPlanFeatures f
         WHERE f.PlanId = mp.Id AND f.IsActive = 1
         ORDER BY f.SortOrder
         FOR JSON PATH) AS Features
    FROM dbo.WN_MembershipPlans mp WITH (NOLOCK)
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = mp.BillingPeriodId
    WHERE mp.Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_MembershipPlans_Insert]
    @Name            NVARCHAR(120),
    @Description     NVARCHAR(MAX)  = NULL,
    @BillingPeriodId TINYINT,
    @Price           DECIMAL(18,4),
    @IncludesHours   INT            = NULL,
    @CurrencyCode    NVARCHAR(10)   = 'PKR',
    @CreatedById     INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_MembershipPlans
        (Name, Description, BillingPeriodId, Price,
         IncludesHours, CurrencyCode, IsActive, CreatedById)
    VALUES
        (@Name, @Description, @BillingPeriodId, @Price,
         @IncludesHours, @CurrencyCode, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_MembershipPlans WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_MembershipPlans_Update]
    @Id              INT,
    @Name            NVARCHAR(120) = NULL,
    @Description     NVARCHAR(MAX) = NULL,
    @BillingPeriodId TINYINT       = NULL,
    @Price           DECIMAL(18,4) = NULL,
    @IncludesHours   INT           = NULL,
    @CurrencyCode    NVARCHAR(10)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlans SET
        Name            = ISNULL(@Name,            Name),
        Description     = ISNULL(@Description,     Description),
        BillingPeriodId = ISNULL(@BillingPeriodId, BillingPeriodId),
        Price           = ISNULL(@Price,           Price),
        IncludesHours   = ISNULL(@IncludesHours,   IncludesHours),
        CurrencyCode    = ISNULL(@CurrencyCode,    CurrencyCode),
        UpdatedOn       = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Memberships_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StatusId')
    BEGIN
        SET @sql = N'UPDATE dbo.WN_Memberships SET StatusId = 3, UpdatedOn = SYSUTCDATETIME() WHERE Id = @Id;';
        EXEC sp_executesql @sql, N'@Id INT', @Id;
    END
    ELSE
    BEGIN
        UPDATE dbo.WN_MemberShips_Temp SET Status = 'Cancelled', IsActive = 0 WHERE Id = @Id;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_GetByPlanId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Memberships_GetByPlanId]
    @PlanId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
        SET @sql = N'
        SELECT m.Id, m.PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Price AS PlanPrice,
            m.StartOn, m.EndOn, m.StatusId, m.AutoRenew, m.CreatedOn
        FROM dbo.WN_Memberships m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.PlanId = @PlanId
        ORDER BY m.CreatedOn DESC';
    ELSE
        SET @sql = N'
        SELECT m.Id, NULL AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Price AS PlanPrice,
            m.StartDate AS StartOn, m.EndDate AS EndOn,
            CASE m.Status WHEN ''Active'' THEN 1 WHEN ''Cancelled'' THEN 3 ELSE 2 END AS StatusId,
            CAST(0 AS BIT) AS AutoRenew, m.CreatedAt AS CreatedOn
        FROM dbo.WN_MemberShips_Temp m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.PlanId = @PlanId AND m.IsActive = 1
        ORDER BY m.CreatedAt DESC';

    EXEC sp_executesql @sql, N'@PlanId INT', @PlanId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 16: MEMBERSHIPS
-- -- NOTE: WN_Memberships is the new table created by Schema_Part0_Migration.sql
-- -- Old table was WN_MemberShips_Temp with columns:
-- -- Id, UserId(INT), PlanId, StartDate, EndDate, Status(NVARCHAR), IsActive, CreatedAt
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Memberships_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
        SET @sql = N'
        SELECT m.Id, m.PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName,
            bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
            mp.Price AS PlanPrice,
            m.StartOn, m.EndOn, m.StatusId,
            m.AutoRenew, m.Notes, m.CreatedOn,
            COUNT(*) OVER () AS TotalCount
        FROM dbo.WN_Memberships m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = mp.BillingPeriodId
        WHERE (@Search IS NULL OR @Search = ''''
               OR u.Name  LIKE ''%''+@Search+''%''
               OR u.Email LIKE ''%''+@Search+''%''
               OR mp.Name LIKE ''%''+@Search+''%'')
        ORDER BY m.CreatedOn DESC
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY';
    ELSE
        SET @sql = N'
        SELECT m.Id, NULL AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName,
            NULL AS BillingPeriodCode, NULL AS BillingPeriodLabel,
            mp.Price AS PlanPrice,
            m.StartDate AS StartOn, m.EndDate AS EndOn,
            CASE m.Status WHEN ''Active'' THEN 1 WHEN ''Cancelled'' THEN 3 ELSE 2 END AS StatusId,
            CAST(0 AS BIT) AS AutoRenew, NULL AS Notes, m.CreatedAt AS CreatedOn,
            COUNT(*) OVER () AS TotalCount
        FROM dbo.WN_MemberShips_Temp m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.IsActive = 1
          AND (@Search IS NULL OR @Search = ''''
               OR u.Name  LIKE ''%''+@Search+''%''
               OR u.Email LIKE ''%''+@Search+''%''
               OR mp.Name LIKE ''%''+@Search+''%'')
        ORDER BY m.CreatedAt DESC
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY';

    EXEC sp_executesql @sql,
        N'@Search NVARCHAR(255), @Offset INT, @Limit INT',
        @Search, @Offset, @Limit;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_GetSummary]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Memberships_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
        SET @sql = N'
        SELECT m.Id, m.PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Description AS PlanDescription,
            bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
            mp.Price AS PlanPrice, mp.IncludesHours,
            m.StartOn, m.EndOn, m.StatusId, m.AutoRenew, m.Notes, m.CreatedOn,
            (SELECT f.FeatureName, f.FeatureValue, f.SortOrder
             FROM dbo.WN_MembershipPlanFeatures f
             WHERE f.PlanId = mp.Id AND f.IsActive = 1
             ORDER BY f.SortOrder FOR JSON PATH) AS PlanFeatures
        FROM dbo.WN_Memberships m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = mp.BillingPeriodId
        WHERE m.Id = @Id';
    ELSE
        SET @sql = N'
        SELECT m.Id, NULL AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Description AS PlanDescription,
            NULL AS BillingPeriodCode, NULL AS BillingPeriodLabel,
            mp.Price AS PlanPrice, mp.IncludesHours,
            m.StartDate AS StartOn, m.EndDate AS EndOn,
            CASE m.Status WHEN ''Active'' THEN 1 WHEN ''Cancelled'' THEN 3 ELSE 2 END AS StatusId,
            CAST(0 AS BIT) AS AutoRenew, NULL AS Notes, m.CreatedAt AS CreatedOn,
            NULL AS PlanFeatures
        FROM dbo.WN_MemberShips_Temp m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.Id = @Id';

    EXEC sp_executesql @sql, N'@Id INT', @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Memberships_Insert]
    @UserId      INT,
    @PlanId      INT,
    @StartOn     DATETIME2,
    @EndOn       DATETIME2     = NULL,
    @AutoRenew   BIT           = 0,
    @Notes       NVARCHAR(500) = NULL,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
    BEGIN
        SET @sql = N'
        INSERT INTO dbo.WN_Memberships
            (UserId, PlanId, StartOn, EndOn, StatusId, AutoRenew, Notes, CreatedById)
        VALUES
            (@UserId, @PlanId, @StartOn, @EndOn, 1, @AutoRenew, @Notes, @CreatedById);
        SELECT Id, PublicId FROM dbo.WN_Memberships WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT, @PlanId INT, @StartOn DATETIME2, @EndOn DATETIME2, @AutoRenew BIT, @Notes NVARCHAR(500), @CreatedById INT',
            @UserId, @PlanId, @StartOn, @EndOn, @AutoRenew, @Notes, @CreatedById;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_MemberShips_Temp
            (UserId, PlanId, StartDate, EndDate, Status, IsActive)
        VALUES
            (@UserId, @PlanId, @StartOn, @EndOn, 'Active', 1);
        SELECT SCOPE_IDENTITY() AS Id, NULL AS PublicId;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Memberships_UpdateStatus]
    @Id          INT,
    @StatusId    TINYINT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StatusId')
    BEGIN
        SET @sql = N'UPDATE dbo.WN_Memberships SET StatusId = @StatusId, UpdatedOn = SYSUTCDATETIME() WHERE Id = @Id;';
        EXEC sp_executesql @sql, N'@StatusId TINYINT, @Id INT', @StatusId, @Id;
    END
    ELSE
    BEGIN
        UPDATE dbo.WN_MemberShips_Temp SET
            Status   = CASE @StatusId WHEN 1 THEN 'Active' WHEN 3 THEN 'Cancelled' ELSE 'Expired' END,
            IsActive = CASE WHEN @StatusId = 1 THEN 1 ELSE 0 END
        WHERE Id = @Id;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Payments_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET StatusId = 5, UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GenerateVoucher]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_GenerateVoucher]
    @UserId      INT,
    @BookingId   INT           = NULL,
    @Amount      DECIMAL(18,4),
    @ExpiresOn   DATETIME2,
    @CreatedById INT           = NULL,
    @UserEmail   NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);
    DECLARE @SeqNum INT;
    DECLARE @VoucherRef NVARCHAR(50);

    UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
    SET LastNumber = LastNumber + 1 WHERE CounterDate = @Today;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
        SELECT @Today, 1 WHERE NOT EXISTS (
            SELECT 1 FROM dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
            WHERE CounterDate = @Today
        );

    SELECT @SeqNum = LastNumber FROM dbo.WN_ChallanCounter WITH (NOLOCK)
    WHERE CounterDate = @Today;

    SET @VoucherRef = 'VCH-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                    + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);

    DECLARE @PaymentId INT;
    DECLARE @PaymentGuid UNIQUEIDENTIFIER;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, StatusId, ExpiresOn, CreatedById)
        VALUES (@UserId,@BookingId,5,@Amt,''PKR'',@Ref,1,@Exp,@CBy);
        SET @OutId = SCOPE_IDENTITY();
        SELECT @OutGuid = PublicId FROM dbo.WN_Payments WHERE Id = @OutId;';
        
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@Amt DECIMAL(18,4),@Ref NVARCHAR(50),@Exp DATETIME2,@CBy INT, @OutId INT OUTPUT, @OutGuid UNIQUEIDENTIFIER OUTPUT',
            @UserId,@BookingId,@Amount,@VoucherRef,@ExpiresOn,@CreatedById, @PaymentId OUTPUT, @PaymentGuid OUTPUT;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, StatusId, ExpiresOn, CreatedAt)
        VALUES (@UG,@BG,5,@Amt,''PKR'',@Ref,1,@Exp,SYSUTCDATETIME());
        SET @OutId = SCOPE_IDENTITY();
        SELECT @OutGuid = PublicId FROM dbo.WN_Payments WHERE Id = @OutId;';
        
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@Amt DECIMAL(18,4),@Ref NVARCHAR(50),@Exp DATETIME2, @OutId INT OUTPUT, @OutGuid UNIQUEIDENTIFIER OUTPUT',
            @UG,@BG,@Amount,@VoucherRef,@ExpiresOn, @PaymentId OUTPUT, @PaymentGuid OUTPUT;
    END

    -- Return a single SELECT containing all voucher information
    SELECT 
        @PaymentId AS Id, 
        @PaymentGuid AS PublicId, 
        @VoucherRef AS VoucherRef,
        @VoucherRef AS VoucherNumber,
        @ExpiresOn AS ExpiryDate,
        @Amount AS Amount,
        1 AS IsSuccessful,
        N'Voucher generated successfully' AS Message;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetByMembershipId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_GetByMembershipId]
    @MembershipId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        p.Id, p.PublicId, p.UserId,
        p.PaymentMethodId, pm.Code AS PaymentMethodCode,
        p.Amount, p.CurrencyCode,
        p.TransactionRef, p.StatusId, p.PaidOn, p.CreatedOn
    FROM dbo.WN_Payments       p  WITH (NOLOCK)
    JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
    WHERE p.MembershipId = @MembershipId
    ORDER BY p.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.WN_Payments')
          AND name = 'UserId' AND system_type_id = 56
    )
        SET @sql = N'
        SELECT p.Id, p.PublicId,
            p.InvoiceId, p.BookingId, p.MembershipId,
            p.UserId, u.Name AS UserName, u.Email AS UserEmail,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.GatewayRef,
            p.StatusId, p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
            COUNT(*) OVER () AS TotalCount
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE (@Search IS NULL OR @Search = ''''
            OR u.Name LIKE ''%''+@Search+''%''
            OR u.Email LIKE ''%''+@Search+''%''
            OR p.TransactionRef LIKE ''%''+@Search+''%'')
        ORDER BY p.CreatedOn DESC
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY';
    ELSE
        SET @sql = N'
        SELECT p.Id, p.PublicId,
            NULL AS InvoiceId, NULL AS BookingId, NULL AS MembershipId,
            u.Id AS UserId, u.Name AS UserName, u.Email AS UserEmail,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.GatewayRef,
            p.StatusId, p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
            COUNT(*) OVER () AS TotalCount
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.IdGUID = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE (@Search IS NULL OR @Search = ''''
            OR u.Name LIKE ''%''+@Search+''%''
            OR u.Email LIKE ''%''+@Search+''%''
            OR p.TransactionRef LIKE ''%''+@Search+''%'')
        ORDER BY p.CreatedOn DESC
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY';

    EXEC sp_executesql @sql,
        N'@Search NVARCHAR(255), @Offset INT, @Limit INT',
        @Search, @Offset, @Limit;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetMyList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_GetMyList]
    @UserEmail NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.WN_Payments')
          AND name = 'UserId' AND system_type_id = 56
    )
        SET @sql = N'
        SELECT p.Id, p.PublicId, p.BookingId, p.MembershipId,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.StatusId,
            p.ExpiresOn, p.PaidOn, p.CreatedOn,
            b.StartOn, b.EndOn,
            s.Name AS SpaceName, s.Code AS SpaceCode,
            ch.ChallanNumber, ch.ValidUntil AS ChallanValidUntil
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = p.BookingId
        LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        OUTER APPLY (
            SELECT TOP 1 ChallanNumber, ValidUntil
            FROM dbo.WN_Challans WHERE BookingId = p.BookingId
            ORDER BY CreatedOn DESC
        ) ch
        WHERE u.Email = @UserEmail AND p.StatusId <> 5
        ORDER BY p.CreatedOn DESC';
    ELSE
        SET @sql = N'
        SELECT p.Id, p.PublicId, NULL AS BookingId, NULL AS MembershipId,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.StatusId,
            p.ExpiresOn, p.PaidOn, p.CreatedOn,
            NULL AS StartOn, NULL AS EndOn,
            NULL AS SpaceName, NULL AS SpaceCode,
            NULL AS ChallanNumber, NULL AS ChallanValidUntil
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.IdGUID = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE u.Email = @UserEmail AND p.StatusId <> 5
        ORDER BY p.CreatedOn DESC';

    EXEC sp_executesql @sql, N'@UserEmail NVARCHAR(256)', @UserEmail;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetSummary]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
        SET @sql = N'
        SELECT p.Id, p.PublicId,
            p.InvoiceId, p.BookingId, p.MembershipId,
            p.UserId, u.Name AS UserName, u.Email AS UserEmail,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.GatewayRef,
            p.StatusId, p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
            (SELECT pl.Id, ct.Code AS ChargeTypeCode, ct.Label AS ChargeTypeLabel,
                    pl.Amount, pl.AccountId, pl.Notes
             FROM dbo.WN_PaymentLines pl
             JOIN dbo.WN_ChargeTypes ct ON ct.Id = pl.ChargeTypeId
             WHERE pl.PaymentId = p.Id
             FOR JSON PATH) AS PaymentLines
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE p.Id = @Id';
    ELSE
        SET @sql = N'
        SELECT p.Id, p.PublicId,
            NULL AS InvoiceId, NULL AS BookingId, NULL AS MembershipId,
            u.Id AS UserId, u.Name AS UserName, u.Email AS UserEmail,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.GatewayRef,
            p.StatusId, p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
            NULL AS PaymentLines
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.IdGUID = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE p.Id = @Id';
    EXEC sp_executesql @sql, N'@Id INT', @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_InitiatePayFast]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_InitiatePayFast]
    @UserId      INT,
    @BookingId   INT           = NULL,
    @Amount      DECIMAL(18,4),
    @GatewayRef  NVARCHAR(200) = NULL,
    @CreatedById INT           = NULL,
    @UserEmail   NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode, GatewayRef, StatusId, CreatedById)
        VALUES (@UserId,@BookingId,4,@Amt,''PKR'',@GRef,1,@CBy);
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@Amt DECIMAL(18,4),@GRef NVARCHAR(200),@CBy INT',
            @UserId,@BookingId,@Amount,@GatewayRef,@CreatedById;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode, GatewayRef, StatusId, CreatedAt)
        VALUES (@UG,@BG,4,@Amt,''PKR'',@GRef,1,SYSUTCDATETIME());
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@Amt DECIMAL(18,4),@GRef NVARCHAR(200)',
            @UG,@BG,@Amount,@GatewayRef;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Payments_Insert]
    @UserId          INT,
    @BookingId       INT           = NULL,
    @MembershipId    INT           = NULL,
    @InvoiceId       INT           = NULL,
    @PaymentMethodId TINYINT,
    @Amount          DECIMAL(18,4),
    @CurrencyCode    NVARCHAR(10)  = 'PKR',
    @TransactionRef  NVARCHAR(200) = NULL,
    @ExpiresOn       DATETIME2     = NULL,
    @Notes           NVARCHAR(500) = NULL,
    @CreatedById     INT           = NULL,
    @UserEmail       NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, MembershipId, InvoiceId, PaymentMethodId,
             Amount, CurrencyCode, TransactionRef, StatusId, ExpiresOn, Notes, CreatedById)
        VALUES (@UserId,@BookingId,@MembershipId,@InvoiceId,@PM,@Amt,@Cur,@Ref,1,@Exp,@Notes,@CBy);
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@MembershipId INT,@InvoiceId INT,@PM TINYINT,@Amt DECIMAL(18,4),@Cur NVARCHAR(10),@Ref NVARCHAR(200),@Exp DATETIME2,@Notes NVARCHAR(500),@CBy INT',
            @UserId,@BookingId,@MembershipId,@InvoiceId,@PaymentMethodId,@Amount,@CurrencyCode,@TransactionRef,@ExpiresOn,@Notes,@CreatedById;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, StatusId, ExpiresOn, CreatedAt)
        VALUES (@UG,@BG,@PM,@Amt,@Cur,@Ref,1,@Exp,SYSUTCDATETIME());
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@PM TINYINT,@Amt DECIMAL(18,4),@Cur NVARCHAR(10),@Ref NVARCHAR(200),@Exp DATETIME2',
            @UG,@BG,@PaymentMethodId,@Amount,@CurrencyCode,@TransactionRef,@ExpiresOn;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_InsertCard]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Payments_InsertCard]
    @UserId         INT,
    @BookingId      INT           = NULL,
    @MembershipId   INT           = NULL,
    @Amount         DECIMAL(18,4),
    @TransactionRef NVARCHAR(200) = NULL,
    @GatewayRef     NVARCHAR(200) = NULL,
    @CreatedById    INT           = NULL,
    @UserEmail      NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, MembershipId, PaymentMethodId,
             Amount, CurrencyCode, TransactionRef, GatewayRef, StatusId, PaidOn, CreatedById)
        VALUES (@UserId,@BookingId,@MembershipId,2,@Amt,''PKR'',@Ref,@GRef,2,SYSUTCDATETIME(),@CBy);
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@MembershipId INT,@Amt DECIMAL(18,4),@Ref NVARCHAR(200),@GRef NVARCHAR(200),@CBy INT',
            @UserId,@BookingId,@MembershipId,@Amount,@TransactionRef,@GatewayRef,@CreatedById;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, GatewayRef, StatusId, PaidOn, CreatedAt)
        VALUES (@UG,@BG,2,@Amt,''PKR'',@Ref,@GRef,2,SYSUTCDATETIME(),SYSUTCDATETIME());
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@Amt DECIMAL(18,4),@Ref NVARCHAR(200),@GRef NVARCHAR(200)',
            @UG,@BG,@Amount,@TransactionRef,@GatewayRef;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO



CREATE   PROCEDURE [dbo].[WN_Payments_UpdateStatus]
    @Id          INT,
    @StatusId    TINYINT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET
        StatusId  = @StatusId,
        PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
        UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatusByGuid]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_Payments_UpdateStatusByGuid]
    @IdGUID UNIQUEIDENTIFIER,
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
            UPDATE dbo.WN_Payments
            SET    PaymentStatus = @Status
            WHERE  IdGUID = @IdGUID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatusByPublicId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Payments_UpdateStatusByPublicId]
    @PublicId UNIQUEIDENTIFIER,
    @StatusId TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET
        StatusId  = @StatusId,
        PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
        UpdatedOn = SYSUTCDATETIME()
    WHERE PublicId = @PublicId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatusByRef]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Payments_UpdateStatusByRef]
    @TransactionRef NVARCHAR(200),
    @StatusId       TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET
        StatusId  = @StatusId,
        PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
        UpdatedOn = SYSUTCDATETIME()
    WHERE TransactionRef = @TransactionRef;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_PlanFeatures_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlanFeatures SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_GetByPlan]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 15: PLAN FEATURES
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_PlanFeatures_GetByPlan]
    @PlanId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, PublicId, PlanId, FeatureName, FeatureValue, SortOrder, IsActive
    FROM dbo.WN_MembershipPlanFeatures WITH (NOLOCK)
    WHERE PlanId = @PlanId AND IsActive = 1
    ORDER BY SortOrder;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_PlanFeatures_Insert]
    @PlanId       INT,
    @FeatureName  NVARCHAR(120),
    @FeatureValue NVARCHAR(120) = NULL,
    @SortOrder    SMALLINT      = 0
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_MembershipPlanFeatures
        (PlanId, FeatureName, FeatureValue, SortOrder, IsActive)
    VALUES
        (@PlanId, @FeatureName, @FeatureValue, @SortOrder, 1);
    SELECT Id, PublicId FROM dbo.WN_MembershipPlanFeatures WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_PlanFeatures_Update]
    @Id           INT,
    @FeatureName  NVARCHAR(120) = NULL,
    @FeatureValue NVARCHAR(120) = NULL,
    @SortOrder    SMALLINT      = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlanFeatures SET
        FeatureName  = ISNULL(@FeatureName,  FeatureName),
        FeatureValue = ISNULL(@FeatureValue, FeatureValue),
        SortOrder    = ISNULL(@SortOrder,    SortOrder)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PricingPlans_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ── 11. WN_PricingPlans_GetList ───────────────────────────
CREATE   PROCEDURE [dbo].[WN_PricingPlans_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.IdGUID        AS IdGuid,
           p.Id            AS Id,
           p.Name          AS Name,
           ISNULL(p.Price, 0) AS Price,
           p.BillingCycle  AS BillingCycle,
           p.IncludesHours AS IncludesHours,
           p.IsActive      AS IsActive,
           p.Description   AS Description,
           f.FeatureName   AS FeatureName
    FROM dbo.WN_PricingPlans p WITH (NOLOCK)
    LEFT JOIN dbo.WN_PlanFeatures f WITH (NOLOCK) ON p.Id = f.PlanId AND f.Status != 0
    WHERE p.IsActive = 1;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_GetByHash]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_RefreshTokens_GetByHash]
    @TokenHash NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rt.Id, rt.UserId, rt.TokenHash, rt.CreatedOn, rt.ExpiresOn,
           rt.RevokedOn, rt.ReplacedByTokenId,
           u.Email, u.RoleId, u.IsActive
    FROM dbo.WN_RefreshTokens rt WITH (NOLOCK)
    JOIN dbo.WN_Users          u  WITH (NOLOCK) ON u.Id = rt.UserId
    WHERE rt.TokenHash = @TokenHash;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 2: AUTH TOKENS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_RefreshTokens_Insert]
    @UserId    INT,
    @TokenHash NVARCHAR(450),
    @ExpiresOn DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_RefreshTokens (UserId, TokenHash, ExpiresOn)
    VALUES (@UserId, @TokenHash, @ExpiresOn);
    SELECT Id FROM dbo.WN_RefreshTokens WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_Revoke]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_RefreshTokens_Revoke]
    @Id                  UNIQUEIDENTIFIER,
    @ReplacedByTokenId   UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_RefreshTokens SET
        RevokedOn          = SYSUTCDATETIME(),
        ReplacedByTokenId  = @ReplacedByTokenId
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_RevokeAllForUser]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_RefreshTokens_RevokeAllForUser]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_RefreshTokens SET
        RevokedOn = SYSUTCDATETIME()
    WHERE UserId    = @UserId
      AND RevokedOn IS NULL;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Refunds_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 19: REFUNDS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Refunds_Insert]
    @PaymentId   INT,
    @Amount      DECIMAL(18,4),
    @Reason      NVARCHAR(500) = NULL,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PaidAmount      DECIMAL(18,4);
    DECLARE @AlreadyRefunded DECIMAL(18,4);

    SELECT @PaidAmount = Amount FROM dbo.WN_Payments WHERE Id = @PaymentId;

    SELECT @AlreadyRefunded = ISNULL(SUM(Amount), 0)
    FROM dbo.WN_Refunds
    WHERE PaymentId = @PaymentId AND StatusId IN (1, 2);

    IF (@AlreadyRefunded + @Amount) > @PaidAmount
    BEGIN
        RAISERROR('Refund amount exceeds available payment amount.', 16, 1);
        RETURN;
    END

    INSERT INTO dbo.WN_Refunds
        (PaymentId, Amount, Reason, StatusId, CreatedById)
    VALUES
        (@PaymentId, @Amount, @Reason, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_Refunds WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Refunds_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Refunds_UpdateStatus]
    @Id            INT,
    @StatusId      TINYINT,
    @ProcessedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Refunds SET
        StatusId      = @StatusId,
        ProcessedOn   = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE ProcessedOn END,
        ProcessedById = ISNULL(@ProcessedById, ProcessedById)
    WHERE Id = @Id;

    IF @StatusId = 2
        UPDATE dbo.WN_Payments SET
            StatusId  = 4,
            UpdatedOn = SYSUTCDATETIME()
        WHERE Id = (SELECT PaymentId FROM dbo.WN_Refunds WHERE Id = @Id);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SecurityDeposits_GetByBooking]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 13: SECURITY DEPOSITS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_SecurityDeposits_GetByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        sd.Id, sd.PublicId, sd.BookingId, sd.UserId,
        u.Name AS UserName, u.Email AS UserEmail,
        sd.Amount, sd.StatusId,
        sd.HeldOn, sd.ReleasedOn, sd.ForfeitedOn,
        sd.ForfeitReason, sd.Notes
    FROM dbo.WN_SecurityDeposits sd WITH (NOLOCK)
    JOIN dbo.WN_Users            u  WITH (NOLOCK) ON u.Id = sd.UserId
    WHERE sd.BookingId = @BookingId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SecurityDeposits_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SecurityDeposits_Insert]
    @BookingId   INT,
    @UserId      INT,
    @Amount      DECIMAL(18,4),
    @AccountId   INT           = NULL,
    @Notes       NVARCHAR(500) = NULL,
    @UpdatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_SecurityDeposits
        (BookingId, UserId, Amount, AccountId, StatusId, Notes, UpdatedById)
    VALUES
        (@BookingId, @UserId, @Amount, @AccountId, 1, @Notes, @UpdatedById);
    SELECT Id, PublicId FROM dbo.WN_SecurityDeposits WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SecurityDeposits_UpdateStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SecurityDeposits_UpdateStatus]
    @Id            INT,
    @StatusId      TINYINT,
    @ForfeitReason NVARCHAR(500) = NULL,
    @UpdatedById   INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SecurityDeposits SET
        StatusId      = @StatusId,
        ReleasedOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE ReleasedOn  END,
        ForfeitedOn   = CASE WHEN @StatusId IN (3,4) THEN SYSUTCDATETIME() ELSE ForfeitedOn END,
        ForfeitReason = ISNULL(@ForfeitReason, ForfeitReason),
        UpdatedById   = @UpdatedById
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- 5. WN_SpaceConfig_Delete (soft)
-- ============================================================
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceConfig SET Status = 0, UpdatedOn = GETUTCDATE()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_DeleteSpaces]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- 8. WN_SpaceConfig_DeleteSpaces
--    Deletes spaces by GUID list; skips those with bookings.
--    @SpaceGuids: comma-separated GUIDs, or NULL = delete all for config
-- ============================================================
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_DeleteSpaces]
    @ConfigId   INT,
    @SpaceGuids NVARCHAR(MAX) = NULL   -- NULL = delete all
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MinCode      INT;
    DECLARE @TotalSpaces  INT;
    DECLARE @LocationGuid UNIQUEIDENTIFIER;
    DECLARE @LocationId   INT;

    SELECT @MinCode = MinCode, @TotalSpaces = TotalSpaces, @LocationId = LocationId
    FROM dbo.WN_SpaceConfig WHERE Id = @ConfigId AND Status = 1;

    SELECT @LocationGuid = IdGUID FROM dbo.WN_Locations WHERE Id = @LocationId;

    -- Build candidate set
    IF @SpaceGuids IS NULL
    BEGIN
        -- All spaces in range
        UPDATE dbo.WN_Spaces SET Status = 0
        WHERE LocationId = @LocationGuid
          AND TRY_CAST(Code AS INT) BETWEEN @MinCode AND (@MinCode + @TotalSpaces - 1)
          AND Status = 1
          AND NOT EXISTS (
              SELECT 1 FROM dbo.WN_Bookings b
              WHERE b.SpaceGuid = IdGUID AND b.BookingStatus IN (1,4)
          );
    END
    ELSE
    BEGIN
        -- Selected GUIDs only
        UPDATE dbo.WN_Spaces SET Status = 0
        WHERE CAST(IdGUID AS NVARCHAR(36)) IN (
            SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@SpaceGuids, ',')
        )
        AND Status = 1
        AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings b
            WHERE b.SpaceGuid = IdGUID AND b.BookingStatus IN (1,4)
        );
    END

    -- Return blocked spaces (have bookings)
    SELECT
        CAST(s.IdGUID AS NVARCHAR(36)) AS IdGuid,
        s.Code,
        s.Name
    FROM dbo.WN_Spaces s
    WHERE s.LocationId = @LocationGuid
      AND TRY_CAST(s.Code AS INT) BETWEEN @MinCode AND (@MinCode + @TotalSpaces - 1)
      AND s.Status = 1
      AND EXISTS (
          SELECT 1 FROM dbo.WN_Bookings b
          WHERE b.SpaceGuid = s.IdGUID AND b.BookingStatus IN (1,4)
      );
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GenerateSpaces]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Hotfix: WN_SpaceConfig_GenerateSpaces
-- Removes CompanyId / BranchId from INSERT (columns dropped in 3NF migration)
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_GenerateSpaces]
    @ConfigId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SpaceCategory    NVARCHAR(20);
    DECLARE @TotalSpaces      INT;
    DECLARE @MinCode          INT;
    DECLARE @FloorId          INT;
    DECLARE @PricePerHour     DECIMAL(18,2);
    DECLARE @PricePerDay      DECIMAL(18,2);
    DECLARE @PricePerMonth    DECIMAL(18,2);
    DECLARE @Amenities        NVARCHAR(MAX);
    DECLARE @RentAccountId    INT;
    DECLARE @DepositAccountId INT;
    DECLARE @LocationId       INT;
    DECLARE @SpaceTypeId      INT;
    DECLARE @LocationGuid     UNIQUEIDENTIFIER;
    DECLARE @SpaceTypeGuid    UNIQUEIDENTIFIER;

    SELECT
        @SpaceCategory    = SpaceCategory,
        @TotalSpaces      = TotalSpaces,
        @MinCode          = MinCode,
        @FloorId          = FloorId,
        @PricePerHour     = ISNULL(PricePerHour, 0),
        @PricePerDay      = ISNULL(PricePerDay, 0),
        @PricePerMonth    = ISNULL(PricePerMonth, 0),
        @Amenities        = Amenities,
        @RentAccountId    = RentAccountId,
        @DepositAccountId = SecurityAccountId,
        @LocationId       = LocationId,
        @SpaceTypeId      = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE Id = @ConfigId AND Status = 1;

    IF @SpaceCategory IS NULL
    BEGIN
        RAISERROR('Configuration not found or inactive.', 16, 1); RETURN;
    END

    SELECT @LocationGuid  = IdGUID FROM dbo.WN_Locations  WHERE Id = @LocationId;
    SELECT @SpaceTypeGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    IF @LocationGuid IS NULL OR @SpaceTypeGuid IS NULL
    BEGIN
        RAISERROR('Location or SpaceType not found.', 16, 1); RETURN;
    END

    DECLARE @NamePrefix NVARCHAR(50) = CASE
        WHEN @SpaceCategory LIKE '%Private%' OR @SpaceCategory LIKE '%Office%' THEN 'Office'
        WHEN @SpaceCategory LIKE '%Meeting%' OR @SpaceCategory LIKE '%Room%'   THEN 'Meeting Room'
        ELSE 'Shared Space'
    END;

    DECLARE @Created INT = 0;
    DECLARE @Skipped INT = 0;
    DECLARE @i       INT = 0;
    DECLARE @Code    NVARCHAR(20);
    DECLARE @Name    NVARCHAR(255);

    WHILE @i < @TotalSpaces
    BEGIN
        SET @Code = CAST(@MinCode + @i AS NVARCHAR(20));
        SET @Name = @NamePrefix + ' ' + @Code;

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE (LocationIdInt = @LocationId OR LocationId = @LocationGuid)
              AND (SpaceTypeIdInt = @SpaceTypeId OR SpaceTypeId = @SpaceTypeGuid)
              AND Code = @Code
              AND (IsActive = 0 OR Status = 0)
        )
        BEGIN
            UPDATE dbo.WN_Spaces 
            SET IsActive = 1, Status = 1, Name = @Name
            WHERE (LocationIdInt = @LocationId OR LocationId = @LocationGuid)
              AND (SpaceTypeIdInt = @SpaceTypeId OR SpaceTypeId = @SpaceTypeGuid)
              AND Code = @Code;

            SET @Created = @Created + 1;
        END
        ELSE IF NOT EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE (LocationIdInt = @LocationId OR LocationId = @LocationGuid)
              AND (SpaceTypeIdInt = @SpaceTypeId OR SpaceTypeId = @SpaceTypeGuid)
              AND Code = @Code
        )
        BEGIN
            INSERT INTO dbo.WN_Spaces
                (IdGUID, Name, Code, LocationId, LocationIdInt, SpaceTypeId, SpaceTypeIdInt, FloorId,
                 PricePerHour, PricePerDay, PricePerMonth,
                 Amenities, RentAccountId, SecurityDepositAccountId,
                 IsActive, Status, CreatedOn)
            VALUES
                (NEWID(), @Name, @Code, @LocationGuid, @LocationId, @SpaceTypeGuid, @SpaceTypeId, @FloorId,
                 @PricePerHour, @PricePerDay, @PricePerMonth,
                 @Amenities, @RentAccountId, @DepositAccountId,
                 1, 1, GETUTCDATE());

            SET @Created = @Created + 1;
        END
        ELSE
            SET @Skipped = @Skipped + 1;

        SET @i = @i + 1;
    END

    SELECT @Created AS Created, @Skipped AS Skipped, @TotalSpaces AS Configured;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetDepositByCategory]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpaceConfig_GetDepositByCategory]
    @CategoryCode NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        sp.SecurityDeposit,
        sp.SeatPrice,
        sp.SeatPrice * s.Capacity AS RoomPrice,
        s.Capacity,
        bp.Code AS BillingPeriodCode
    FROM dbo.WN_SpacePricing      sp WITH (NOLOCK)
    JOIN dbo.WN_Spaces            s  WITH (NOLOCK) ON s.Id  = sp.SpaceId
    JOIN dbo.WN_SpaceTypes        st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories   sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    JOIN dbo.WN_BillingPeriods    bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    WHERE sc.Code         = @CategoryCode
      AND bp.Code         = 'Monthly'
      AND sp.IsActive     = 1
      AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
    ORDER BY sp.EffectiveFrom DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpaceConfig_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        Id, SpaceCategory, TotalSpaces, CodePrefix, MinCode,
        DefaultCapacities, OpeningTime, ClosingTime,
        ISNULL(SecurityDeposit, 0) AS SecurityDeposit,
        UpdatedOn, UpdatedBy
    FROM dbo.WN_SpaceConfig
    ORDER BY Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetListV2]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.10  WN_SpaceConfig_GetListV2
-- SELECT + WHERE derive CompanyId/BranchId via
-- SpaceConfig → Location
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_GetListV2]
    @CompanyId  INT = NULL,
    @BranchId   INT = NULL,
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        sc.Id,
        sc.SpaceCategory,
        sc.TotalSpaces,
        sc.CodePrefix,
        sc.MinCode,
        sc.DefaultCapacities,
        sc.OpeningTime,
        sc.ClosingTime,
        ISNULL(sc.SecurityDeposit, 0) AS SecurityDeposit,
        sc.SecurityAccountId          AS DepositAccountId,
        sc.RentAccountId,
        sc.PricePerHour,
        sc.PricePerDay,
        sc.PricePerMonth,
        sc.Amenities,
        sc.FloorId,
        sc.LocationId,
        l.CompanyId    AS CompanyId,
        l.BranchId     AS BranchId,
        sc.SpaceTypeId,
        sc.Status,
        sc.UpdatedOn,
        sc.UpdatedBy,
        l.Name         AS LocationName,
        st.Description AS SpaceTypeName
    FROM dbo.WN_SpaceConfig sc
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.Id  = sc.LocationId
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = sc.SpaceTypeId
    WHERE sc.Status = 1
      AND (@CompanyId  IS NULL OR l.CompanyId   = @CompanyId)
      AND (@BranchId   IS NULL OR l.BranchId    = @BranchId)
      AND (@LocationId IS NULL OR sc.LocationId = @LocationId)
    ORDER BY sc.LocationId, sc.SpaceCategory;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetSpaceStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- 7. WN_SpaceConfig_GetSpaceStatus
--    Returns existing spaces for a config with booking check
-- ============================================================
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_GetSpaceStatus]
    @ConfigId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @MinCode       INT;
    DECLARE @TotalSpaces   INT;
    DECLARE @LocationId    INT;
    DECLARE @SpaceTypeId   INT;
    DECLARE @LocationGuid  UNIQUEIDENTIFIER;
    DECLARE @SpaceTypeGuid UNIQUEIDENTIFIER;

    SELECT 
        @MinCode     = MinCode, 
        @TotalSpaces = TotalSpaces, 
        @LocationId  = LocationId,
        @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig WHERE Id = @ConfigId AND Status = 1;

    SELECT @LocationGuid  = IdGUID FROM dbo.WN_Locations  WHERE Id = @LocationId;
    SELECT @SpaceTypeGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    SELECT
        s.Id,
        CAST(s.IdGUID AS NVARCHAR(36)) AS IdGuid,
        CAST(s.PublicId AS NVARCHAR(36)) AS PublicId,
        s.Code,
        s.Name,
        s.Status,
        s.IsActive,
        CASE WHEN EXISTS (
            SELECT 1 FROM dbo.WN_Bookings b
            WHERE (b.SpaceGuid = s.IdGUID OR b.SpaceId = s.Id)
              AND b.IsDeleted = 0 
              AND b.BookingStatusId IN (1, 2)
              AND b.EndOn > SYSUTCDATETIME()
        ) THEN 1 ELSE 0 END AS HasBookings
    FROM dbo.WN_Spaces s
    WHERE s.IsActive = 1
      AND (s.LocationIdInt = @LocationId OR s.LocationId = @LocationGuid)
      AND (s.SpaceTypeIdInt = @SpaceTypeId OR s.SpaceTypeId = @SpaceTypeGuid)
      AND TRY_CAST(s.Code AS INT) BETWEEN @MinCode AND (@MinCode + @TotalSpaces - 1)
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.11  WN_SpaceConfig_Insert
-- Removed @BranchId / @CompanyId parameters.
-- Removed BranchId / CompanyId from INSERT column list.
-- Duplicate-prefix check scoped by LocationId.
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_Insert]
    @SpaceCategory    NVARCHAR(20),
    @TotalSpaces      INT,
    @CodePrefix       NVARCHAR(5),
    @MinCode          INT,
    @OpeningTime      NVARCHAR(5)   = '08:00',
    @ClosingTime      NVARCHAR(5)   = '20:00',
    @SecurityDeposit  DECIMAL(10,2) = 0,
    @RentAccountId    INT           = NULL,
    @DepositAccountId INT           = NULL,
    @FloorId          INT           = NULL,
    @PricePerHour     DECIMAL(18,2) = 0,
    @PricePerDay      DECIMAL(18,2) = 0,
    @PricePerMonth    DECIMAL(18,2) = 0,
    @Amenities        NVARCHAR(MAX) = NULL,
    @LocationId       INT           = NULL,
    @SpaceTypeId      INT           = NULL,
    @CreatedBy        NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceConfig
        WHERE CodePrefix = @CodePrefix
          AND Status     = 1
          AND (@LocationId IS NULL OR LocationId = @LocationId)
    )
    BEGIN
        RAISERROR('Prefix %s is already used by another configuration at this location.', 16, 1, @CodePrefix);
        RETURN;
    END

    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceConfig
        WHERE LocationId  = @LocationId
          AND SpaceTypeId = @SpaceTypeId
          AND Status      = 1
    )
    BEGIN
        RAISERROR('A configuration for this Location and Space Type already exists.', 16, 1);
        RETURN;
    END

    INSERT INTO dbo.WN_SpaceConfig
        (SpaceCategory, TotalSpaces, CodePrefix, MinCode,
         OpeningTime, ClosingTime, SecurityDeposit,
         RentAccountId, SecurityAccountId, FloorId,
         PricePerHour, PricePerDay, PricePerMonth, Amenities,
         LocationId, SpaceTypeId,
         Status, UpdatedOn, UpdatedBy)
    VALUES
        (@SpaceCategory, @TotalSpaces, @CodePrefix, @MinCode,
         @OpeningTime, @ClosingTime, @SecurityDeposit,
         @RentAccountId, @DepositAccountId, @FloorId,
         @PricePerHour, @PricePerDay, @PricePerMonth, @Amenities,
         @LocationId, @SpaceTypeId,
         1, GETUTCDATE(), @CreatedBy);

    SELECT SCOPE_IDENTITY() AS NewId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.12  WN_SpaceConfig_Update
-- Duplicate-prefix check scoped by LocationId
-- instead of the removed CompanyId column.
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_SpaceConfig_Update]
    @Id               INT,
    @TotalSpaces      INT,
    @CodePrefix       NVARCHAR(5),
    @MinCode          INT,
    @OpeningTime      NVARCHAR(5)   = NULL,
    @ClosingTime      NVARCHAR(5)   = NULL,
    @SecurityDeposit  DECIMAL(10,2) = NULL,
    @RentAccountId    INT           = NULL,
    @DepositAccountId INT           = NULL,
    @FloorId          INT           = NULL,
    @PricePerHour     DECIMAL(18,2) = NULL,
    @PricePerDay      DECIMAL(18,2) = NULL,
    @PricePerMonth    DECIMAL(18,2) = NULL,
    @Amenities        NVARCHAR(MAX) = NULL,
    @UpdatedBy        NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LocationId INT;
    SELECT @LocationId = LocationId FROM dbo.WN_SpaceConfig WHERE Id = @Id;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceConfig
        WHERE CodePrefix = @CodePrefix
          AND Status     = 1
          AND Id        <> @Id
          AND (@LocationId IS NULL OR LocationId = @LocationId)
    )
    BEGIN
        RAISERROR('Prefix %s is already used by another configuration at this location.', 16, 1, @CodePrefix);
        RETURN;
    END

    UPDATE dbo.WN_SpaceConfig SET
        TotalSpaces       = @TotalSpaces,
        CodePrefix        = @CodePrefix,
        MinCode           = @MinCode,
        OpeningTime       = ISNULL(@OpeningTime,      OpeningTime),
        ClosingTime       = ISNULL(@ClosingTime,      ClosingTime),
        SecurityDeposit   = ISNULL(@SecurityDeposit,  SecurityDeposit),
        RentAccountId     = ISNULL(@RentAccountId,    RentAccountId),
        SecurityAccountId = ISNULL(@DepositAccountId, SecurityAccountId),
        FloorId           = ISNULL(@FloorId,          FloorId),
        PricePerHour      = ISNULL(@PricePerHour,     PricePerHour),
        PricePerDay       = ISNULL(@PricePerDay,      PricePerDay),
        PricePerMonth     = ISNULL(@PricePerMonth,    PricePerMonth),
        Amenities         = ISNULL(@Amenities,        Amenities),
        UpdatedOn         = GETUTCDATE(),
        UpdatedBy         = @UpdatedBy
    WHERE Id = @Id;

    SELECT @@ROWCOUNT AS AffectedRows;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceInventoryConfig_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpaceInventoryConfig_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceInventoryConfig SET
        IsActive  = 0,
        UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceInventoryConfig_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 5: SPACE INVENTORY CONFIG & PRICING
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_SpaceInventoryConfig_GetList]
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.Id, c.LocationId, l.Name AS LocationName,
        c.SpaceTypeId, st.Name AS SpaceTypeName,
        c.CodePrefix, c.MinCode, c.TotalSpaces,
        c.IsActive, c.UpdatedOn
    FROM dbo.WN_SpaceInventoryConfig c  WITH (NOLOCK)
    JOIN dbo.WN_Locations            l  WITH (NOLOCK) ON l.Id  = c.LocationId
    JOIN dbo.WN_SpaceTypes           st WITH (NOLOCK) ON st.Id = c.SpaceTypeId
    WHERE c.IsActive = 1
      AND (@LocationId IS NULL OR c.LocationId = @LocationId)
    ORDER BY c.LocationId, st.Name;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceInventoryConfig_Upsert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpaceInventoryConfig_Upsert]
    @LocationId  INT,
    @SpaceTypeId INT,
    @CodePrefix  NVARCHAR(10),
    @MinCode     INT,
    @TotalSpaces INT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceInventoryConfig
        WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId
    )
    BEGIN
        UPDATE dbo.WN_SpaceInventoryConfig SET
            CodePrefix  = @CodePrefix,
            MinCode     = @MinCode,
            TotalSpaces = @TotalSpaces,
            UpdatedOn   = SYSUTCDATETIME(),
            UpdatedById = @UpdatedById
        WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_SpaceInventoryConfig
            (LocationId, SpaceTypeId, CodePrefix, MinCode, TotalSpaces, IsActive, UpdatedById)
        VALUES
            (@LocationId, @SpaceTypeId, @CodePrefix, @MinCode, @TotalSpaces, 1, @UpdatedById);
    END
    SELECT Id FROM dbo.WN_SpaceInventoryConfig
    WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpacePricing_GetActive]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpacePricing_GetActive]
    @SpaceId         INT,
    @BillingPeriodId TINYINT = NULL,
    @TierTypeId      TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        sp.Id AS PricingId, sp.SpaceId,
        sp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        sp.TierTypeId, tt.Code AS TierCode,
        sp.SeatPrice, sp.SeatPrice * s.Capacity AS RoomPrice,
        sp.SecurityDeposit, sp.CurrencyCode,
        sp.EffectiveFrom, sp.EffectiveTo
    FROM dbo.WN_SpacePricing      sp WITH (NOLOCK)
    JOIN dbo.WN_Spaces            s  WITH (NOLOCK) ON s.Id  = sp.SpaceId
    JOIN dbo.WN_BillingPeriods    bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    JOIN dbo.WN_PricingTierTypes  tt WITH (NOLOCK) ON tt.Id = sp.TierTypeId
    WHERE sp.SpaceId      = @SpaceId
      AND sp.IsActive     = 1
      AND sp.TierTypeId   = @TierTypeId
      AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
      AND (@BillingPeriodId IS NULL OR sp.BillingPeriodId = @BillingPeriodId)
    ORDER BY sp.BillingPeriodId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpacePricing_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpacePricing_Insert]
    @SpaceId          INT,
    @BillingPeriodId  TINYINT,
    @TierTypeId       TINYINT       = 1,
    @SeatPrice        DECIMAL(18,4),
    @SecurityDeposit  DECIMAL(18,4) = 0,
    @RentAccountId    INT           = NULL,
    @DepositAccountId INT           = NULL,
    @CurrencyCode     NVARCHAR(10)  = 'PKR',
    @EffectiveFrom    DATE          = NULL,
    @EffectiveTo      DATE          = NULL,
    @Notes            NVARCHAR(500) = NULL,
    @CreatedById      INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @EffectiveFrom = ISNULL(@EffectiveFrom, CAST(SYSUTCDATETIME() AS DATE));

    UPDATE dbo.WN_SpacePricing SET
        EffectiveTo = DATEADD(DAY, -1, @EffectiveFrom),
        IsActive    = 0
    WHERE SpaceId         = @SpaceId
      AND BillingPeriodId = @BillingPeriodId
      AND TierTypeId      = @TierTypeId
      AND IsActive        = 1
      AND EffectiveTo     IS NULL;

    INSERT INTO dbo.WN_SpacePricing
        (SpaceId, BillingPeriodId, TierTypeId, SeatPrice, SecurityDeposit,
         RentAccountId, DepositAccountId, CurrencyCode,
         EffectiveFrom, EffectiveTo, IsActive, Notes, CreatedById)
    VALUES
        (@SpaceId, @BillingPeriodId, @TierTypeId, @SeatPrice, @SecurityDeposit,
         @RentAccountId, @DepositAccountId, @CurrencyCode,
         @EffectiveFrom, @EffectiveTo, 1, @Notes, @CreatedById);

    SELECT Id, PublicId FROM dbo.WN_SpacePricing WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM dbo.WN_Bookings
        WHERE SpaceId = @Id AND IsDeleted = 0 AND BookingStatusId IN (1, 2)
          AND EndOn > SYSUTCDATETIME()
    )
    BEGIN
        RAISERROR('Cannot delete space with active bookings.', 16, 1);
        RETURN;
    END
    UPDATE dbo.WN_Spaces SET IsActive = 0, Status = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GenerateInventory]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_GenerateInventory]
    @ConfigId    INT,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @LocationId  INT, @SpaceTypeId INT, @CodePrefix NVARCHAR(10),
            @MinCode     INT, @TotalSpaces  INT;

    SELECT @LocationId  = LocationId,  @SpaceTypeId = SpaceTypeId,
           @CodePrefix  = CodePrefix,  @MinCode     = MinCode,
           @TotalSpaces = TotalSpaces
    FROM dbo.WN_SpaceInventoryConfig
    WHERE Id = @ConfigId AND IsActive = 1;

    IF @LocationId IS NULL
    BEGIN
        RAISERROR('Config not found or inactive.', 16, 1); RETURN;
    END

    DECLARE @st_Name NVARCHAR(100);
    SELECT @st_Name = Name FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    DECLARE @Created INT = 0, @Skipped INT = 0, @i INT = 0;
    DECLARE @Code NVARCHAR(20), @SpaceName NVARCHAR(200);

    WHILE @i < @TotalSpaces
    BEGIN
        SET @Code      = @CodePrefix + CAST(@MinCode + @i AS NVARCHAR(10));
        SET @SpaceName = @st_Name + ' ' + @Code;

        IF NOT EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE LocationIdInt = @LocationId AND Code = @Code
        )
        BEGIN
            INSERT INTO dbo.WN_Spaces
                (Code, Name, LocationIdInt, SpaceTypeIdInt, Capacity, IsActive, CreatedById)
            VALUES
                (@Code, @SpaceName, @LocationId, @SpaceTypeId, 1, 1, @CreatedById);
            SET @Created = @Created + 1;
        END
        ELSE
            SET @Skipped = @Skipped + 1;

        SET @i = @i + 1;
    END

    SELECT @Created AS Created, @Skipped AS Skipped, @TotalSpaces AS Configured;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetAvailabilityCounts]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Spaces_GetAvailabilityCounts]
AS
BEGIN
    SET NOCOUNT ON;
    WITH SpaceAvail AS (
        SELECT
            s.Id,
            st.CategoryId,
            CASE WHEN EXISTS (
                SELECT 1 FROM dbo.WN_Bookings bk
                WHERE bk.SpaceId = s.Id AND bk.IsDeleted = 0
                  AND bk.BookingStatusId IN (1, 2)
                  AND SYSUTCDATETIME() < bk.EndOn
                  AND SYSUTCDATETIME() >= bk.StartOn
            ) THEN 0 ELSE 1 END AS IsAvailable
        FROM dbo.WN_Spaces     s  WITH (NOLOCK)
        JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
        WHERE s.IsActive = 1
    )
    SELECT
        sc.Code  AS CategoryCode,
        sc.Label AS CategoryLabel,
        COUNT(a.Id)        AS TotalSpaces,
        SUM(a.IsAvailable) AS AvailableNow
    FROM SpaceAvail             a
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = a.CategoryId
    GROUP BY sc.Id, sc.Code, sc.Label
    ORDER BY sc.Label;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetAvailable]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_GetAvailable]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.SpaceTypeIdInt AS SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationIdInt
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId        = s.Id
            AND bk.IsDeleted      = 0
            AND bk.BookingStatusId IN (1, 2)
            AND SYSUTCDATETIME()  < bk.EndOn
            AND SYSUTCDATETIME()  >= bk.StartOn
      )
    ORDER BY s.LocationIdInt, TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetAvailableByType]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_GetAvailableByType]
    @SpaceTypeId      INT,
    @StartOn          DATETIME2,
    @EndOn            DATETIME2,
    @ExcludeBookingId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.SpaceTypeIdInt AS SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationIdInt
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive        = 1
      AND s.SpaceTypeIdInt  = @SpaceTypeId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (1, 2)
            AND (@ExcludeBookingId IS NULL OR bk.Id <> @ExcludeBookingId)
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
      )
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetByGuid]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_Spaces_GetByGuid]
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            s.Id,
            s.IdGUID,
            s.Name,
            s.Code,
            s.Description,
            s.LocationId,
            s.SpaceTypeId,
            s.FloorId,
            s.PricePerDay,
            s.PricePerHour,
            s.ImageUrl,
            s.Amenities,
            s.Status,
            l.Name            AS LocationName,
            st.Description    AS SpaceTypeName
        FROM dbo.WN_Spaces s WITH (NOLOCK)
        LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.IdGUID  = s.LocationId
        LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.IdGUID = s.SpaceTypeId
        WHERE s.IdGUID = @IdGUID;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetByPublicId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_GetByPublicId]
    @PublicId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name, s.Description,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.FloorId, f.Name AS FloorName,
        s.SpaceTypeIdInt AS SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl, s.IsActive, s.CreatedOn,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode, vp.BillingPeriodLabel,
        (SELECT STRING_AGG(a.Name, ', ')
         FROM dbo.WN_SpaceAmenities sa
         JOIN dbo.WN_Amenities      a ON a.Id = sa.AmenityId
         WHERE sa.SpaceId = s.Id) AS Amenities
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationIdInt
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    LEFT JOIN dbo.WN_Floors     f  WITH (NOLOCK) ON f.Id  = s.FloorId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code  AS BillingPeriodCode,
            bp.Label AS BillingPeriodLabel
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.PublicId = @PublicId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetConfig]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE   PROCEDURE [dbo].[WN_Spaces_GetConfig]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, SpaceCategory, TotalSpaces, CodePrefix, MinCode,
           DefaultCapacities, OpeningTime, ClosingTime,
           SecurityDeposit, UpdatedOn, UpdatedBy
    FROM dbo.WN_SpaceConfig
    ORDER BY Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 4: SPACES
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Spaces_GetList]
    @Page        INT           = 1,
    @Limit       INT           = 20,
    @Search      NVARCHAR(255) = NULL,
    @LocationId  INT           = NULL,
    @SpaceTypeId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        s.Id, s.PublicId, s.Code, s.Name, s.Description,
        s.LocationIdInt AS LocationId, l.Name AS LocationName,
        s.FloorId, f.Name AS FloorName, f.FloorNumber,
        s.SpaceTypeIdInt AS SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl, s.IsActive, s.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationIdInt
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    LEFT JOIN dbo.WN_Floors     f  WITH (NOLOCK) ON f.Id  = s.FloorId
    WHERE s.IsActive = 1
      AND (@LocationId  IS NULL OR s.LocationIdInt  = @LocationId)
      AND (@SpaceTypeId IS NULL OR s.SpaceTypeIdInt = @SpaceTypeId)
      AND (@Search      IS NULL OR @Search = ''
           OR s.Name LIKE '%' + @Search + '%'
           OR s.Code LIKE '%' + @Search + '%')
    ORDER BY s.LocationIdInt, TRY_CAST(s.Code AS INT), s.Code
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetVacant]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ------------------------------------------------------------
-- 4.9  WN_Spaces_GetVacant
-- WHERE + SELECT derive CompanyId/BranchId via Space → Location
-- ------------------------------------------------------------
CREATE   PROCEDURE [dbo].[WN_Spaces_GetVacant]
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
        l.CompanyId    AS CompanyId,
        l.BranchId     AS BranchId,
        st.Description AS SpaceTypeName
    FROM dbo.WN_Spaces s
    LEFT JOIN dbo.WN_Locations  l  ON l.IdGUID  = s.LocationId
    LEFT JOIN dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    WHERE s.Status = 1
      AND (@CompanyId IS NULL OR l.CompanyId = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId  = @BranchId)
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
/****** Object:  StoredProcedure [dbo].[WN_Spaces_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


CREATE   PROCEDURE [dbo].[WN_Spaces_Insert]
    @Code        NVARCHAR(20),
    @Name        NVARCHAR(200),
    @Description NVARCHAR(1000) = NULL,
    @LocationId  INT,
    @FloorId     INT            = NULL,
    @SpaceTypeId INT,
    @Capacity    SMALLINT,
    @ImageUrl    NVARCHAR(500)  = NULL,
    @CreatedById INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.WN_Spaces WHERE LocationIdInt = @LocationId AND Code = @Code)
    BEGIN
        RAISERROR('Space code %s already exists at this location.', 16, 1, @Code);
        RETURN;
    END
    INSERT INTO dbo.WN_Spaces
        (Code, Name, Description, LocationIdInt, FloorId, SpaceTypeIdInt,
         Capacity, ImageUrl, IsActive, CreatedById)
    VALUES
        (@Code, @Name, @Description, @LocationId, @FloorId, @SpaceTypeId,
         @Capacity, @ImageUrl, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_Spaces WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_SetAmenities]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_SetAmenities]
    @SpaceId    INT,
    @AmenityIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.WN_SpaceAmenities WHERE SpaceId = @SpaceId;
    INSERT INTO dbo.WN_SpaceAmenities (SpaceId, AmenityId)
    SELECT @SpaceId, CAST(LTRIM(RTRIM(value)) AS INT)
    FROM STRING_SPLIT(@AmenityIds, ',')
    WHERE LTRIM(RTRIM(value)) <> '';
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Spaces_Update]
    @Id          INT,
    @Name        NVARCHAR(200)  = NULL,
    @Description NVARCHAR(1000) = NULL,
    @FloorId     INT            = NULL,
    @SpaceTypeId INT            = NULL,
    @Capacity    SMALLINT       = NULL,
    @ImageUrl    NVARCHAR(500)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Spaces SET
        Name          = ISNULL(@Name,        Name),
        Description   = ISNULL(@Description, Description),
        FloorId       = ISNULL(@FloorId,     FloorId),
        SpaceTypeIdInt= ISNULL(@SpaceTypeId, SpaceTypeIdInt),
        Capacity      = ISNULL(@Capacity,    Capacity),
        ImageUrl      = ISNULL(@ImageUrl,    ImageUrl)
    WHERE Id = @Id;
    SELECT @@ROWCOUNT AS AffectedRows;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_UpdateConfig]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Update the UpdateConfig SP to support SecurityDeposit
CREATE   PROCEDURE [dbo].[WN_Spaces_UpdateConfig]
    @SpaceCategory      NVARCHAR(20),
    @TotalSpaces        INT,
    @DefaultCapacities  NVARCHAR(50)  = NULL,
    @OpeningTime        NVARCHAR(5)   = NULL,
    @ClosingTime        NVARCHAR(5)   = NULL,
    @AdminEmail         NVARCHAR(255) = NULL,
    @SecurityDeposit    DECIMAL(10,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceConfig
    SET TotalSpaces       = @TotalSpaces,
        DefaultCapacities = ISNULL(@DefaultCapacities, DefaultCapacities),
        OpeningTime       = ISNULL(@OpeningTime,       OpeningTime),
        ClosingTime       = ISNULL(@ClosingTime,       ClosingTime),
        SecurityDeposit   = ISNULL(@SecurityDeposit,   SecurityDeposit),
        UpdatedOn         = GETUTCDATE(),
        UpdatedBy         = @AdminEmail
    WHERE SpaceCategory = @SpaceCategory;

    SELECT @@ROWCOUNT AS AffectedRows;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceTypes_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpaceTypes_GetList]
    @CategoryId TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        st.Id, st.PublicId, st.Name, st.Description,
        st.CategoryId, sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        st.HourlyAllowed, st.IsActive
    FROM dbo.WN_SpaceTypes      st WITH (NOLOCK)
    LEFT JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    WHERE st.IsActive = 1
      AND (@CategoryId IS NULL OR st.CategoryId = @CategoryId)
    ORDER BY sc.Label, st.Name;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceTypes_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_SpaceTypes_Update]
    @Id            INT,
    @Name          NVARCHAR(100) = NULL,
    @Description   NVARCHAR(500) = NULL,
    @HourlyAllowed BIT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceTypes SET
        Name          = ISNULL(@Name,          Name),
        Description   = ISNULL(@Description,   Description),
        HourlyAllowed = ISNULL(@HourlyAllowed, HourlyAllowed)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_Delete]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetByEmail]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_GetByEmail]
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        u.Id, u.PublicId, u.Email, u.PasswordHash,
        u.Name, u.PhoneNumber, u.RoleId,
        u.CompanyId, u.CityId, u.IsActive,
        u.LockoutEnd, u.CreatedOn
    FROM dbo.WN_Users u WITH (NOLOCK)
    WHERE u.Email = @Email;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetByGuid]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[WN_Users_GetByGuid]
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            u.Id,
            u.IdGUID,
            u.Email,
            u.Name,
            u.PhoneNumber,
            u.CreatedOn,
            u.RoleId,
            u.CompanyId,
            u.Status
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.IdGUID = @IdGUID;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetById]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_GetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        u.Id, u.PublicId, u.Email, u.Name, u.PhoneNumber,
        u.RoleId, u.CompanyId, co.CompanyName AS CompanyName,
        u.CityId, ci.Name AS CityName,
        u.Address, u.CnicOrPassport, u.AvatarUrl,
        u.IsActive, u.Notes, u.CreatedOn
    FROM dbo.WN_Users u WITH (NOLOCK)
    LEFT JOIN dbo.Company co WITH (NOLOCK) ON co.Id = u.CompanyId
    LEFT JOIN dbo.WN_Cities    ci WITH (NOLOCK) ON ci.Id = u.CityId
    WHERE u.Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetByPublicId]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_GetByPublicId]
    @PublicId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        u.Id, u.PublicId, u.Email, u.Name, u.PhoneNumber,
        u.RoleId, u.CompanyId, co.CompanyName AS CompanyName,
        u.CityId, ci.Name AS CityName,
        u.Address, u.CnicOrPassport, u.AvatarUrl,
        u.IsActive, u.Notes, u.CreatedOn
    FROM dbo.WN_Users u WITH (NOLOCK)
    LEFT JOIN dbo.Company co WITH (NOLOCK) ON co.Id = u.CompanyId
    LEFT JOIN dbo.WN_Cities    ci WITH (NOLOCK) ON ci.Id = u.CityId
    WHERE u.PublicId = @PublicId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetHistory]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_GetHistory]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        b.Id            AS BookingId,
        b.PublicId      AS BookingPublicId,
        s.Name          AS SpaceName,
        s.Code          AS SpaceCode,
        st.Name         AS SpaceTypeName,
        b.StartOn,
        b.EndOn,
        bs.Label        AS BookingStatus,
        bp.Label        AS BillingPeriod,
        sp.SeatPrice * s.Capacity AS RoomPrice,
        b.CreatedOn     AS BookedOn
    FROM dbo.WN_Bookings        b  WITH (NOLOCK)
    JOIN dbo.WN_Spaces          s  WITH (NOLOCK) ON s.Id  = b.SpaceId
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeIdInt
    JOIN dbo.WN_SpacePricing    sp WITH (NOLOCK) ON sp.Id = b.PricingId
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    JOIN dbo.WN_BookingStatuses bs WITH (NOLOCK) ON bs.Id = b.BookingStatusId
    WHERE b.UserId    = @UserId
      AND b.IsDeleted = 0
    ORDER BY b.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetList]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- SECTION 1: USERS
-- ============================================================

CREATE   PROCEDURE [dbo].[WN_Users_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT
        u.Id,
        u.PublicId,
        u.Email,
        u.Name,
        u.PhoneNumber,
        u.RoleId,
        u.CompanyId,
        co.CompanyName  AS CompanyName,
        u.CityId,
        ci.Name         AS CityName,
        u.Address,
        u.CnicOrPassport,
        u.AvatarUrl,
        u.IsActive,
        u.Notes,
        u.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Users u WITH (NOLOCK)
    LEFT JOIN dbo.Company co WITH (NOLOCK) ON co.Id = u.CompanyId
    LEFT JOIN dbo.WN_Cities    ci WITH (NOLOCK) ON ci.Id = u.CityId
    WHERE u.IsActive = 1
      AND (@Search IS NULL OR @Search = ''
           OR u.Name        LIKE '%' + @Search + '%'
           OR u.Email       LIKE '%' + @Search + '%'
           OR u.PhoneNumber LIKE '%' + @Search + '%')
    ORDER BY u.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_Insert]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_Insert]
    @Email          NVARCHAR(256),
    @PasswordHash   NVARCHAR(500)  = NULL,
    @Name           NVARCHAR(200)  = NULL,
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @RoleId         INT            = 14,
    @CompanyId      INT            = NULL,
    @CityId         INT            = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @AvatarUrl      NVARCHAR(500)  = NULL,
    @Notes          NVARCHAR(1000) = NULL,
    @CreatedById    INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @Email)
    BEGIN
        SELECT Id, PublicId FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @Email;
        RETURN;
    END
    INSERT INTO dbo.WN_Users
        (Email, PasswordHash, Name, PhoneNumber, RoleId, CompanyId, CityId,
         Address, CnicOrPassport, AvatarUrl, Notes, IsActive, CreatedById)
    VALUES
        (@Email, @PasswordHash, @Name, @PhoneNumber, @RoleId, @CompanyId, @CityId,
         @Address, @CnicOrPassport, @AvatarUrl, @Notes, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_Users WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_SetRole]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_SetRole]
    @Id     INT,
    @RoleId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET RoleId = @RoleId WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_SetStatus]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_SetStatus]
    @Id       INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET IsActive = @IsActive WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_Update]    Script Date: 10/08/2026 10:45:43 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE   PROCEDURE [dbo].[WN_Users_Update]
    @Id             INT,
    @Name           NVARCHAR(200)  = NULL,
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @CompanyId      INT            = NULL,
    @CityId         INT            = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @AvatarUrl      NVARCHAR(500)  = NULL,
    @Notes          NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET
        Name           = ISNULL(@Name,           Name),
        PhoneNumber    = ISNULL(@PhoneNumber,     PhoneNumber),
        CompanyId      = ISNULL(@CompanyId,       CompanyId),
        CityId         = ISNULL(@CityId,          CityId),
        Address        = ISNULL(@Address,         Address),
        CnicOrPassport = ISNULL(@CnicOrPassport,  CnicOrPassport),
        AvatarUrl      = ISNULL(@AvatarUrl,       AvatarUrl),
        Notes          = ISNULL(@Notes,           Notes)
    WHERE Id = @Id;
    SELECT @@ROWCOUNT AS AffectedRows;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, IdGUID, Code, FirstName, LastName, Email, PhoneNumber, CNIC, Address, CityId, IsActive, CreatedAt, Notes, UserId
    FROM dbo.WN_Customers WITH (NOLOCK)
    WHERE UserId = @UserId;
END
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetByEmail]
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, IdGUID, Code, FirstName, LastName, Email, PhoneNumber, CNIC, Address, CityId, IsActive, CreatedAt, Notes, UserId
    FROM dbo.WN_Customers WITH (NOLOCK)
    WHERE Email = @Email;
END
GO

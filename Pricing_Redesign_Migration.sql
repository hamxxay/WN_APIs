-- =============================================================================
-- WorkNest Pricing Database Redesign — COMPLETE MIGRATION SCRIPT
-- =============================================================================
-- IMPORTANT: Run this on your database AFTER taking a full backup.
--
-- PHASE 1 — Add Price column to WN_SpacePricing (rename SeatPrice -> Price)
-- PHASE 2 — Migrate old prices from WN_Spaces + WN_SpaceConfig into WN_SpacePricing
-- PHASE 3 — Update all stored procedures and views (preserve API aliases)
-- PHASE 4 — Validation queries
-- PHASE 5 — (DEFERRED) Drop old deprecated columns
-- =============================================================================

PRINT '=== WorkNest Pricing Migration ===';
PRINT '=== Start: ' + CONVERT(NVARCHAR(50), SYSUTCDATETIME(), 121);
PRINT '';

-- PRE-MIGRATION BASELINES
SELECT 'WN_Spaces' AS TableName, COUNT(*) AS TotalRows,
    SUM(CASE WHEN PricePerHour  > 0 THEN 1 ELSE 0 END) AS RowsWithHourlyPrice,
    SUM(CASE WHEN PricePerDay   > 0 THEN 1 ELSE 0 END) AS RowsWithDailyPrice,
    SUM(CASE WHEN PricePerMonth > 0 THEN 1 ELSE 0 END) AS RowsWithMonthlyPrice
FROM dbo.WN_Spaces;
SELECT 'WN_SpaceConfig' AS TableName, COUNT(*) AS TotalRows,
    SUM(CASE WHEN PricePerHour  > 0 THEN 1 ELSE 0 END) AS RowsWithHourlyPrice,
    SUM(CASE WHEN PricePerDay   > 0 THEN 1 ELSE 0 END) AS RowsWithDailyPrice,
    SUM(CASE WHEN PricePerMonth > 0 THEN 1 ELSE 0 END) AS RowsWithMonthlyPrice
FROM dbo.WN_SpaceConfig;
SELECT 'WN_SpacePricing' AS TableName, COUNT(*) AS TotalRows,
    SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveRows
FROM dbo.WN_SpacePricing;
PRINT '--- BillingPeriods (verify codes before Phase 2) ---';
SELECT Id, Code, Label, IsActive FROM dbo.WN_BillingPeriods ORDER BY Id;
GO

-- =============================================================================
-- PHASE 1: ADD Price COLUMN TO WN_SpacePricing
-- =============================================================================
PRINT '=== PHASE 1: Adding Price column to WN_SpacePricing ===';
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_SpacePricing') AND name = 'Price')
    BEGIN
        ALTER TABLE dbo.WN_SpacePricing ADD Price DECIMAL(18,4) NULL;
        PRINT 'Phase 1: Price column added.';
    END ELSE PRINT 'Phase 1: Price column already exists.';

    UPDATE dbo.WN_SpacePricing SET Price = SeatPrice WHERE Price IS NULL;
    PRINT 'Phase 1: Copied SeatPrice -> Price (' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows).';

    ALTER TABLE dbo.WN_SpacePricing ALTER COLUMN Price DECIMAL(18,4) NOT NULL;
    PRINT 'Phase 1: Price set NOT NULL.';

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.WN_SpacePricing') AND name = 'CK_WN_SpacePricing_SeatPrice')
    BEGIN ALTER TABLE dbo.WN_SpacePricing DROP CONSTRAINT CK_WN_SpacePricing_SeatPrice; PRINT 'Phase 1: Old SeatPrice constraint dropped.'; END

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.WN_SpacePricing') AND name = 'CK_WN_SpacePricing_Price')
    BEGIN ALTER TABLE dbo.WN_SpacePricing ADD CONSTRAINT CK_WN_SpacePricing_Price CHECK (Price >= 0); PRINT 'Phase 1: Price constraint added.'; END

    COMMIT TRANSACTION;
    PRINT 'Phase 1: COMMITTED.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'Phase 1 FAILED: ' + ERROR_MESSAGE(); THROW;
END CATCH;
GO

SELECT 'Phase 1 Check' AS Step,
    SUM(CASE WHEN Price = SeatPrice THEN 1 ELSE 0 END) AS RowsMatching,
    SUM(CASE WHEN Price IS NULL THEN 1 ELSE 0 END) AS NullPriceRows
FROM dbo.WN_SpacePricing;
GO

-- =============================================================================
-- PHASE 2: MIGRATE LEGACY PRICES INTO WN_SpacePricing
-- =============================================================================
PRINT '=== PHASE 2: Migrating legacy prices into WN_SpacePricing ===';
GO

DECLARE @BH TINYINT, @BD TINYINT, @BM TINYINT;
SELECT @BH = Id FROM dbo.WN_BillingPeriods WHERE IsActive=1 AND (LOWER(Code) LIKE '%hour%'  OR LOWER(Label) LIKE '%hour%');
SELECT @BD = Id FROM dbo.WN_BillingPeriods WHERE IsActive=1 AND (LOWER(Code) LIKE '%day%'   OR LOWER(Label) LIKE '%day%');
SELECT @BM = Id FROM dbo.WN_BillingPeriods WHERE IsActive=1 AND (LOWER(Code) LIKE '%month%' OR LOWER(Label) LIKE '%month%');
PRINT 'BillingPeriod IDs: Hourly=' + ISNULL(CAST(@BH AS NVARCHAR),'NOT FOUND')
    + ', Daily=' + ISNULL(CAST(@BD AS NVARCHAR),'NOT FOUND')
    + ', Monthly=' + ISNULL(CAST(@BM AS NVARCHAR),'NOT FOUND');

IF @BH IS NULL OR @BD IS NULL OR @BM IS NULL
BEGIN PRINT 'ABORT: BillingPeriod lookup failed. Check WN_BillingPeriods table.'; GOTO Phase2End; END

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @T TINYINT = 1; -- Default TierTypeId

    -- From WN_Spaces
    INSERT INTO dbo.WN_SpacePricing (PublicId,SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,CurrencyCode,EffectiveFrom,IsActive,Notes,CreatedOn)
    SELECT NEWID(),s.Id,@BH,@T,s.PricePerHour,s.PricePerHour,0,'PKR',CAST(GETUTCDATE() AS DATE),1,'Migrated from WN_Spaces.PricePerHour',SYSUTCDATETIME()
    FROM dbo.WN_Spaces s WHERE s.PricePerHour>0 AND NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.BillingPeriodId=@BH AND sp.TierTypeId=@T AND sp.IsActive=1);
    PRINT 'WN_Spaces.PricePerHour -> ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows.';

    INSERT INTO dbo.WN_SpacePricing (PublicId,SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,CurrencyCode,EffectiveFrom,IsActive,Notes,CreatedOn)
    SELECT NEWID(),s.Id,@BD,@T,s.PricePerDay,s.PricePerDay,0,'PKR',CAST(GETUTCDATE() AS DATE),1,'Migrated from WN_Spaces.PricePerDay',SYSUTCDATETIME()
    FROM dbo.WN_Spaces s WHERE s.PricePerDay>0 AND NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.BillingPeriodId=@BD AND sp.TierTypeId=@T AND sp.IsActive=1);
    PRINT 'WN_Spaces.PricePerDay -> ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows.';

    INSERT INTO dbo.WN_SpacePricing (PublicId,SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,CurrencyCode,EffectiveFrom,IsActive,Notes,CreatedOn)
    SELECT NEWID(),s.Id,@BM,@T,s.PricePerMonth,s.PricePerMonth,0,'PKR',CAST(GETUTCDATE() AS DATE),1,'Migrated from WN_Spaces.PricePerMonth',SYSUTCDATETIME()
    FROM dbo.WN_Spaces s WHERE s.PricePerMonth>0 AND NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.BillingPeriodId=@BM AND sp.TierTypeId=@T AND sp.IsActive=1);
    PRINT 'WN_Spaces.PricePerMonth -> ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows.';

    -- From WN_SpaceConfig (for spaces that still lack pricing)
    INSERT INTO dbo.WN_SpacePricing (PublicId,SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,CurrencyCode,EffectiveFrom,IsActive,Notes,CreatedOn)
    SELECT NEWID(),s.Id,@BH,@T,sc.PricePerHour,sc.PricePerHour,ISNULL(sc.SecurityDeposit,0),'PKR',CAST(GETUTCDATE() AS DATE),1,'Migrated from WN_SpaceConfig.PricePerHour',SYSUTCDATETIME()
    FROM dbo.WN_SpaceConfig sc JOIN dbo.WN_Spaces s ON s.LocationIdInt=sc.LocationId AND s.SpaceTypeIdInt=sc.SpaceTypeId AND s.IsActive=1
    WHERE sc.Status=1 AND sc.PricePerHour>0 AND NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.BillingPeriodId=@BH AND sp.TierTypeId=@T AND sp.IsActive=1);
    PRINT 'WN_SpaceConfig.PricePerHour -> ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows.';

    INSERT INTO dbo.WN_SpacePricing (PublicId,SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,CurrencyCode,EffectiveFrom,IsActive,Notes,CreatedOn)
    SELECT NEWID(),s.Id,@BD,@T,sc.PricePerDay,sc.PricePerDay,ISNULL(sc.SecurityDeposit,0),'PKR',CAST(GETUTCDATE() AS DATE),1,'Migrated from WN_SpaceConfig.PricePerDay',SYSUTCDATETIME()
    FROM dbo.WN_SpaceConfig sc JOIN dbo.WN_Spaces s ON s.LocationIdInt=sc.LocationId AND s.SpaceTypeIdInt=sc.SpaceTypeId AND s.IsActive=1
    WHERE sc.Status=1 AND sc.PricePerDay>0 AND NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.BillingPeriodId=@BD AND sp.TierTypeId=@T AND sp.IsActive=1);
    PRINT 'WN_SpaceConfig.PricePerDay -> ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows.';

    INSERT INTO dbo.WN_SpacePricing (PublicId,SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,CurrencyCode,EffectiveFrom,IsActive,Notes,CreatedOn)
    SELECT NEWID(),s.Id,@BM,@T,sc.PricePerMonth,sc.PricePerMonth,ISNULL(sc.SecurityDeposit,0),'PKR',CAST(GETUTCDATE() AS DATE),1,'Migrated from WN_SpaceConfig.PricePerMonth',SYSUTCDATETIME()
    FROM dbo.WN_SpaceConfig sc JOIN dbo.WN_Spaces s ON s.LocationIdInt=sc.LocationId AND s.SpaceTypeIdInt=sc.SpaceTypeId AND s.IsActive=1
    WHERE sc.Status=1 AND sc.PricePerMonth>0 AND NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.BillingPeriodId=@BM AND sp.TierTypeId=@T AND sp.IsActive=1);
    PRINT 'WN_SpaceConfig.PricePerMonth -> ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows.';

    COMMIT TRANSACTION;
    PRINT 'Phase 2: COMMITTED.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT 'Phase 2 FAILED: ' + ERROR_MESSAGE(); THROW;
END CATCH;
Phase2End:
GO

PRINT 'Phase 2 duplicate check (must return 0 rows):';
SELECT SpaceId,BillingPeriodId,TierTypeId,COUNT(*) AS Cnt FROM dbo.WN_SpacePricing WHERE IsActive=1
GROUP BY SpaceId,BillingPeriodId,TierTypeId HAVING COUNT(*) > 1;
GO

-- =============================================================================
-- PHASE 3: UPDATE STORED PROCEDURES AND VIEWS
-- SeatPrice output aliases preserved throughout. API contract unchanged.
-- =============================================================================
PRINT '=== PHASE 3: Updating stored procedures and views ===';
GO

-- 3.1 VW_WN_SpacePricingActive
ALTER VIEW [dbo].[VW_WN_SpacePricingActive] AS
    SELECT sp.Id AS PricingId, sp.SpaceId, s.Code AS SpaceCode, s.Name AS SpaceName, s.Capacity,
        sp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        sp.TierTypeId, tt.Code AS TierCode,
        sp.Price              AS SeatPrice,      -- alias: API unchanged
        sp.Price * s.Capacity AS RoomPrice,      -- business rule preserved
        sp.SecurityDeposit, sp.RentAccountId, sp.DepositAccountId, sp.CurrencyCode,
        sp.EffectiveFrom, sp.EffectiveTo
    FROM  [dbo].[WN_SpacePricing]     sp
    JOIN  [dbo].[WN_Spaces]           s  ON s.Id  = sp.SpaceId
    JOIN  [dbo].[WN_BillingPeriods]   bp ON bp.Id = sp.BillingPeriodId
    JOIN  [dbo].[WN_PricingTierTypes] tt ON tt.Id = sp.TierTypeId
    WHERE sp.IsActive = 1 AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE));
GO
PRINT '3.1: VW_WN_SpacePricingActive updated.';
GO

-- 3.2 VW_WN_BookingSummary
ALTER VIEW [dbo].[VW_WN_BookingSummary] AS
    SELECT b.Id AS BookingId, b.PublicId AS BookingPublicId, b.StartOn, b.EndOn,
        b.BookingStatusId, bs.Code AS BookingStatusCode, bs.Label AS BookingStatusLabel,
        b.Notes, b.CancelReason, b.CreatedOn AS BookedOn,
        u.Id AS UserId, u.PublicId AS UserPublicId, u.Name AS UserName, u.Email AS UserEmail,
        s.Id AS SpaceId, s.PublicId AS SpacePublicId, s.Code AS SpaceCode, s.Name AS SpaceName, s.Capacity AS SpaceCapacity,
        st.Id AS SpaceTypeId, st.Name AS SpaceTypeName,
        l.Id AS LocationId, l.Name AS LocationName,
        br.Id AS BranchId, br.[Description] AS BranchName,
        co.Id AS CompanyId, co.CompanyName,
        sp.Price              AS SeatPrice,      -- alias: API unchanged
        sp.Price * s.Capacity AS RoomPrice,      -- business rule preserved
        sp.SecurityDeposit,
        bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        ch.ChallanNumber, ch.ValidUntil AS ChallanValidUntil, ch.StatusId AS ChallanStatusId
    FROM       [dbo].[WN_Bookings]        b
    JOIN       [dbo].[WN_Users]           u  ON u.Id  = b.UserId
    JOIN       [dbo].[WN_Spaces]          s  ON s.Id  = b.SpaceId
    JOIN       [dbo].[WN_SpaceTypes]      st ON st.Id = s.SpaceTypeIdInt
    JOIN       [dbo].[WN_Locations]       l  ON l.Id  = s.LocationIdInt
    JOIN       [dbo].[Branches]           br ON br.Id = l.BranchId
    JOIN       [dbo].[Company]            co ON co.Id = br.CompanyId
    JOIN       [dbo].[WN_SpacePricing]    sp ON sp.Id = b.PricingId
    JOIN       [dbo].[WN_BillingPeriods]  bp ON bp.Id = sp.BillingPeriodId
    JOIN       [dbo].[WN_BookingStatuses] bs ON bs.Id = b.BookingStatusId
    OUTER APPLY (SELECT TOP 1 ChallanNumber,ValidUntil,StatusId FROM [dbo].[WN_Challans] WHERE BookingId=b.Id ORDER BY CreatedOn DESC) ch
    WHERE b.IsDeleted = 0;
GO
PRINT '3.2: VW_WN_BookingSummary updated.';
GO

-- 3.3 WN_SpacePricing_GetActive
ALTER PROCEDURE [dbo].[WN_SpacePricing_GetActive]
    @SpaceId INT, @BillingPeriodId TINYINT = NULL, @TierTypeId TINYINT = 1
AS BEGIN
    SET NOCOUNT ON;
    SELECT sp.Id AS PricingId, sp.SpaceId,
        sp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        sp.TierTypeId, tt.Code AS TierCode,
        sp.Price              AS SeatPrice,
        sp.Price * s.Capacity AS RoomPrice,
        sp.SecurityDeposit, sp.CurrencyCode, sp.EffectiveFrom, sp.EffectiveTo
    FROM dbo.WN_SpacePricing sp WITH (NOLOCK)
    JOIN dbo.WN_Spaces s  WITH (NOLOCK) ON s.Id  = sp.SpaceId
    JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    JOIN dbo.WN_PricingTierTypes tt WITH (NOLOCK) ON tt.Id = sp.TierTypeId
    WHERE sp.SpaceId = @SpaceId AND sp.IsActive = 1 AND sp.TierTypeId = @TierTypeId
      AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
      AND (@BillingPeriodId IS NULL OR sp.BillingPeriodId = @BillingPeriodId)
    ORDER BY sp.BillingPeriodId;
END
GO
PRINT '3.3: WN_SpacePricing_GetActive updated.';
GO

-- 3.4 WN_SpacePricing_Insert (accepts @Price AND legacy @SeatPrice)
ALTER PROCEDURE [dbo].[WN_SpacePricing_Insert]
    @SpaceId INT, @BillingPeriodId TINYINT, @TierTypeId TINYINT = 1,
    @Price DECIMAL(18,4) = NULL,       -- new preferred param
    @SeatPrice DECIMAL(18,4) = NULL,   -- legacy alias still accepted
    @SecurityDeposit DECIMAL(18,4) = 0, @RentAccountId INT = NULL, @DepositAccountId INT = NULL,
    @CurrencyCode NVARCHAR(10) = 'PKR', @EffectiveFrom DATE = NULL, @EffectiveTo DATE = NULL,
    @Notes NVARCHAR(500) = NULL, @CreatedById INT = NULL
AS BEGIN
    SET NOCOUNT ON;
    DECLARE @R DECIMAL(18,4) = ISNULL(@Price, @SeatPrice);
    SET @EffectiveFrom = ISNULL(@EffectiveFrom, CAST(SYSUTCDATETIME() AS DATE));
    UPDATE dbo.WN_SpacePricing SET EffectiveTo = DATEADD(DAY,-1,@EffectiveFrom), IsActive = 0
    WHERE SpaceId=@SpaceId AND BillingPeriodId=@BillingPeriodId AND TierTypeId=@TierTypeId AND IsActive=1 AND EffectiveTo IS NULL;
    INSERT INTO dbo.WN_SpacePricing (SpaceId,BillingPeriodId,TierTypeId,Price,SeatPrice,SecurityDeposit,RentAccountId,DepositAccountId,CurrencyCode,EffectiveFrom,EffectiveTo,IsActive,Notes,CreatedById)
    VALUES (@SpaceId,@BillingPeriodId,@TierTypeId,@R,@R,@SecurityDeposit,@RentAccountId,@DepositAccountId,@CurrencyCode,@EffectiveFrom,@EffectiveTo,1,@Notes,@CreatedById);
    SELECT Id, PublicId FROM dbo.WN_SpacePricing WHERE Id = SCOPE_IDENTITY();
END
GO
PRINT '3.4: WN_SpacePricing_Insert updated.';
GO

-- 3.5 WN_Spaces_GetAvailable
ALTER PROCEDURE [dbo].[WN_Spaces_GetAvailable] AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.PublicId,s.Code,s.Name,s.LocationIdInt AS LocationId,l.Name AS LocationName,
        s.SpaceTypeIdInt AS SpaceTypeId,st.Name AS SpaceTypeName,sc.Code AS CategoryCode,sc.Label AS CategoryLabel,
        s.Capacity,s.ImageUrl,vp.SeatPrice,vp.RoomPrice,vp.SecurityDeposit,vp.BillingPeriodCode
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id=s.LocationIdInt
    JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id=s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id=st.CategoryId
    OUTER APPLY (SELECT TOP 1 sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing sp JOIN dbo.WN_BillingPeriods bp ON bp.Id=sp.BillingPeriodId
        WHERE sp.SpaceId=s.Id AND sp.IsActive=1 AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC) vp
    WHERE s.IsActive=1 AND NOT EXISTS (SELECT 1 FROM dbo.WN_Bookings bk WHERE bk.SpaceId=s.Id AND bk.IsDeleted=0 AND bk.BookingStatusId IN(1,2) AND SYSUTCDATETIME()<bk.EndOn AND SYSUTCDATETIME()>=bk.StartOn)
    ORDER BY s.LocationIdInt, TRY_CAST(s.Code AS INT), s.Code;
END
GO
PRINT '3.5: WN_Spaces_GetAvailable updated.';
GO

-- 3.6 WN_Spaces_GetAvailableByType
ALTER PROCEDURE [dbo].[WN_Spaces_GetAvailableByType]
    @SpaceTypeId INT, @StartOn DATETIME2, @EndOn DATETIME2, @ExcludeBookingId INT = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.PublicId,s.Code,s.Name,s.LocationIdInt AS LocationId,l.Name AS LocationName,
        s.SpaceTypeIdInt AS SpaceTypeId,st.Name AS SpaceTypeName,sc.Code AS CategoryCode,sc.Label AS CategoryLabel,
        s.Capacity,s.ImageUrl,vp.SeatPrice,vp.RoomPrice,vp.SecurityDeposit,vp.BillingPeriodCode
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id=s.LocationIdInt
    JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id=s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id=st.CategoryId
    OUTER APPLY (SELECT TOP 1 sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing sp JOIN dbo.WN_BillingPeriods bp ON bp.Id=sp.BillingPeriodId
        WHERE sp.SpaceId=s.Id AND sp.IsActive=1 AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC) vp
    WHERE s.IsActive=1 AND s.SpaceTypeIdInt=@SpaceTypeId
      AND NOT EXISTS (SELECT 1 FROM dbo.WN_Bookings bk WHERE bk.SpaceId=s.Id AND bk.IsDeleted=0 AND bk.BookingStatusId IN(1,2)
          AND (@ExcludeBookingId IS NULL OR bk.Id<>@ExcludeBookingId) AND @StartOn<bk.EndOn AND @EndOn>bk.StartOn)
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
PRINT '3.6: WN_Spaces_GetAvailableByType updated.';
GO

-- 3.7 WN_Bookings_GetAvailableSpaces
ALTER PROCEDURE [dbo].[WN_Bookings_GetAvailableSpaces]
    @SpaceTypeId INT, @StartOn DATETIME2, @EndOn DATETIME2, @Capacity INT = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.PublicId,s.Code,s.Name,s.LocationIdInt AS LocationId,l.Name AS LocationName,
        s.Capacity,s.ImageUrl,vp.SeatPrice,vp.RoomPrice,vp.SecurityDeposit,vp.BillingPeriodCode
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id=s.LocationIdInt
    OUTER APPLY (SELECT TOP 1 sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing sp JOIN dbo.WN_BillingPeriods bp ON bp.Id=sp.BillingPeriodId
        WHERE sp.SpaceId=s.Id AND sp.IsActive=1 AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC) vp
    WHERE s.IsActive=1 AND s.SpaceTypeIdInt=@SpaceTypeId AND (@Capacity IS NULL OR s.Capacity>=@Capacity)
      AND NOT EXISTS (SELECT 1 FROM dbo.WN_Bookings bk WHERE bk.SpaceId=s.Id AND bk.IsDeleted=0 AND bk.BookingStatusId IN(1,2) AND @StartOn<bk.EndOn AND @EndOn>bk.StartOn)
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
PRINT '3.7: WN_Bookings_GetAvailableSpaces updated.';
GO

-- 3.8 WN_Bookings_GetAvailableSpacesForReassignment
ALTER PROCEDURE [dbo].[WN_Bookings_GetAvailableSpacesForReassignment]
    @SpaceTypeId INT, @StartOn DATETIME2, @EndOn DATETIME2, @ExcludeBookingId INT
AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.PublicId,s.Code,s.Name,s.LocationIdInt AS LocationId,l.Name AS LocationName,
        s.Capacity,s.ImageUrl,vp.SeatPrice,vp.RoomPrice,vp.SecurityDeposit
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id=s.LocationIdInt
    OUTER APPLY (SELECT TOP 1 sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, sp.SecurityDeposit
        FROM dbo.WN_SpacePricing sp WHERE sp.SpaceId=s.Id AND sp.IsActive=1
          AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC) vp
    WHERE s.IsActive=1 AND s.SpaceTypeIdInt=@SpaceTypeId
      AND NOT EXISTS (SELECT 1 FROM dbo.WN_Bookings bk WHERE bk.SpaceId=s.Id AND bk.IsDeleted=0 AND bk.BookingStatusId IN(1,2) AND bk.Id<>@ExcludeBookingId AND @StartOn<bk.EndOn AND @EndOn>bk.StartOn)
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
PRINT '3.8: WN_Bookings_GetAvailableSpacesForReassignment updated.';
GO

-- 3.9 WN_Bookings_GetSmartAvailable
ALTER PROCEDURE [dbo].[WN_Bookings_GetSmartAvailable]
    @CategoryCode NVARCHAR(30), @StartOn DATETIME2, @EndOn DATETIME2, @Capacity INT = NULL
AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.PublicId,s.Code,s.Name,s.LocationIdInt AS LocationId,l.Name AS LocationName,
        s.SpaceTypeIdInt AS SpaceTypeId,st.Name AS SpaceTypeName,sc.Code AS CategoryCode,sc.Label AS CategoryLabel,
        s.Capacity,s.ImageUrl,vp.SeatPrice,vp.RoomPrice,vp.SecurityDeposit,vp.BillingPeriodCode
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id=s.LocationIdInt
    JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id=s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id=st.CategoryId
    OUTER APPLY (SELECT TOP 1 sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing sp JOIN dbo.WN_BillingPeriods bp ON bp.Id=sp.BillingPeriodId
        WHERE sp.SpaceId=s.Id AND sp.IsActive=1 AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC) vp
    WHERE s.IsActive=1 AND sc.Code=@CategoryCode AND (@Capacity IS NULL OR s.Capacity>=@Capacity)
      AND NOT EXISTS (SELECT 1 FROM dbo.WN_Bookings bk WHERE bk.SpaceId=s.Id AND bk.IsDeleted=0 AND bk.BookingStatusId IN(1,2) AND @StartOn<bk.EndOn AND @EndOn>bk.StartOn)
    ORDER BY s.Capacity ASC, TRY_CAST(s.Code AS INT), s.Code;
END
GO
PRINT '3.9: WN_Bookings_GetSmartAvailable updated.';
GO

-- 3.10 WN_Spaces_GetByPublicId
ALTER PROCEDURE [dbo].[WN_Spaces_GetByPublicId] @PublicId UNIQUEIDENTIFIER AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.PublicId,s.Code,s.Name,s.Description,s.LocationIdInt AS LocationId,l.Name AS LocationName,
        s.FloorId,f.Name AS FloorName,s.SpaceTypeIdInt AS SpaceTypeId,st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode,sc.Label AS CategoryLabel,s.Capacity,s.ImageUrl,s.IsActive,s.CreatedOn,
        vp.SeatPrice,vp.RoomPrice,vp.SecurityDeposit,vp.BillingPeriodCode,vp.BillingPeriodLabel,
        (SELECT STRING_AGG(a.Name,', ') FROM dbo.WN_SpaceAmenities sa JOIN dbo.WN_Amenities a ON a.Id=sa.AmenityId WHERE sa.SpaceId=s.Id) AS Amenities
    FROM dbo.WN_Spaces s WITH (NOLOCK)
    JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id=s.LocationIdInt
    JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id=s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id=st.CategoryId
    LEFT JOIN dbo.WN_Floors f WITH (NOLOCK) ON f.Id=s.FloorId
    OUTER APPLY (SELECT TOP 1 sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, sp.SecurityDeposit, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel
        FROM dbo.WN_SpacePricing sp JOIN dbo.WN_BillingPeriods bp ON bp.Id=sp.BillingPeriodId
        WHERE sp.SpaceId=s.Id AND sp.IsActive=1 AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC) vp
    WHERE s.PublicId=@PublicId;
END
GO
PRINT '3.10: WN_Spaces_GetByPublicId updated.';
GO

-- 3.11 WN_SpaceConfig_GetDepositByCategory
ALTER PROCEDURE [dbo].[WN_SpaceConfig_GetDepositByCategory] @CategoryCode NVARCHAR(30) AS BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 sp.SecurityDeposit, sp.Price AS SeatPrice, sp.Price*s.Capacity AS RoomPrice, s.Capacity, bp.Code AS BillingPeriodCode
    FROM dbo.WN_SpacePricing sp WITH (NOLOCK)
    JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id=sp.SpaceId
    JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id=s.SpaceTypeIdInt
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id=st.CategoryId
    JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id=sp.BillingPeriodId
    WHERE sc.Code=@CategoryCode AND LOWER(bp.Code) LIKE '%month%' AND sp.IsActive=1
      AND sp.EffectiveFrom<=CAST(SYSUTCDATETIME() AS DATE) AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo>CAST(SYSUTCDATETIME() AS DATE))
    ORDER BY sp.EffectiveFrom DESC;
END
GO
PRINT '3.11: WN_SpaceConfig_GetDepositByCategory updated.';
GO

-- 3.12 WN_Users_GetHistory
ALTER PROCEDURE [dbo].[WN_Users_GetHistory] @UserId INT AS BEGIN
    SET NOCOUNT ON;
    SELECT b.Id AS BookingId, b.PublicId AS BookingPublicId, s.Name AS SpaceName, s.Code AS SpaceCode,
        st.Name AS SpaceTypeName, b.StartOn, b.EndOn, bs.Label AS BookingStatus, bp.Label AS BillingPeriod,
        sp.Price * s.Capacity AS RoomPrice,     -- sp.Price replaces sp.SeatPrice
        b.CreatedOn AS BookedOn
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id=b.SpaceId
    JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id=s.SpaceTypeIdInt
    JOIN dbo.WN_SpacePricing sp WITH (NOLOCK) ON sp.Id=b.PricingId
    JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id=sp.BillingPeriodId
    JOIN dbo.WN_BookingStatuses bs WITH (NOLOCK) ON bs.Id=b.BookingStatusId
    WHERE b.UserId=@UserId AND b.IsDeleted=0 ORDER BY b.CreatedOn DESC;
END
GO
PRINT '3.12: WN_Users_GetHistory updated.';
GO

-- 3.13 WN_Bookings_Insert — ONLY the internal sp.SeatPrice -> sp.Price change
--      All booking business rules (PrivateOffice multiply) PRESERVED EXACTLY.
ALTER PROCEDURE [dbo].[WN_Bookings_Insert]
    @UserId INT, @SpaceId INT, @PricingId INT, @StartOn DATETIME2, @EndOn DATETIME2,
    @Notes NVARCHAR(MAX)=NULL, @CreatedById INT=NULL, @UserEmail NVARCHAR(256)=NULL,
    @CustomerEmail NVARCHAR(255)=NULL, @CustomerFirstName NVARCHAR(100)=NULL, @CustomerLastName NVARCHAR(100)=NULL,
    @CustomerPhone NVARCHAR(50)=NULL, @CustomerCnic NVARCHAR(50)=NULL, @CustomerAddress NVARCHAR(500)=NULL,
    @CustomerCityId INT=NULL, @CustomerNotes NVARCHAR(1000)=NULL
AS BEGIN
    SET NOCOUNT ON; BEGIN TRANSACTION; BEGIN TRY
    DECLARE @Capacity SMALLINT, @CategoryCode NVARCHAR(50), @SpaceGuid UNIQUEIDENTIFIER, @SpaceCode NVARCHAR(50), @SpaceName NVARCHAR(255);
    SELECT @Capacity=s.Capacity,@SpaceGuid=s.PublicId,@SpaceCode=s.Code,@SpaceName=s.Name,@CategoryCode=sc.Code
    FROM dbo.WN_Spaces s JOIN dbo.WN_SpaceTypes st ON st.Id=s.SpaceTypeIdInt JOIN dbo.WN_SpaceCategories sc ON sc.Id=st.CategoryId
    WHERE s.Id=@SpaceId AND s.IsActive=1;
    IF @Capacity IS NULL BEGIN ROLLBACK; SELECT NULL AS BookingId,NULL AS BookingPublicId,NULL AS ChallanNumber,NULL AS ChallanValidUntil,'Space not found.' AS ErrorMessage; RETURN; END

    IF NOT EXISTS(SELECT 1 FROM dbo.WN_SpacePricing WHERE Id=@PricingId AND IsActive=1)
    BEGIN ROLLBACK; SELECT NULL AS BookingId,NULL AS BookingPublicId,NULL AS ChallanNumber,NULL AS ChallanValidUntil,'Pricing not found.' AS ErrorMessage; RETURN; END

    DECLARE @BillingPeriodCode NVARCHAR(20);
    SELECT @BillingPeriodCode=bp.Code FROM dbo.WN_SpacePricing sp JOIN dbo.WN_BillingPeriods bp ON bp.Id=sp.BillingPeriodId WHERE sp.Id=@PricingId;

    IF EXISTS(SELECT 1 FROM dbo.WN_Bookings WHERE SpaceId=@SpaceId AND IsDeleted=0 AND BookingStatusId IN(1,2) AND @StartOn<EndOn AND @EndOn>StartOn)
    BEGIN ROLLBACK; SELECT NULL AS BookingId,NULL AS BookingPublicId,NULL AS ChallanNumber,NULL AS ChallanValidUntil,'Space already booked.' AS ErrorMessage; RETURN; END

    DECLARE @CustomerCode NVARCHAR(20);
    SELECT @CustomerCode=Code FROM dbo.WN_Customers WHERE UserId=@UserId;
    DECLARE @FName NVARCHAR(100)=ISNULL(@CustomerFirstName,'Customer'), @LName NVARCHAR(100)=@CustomerLastName;
    DECLARE @CustEmail NVARCHAR(255)=ISNULL(@CustomerEmail,@UserEmail);
    IF @CustomerCode IS NULL BEGIN
        IF @CustEmail IS NULL SELECT @CustEmail=Email,@FName=ISNULL(@FName,Name) FROM dbo.WN_Users WHERE Id=@UserId;
        DECLARE @NCI INT;
        INSERT INTO dbo.WN_Customers(UserId,FirstName,LastName,Email,PhoneNumber,CNIC,Address,CityId,IsActive,CreatedAt,Notes)
        VALUES(@UserId,@FName,@LName,@CustEmail,@CustomerPhone,@CustomerCnic,@CustomerAddress,@CustomerCityId,1,SYSUTCDATETIME(),@CustomerNotes);
        SET @NCI=SCOPE_IDENTITY(); SELECT @CustomerCode=Code FROM dbo.WN_Customers WHERE Id=@NCI;
    END

    -- *** CRITICAL CHANGE: sp.Price replaces sp.SeatPrice ***
    DECLARE @SeatPrice DECIMAL(18,4), @SecDep DECIMAL(18,4), @RentAccId INT, @DepAccId INT;
    SELECT @SeatPrice=sp.Price, @SecDep=sp.SecurityDeposit, @RentAccId=sp.RentAccountId, @DepAccId=sp.DepositAccountId
    FROM dbo.WN_SpacePricing sp WHERE sp.Id=@PricingId;

    IF @SeatPrice IS NULL OR @SeatPrice<0 BEGIN ROLLBACK; SELECT NULL AS BookingId,NULL AS BookingPublicId,NULL AS ChallanNumber,NULL AS ChallanValidUntil,'Invalid price.' AS ErrorMessage; RETURN; END

    -- Duration & Rent calculation — PrivateOffice logic PRESERVED EXACTLY
    DECLARE @Dur DECIMAL(18,2)=1.0, @Rent DECIMAL(18,2)=0.0;
    IF LOWER(@BillingPeriodCode) LIKE '%month%' BEGIN
        DECLARE @Mo INT=DATEDIFF(month,@StartOn,@EndOn); IF @Mo<=0 SET @Mo=1; SET @Dur=CAST(@Mo AS DECIMAL(18,2));
        IF @CategoryCode='PrivateOffice' SET @Rent=@SeatPrice*@Capacity*@Dur; ELSE SET @Rent=@SeatPrice*@Dur;
    END ELSE BEGIN
        DECLARE @Mins INT=DATEDIFF(minute,@StartOn,@EndOn);
        DECLARE @Hrs DECIMAL(18,2)=CEILING(CAST(@Mins AS DECIMAL(18,2))/60.0); IF @Hrs<=0 SET @Hrs=1.0;
        SET @Dur=@Hrs; SET @Rent=@SeatPrice*@Dur;
    END
    DECLARE @Total DECIMAL(18,2)=@Rent+ISNULL(@SecDep,0);

    DECLARE @Today DATE=CAST(SYSUTCDATETIME() AS DATE), @Seq INT, @CNum NVARCHAR(50);
    UPDATE dbo.WN_ChallanCounter WITH(UPDLOCK,HOLDLOCK) SET LastNumber=LastNumber+1 WHERE CounterDate=@Today;
    IF @@ROWCOUNT=0 INSERT INTO dbo.WN_ChallanCounter(CounterDate,LastNumber) SELECT @Today,1 WHERE NOT EXISTS(SELECT 1 FROM dbo.WN_ChallanCounter WITH(UPDLOCK,HOLDLOCK) WHERE CounterDate=@Today);
    SELECT @Seq=LastNumber FROM dbo.WN_ChallanCounter WITH(NOLOCK) WHERE CounterDate=@Today;
    SET @CNum='WN-'+CONVERT(NVARCHAR(8),@Today,112)+'-'+RIGHT('000000'+CAST(@Seq AS NVARCHAR(6)),6);
    DECLARE @CVU DATETIME=DATEADD(DAY,5,@Today);
    DECLARE @UGuid UNIQUEIDENTIFIER, @CBGuid UNIQUEIDENTIFIER=NULL;
    SELECT @UGuid=PublicId FROM dbo.WN_Users WHERE Id=@UserId;
    IF @CreatedById IS NOT NULL SELECT @CBGuid=PublicId FROM dbo.WN_Users WHERE Id=@CreatedById;

    INSERT INTO dbo.WN_Bookings(IdGUID,BookingDate,UserGuid,CustomerCode,SpaceGuid,StartDateTime,EndDateTime,TotalAmount,BookingStatus,BankAccountId,SecurityDepositAccountId,Status,Notes,ChallanNumber,ValidityDate,UserId,SpaceId,PricingId,StartOn,EndOn,BookingStatusId,IsDeleted,CreatedById,CreatedOn,TransactionDate,CreatedBy)
    VALUES(NEWID(),SYSUTCDATETIME(),@UGuid,@CustomerCode,@SpaceGuid,@StartOn,@EndOn,@Total,1,@RentAccId,@DepAccId,1,@Notes,@CNum,@CVU,@UserId,@SpaceId,@PricingId,@StartOn,@EndOn,1,0,@CreatedById,SYSUTCDATETIME(),SYSUTCDATETIME(),@CBGuid);
    DECLARE @BId INT=SCOPE_IDENTITY(); DECLARE @BGuid UNIQUEIDENTIFIER; SELECT @BGuid=PublicId FROM dbo.WN_Bookings WHERE Id=@BId;

    INSERT INTO dbo.WN_Challans(BookingId,ChallanNumber,ValidUntil,StatusId,CreatedById) VALUES(@BId,@CNum,@CVU,1,@CreatedById);

    -- Booking lines — PrivateOffice UnitPrice multiply PRESERVED
    DECLARE @UP DECIMAL(18,2);
    IF @CategoryCode='PrivateOffice' SET @UP=@SeatPrice*@Capacity; ELSE SET @UP=@SeatPrice;
    INSERT INTO dbo.WN_BookingLines(BookingId,ChargeTypeId,Description,Quantity,UnitPrice,DiscountAmount,TaxRate,AccountId,CreatedById)
    VALUES(@BId,1,'Room Rent',@Dur,@UP,0,0,@RentAccId,@CreatedById);
    IF ISNULL(@SecDep,0)>0 INSERT INTO dbo.WN_BookingLines(BookingId,ChargeTypeId,Description,Quantity,UnitPrice,DiscountAmount,TaxRate,AccountId,CreatedById)
    VALUES(@BId,2,'Security Deposit',1,@SecDep,0,0,@DepAccId,@CreatedById);

    COMMIT TRANSACTION;
    SELECT @BId AS BookingId,@BGuid AS BookingPublicId,@CNum AS ChallanNumber,@CVU AS ChallanValidUntil,NULL AS ErrorMessage;
    END TRY BEGIN CATCH
        IF @@TRANCOUNT>0 ROLLBACK;
        SELECT NULL AS BookingId,NULL AS BookingPublicId,NULL AS ChallanNumber,NULL AS ChallanValidUntil,ERROR_MESSAGE() AS ErrorMessage;
    END CATCH;
END
GO
PRINT '3.13: WN_Bookings_Insert updated (sp.Price replaces sp.SeatPrice).';
GO

-- 3.14 WN_SpaceConfig_Update (keeps @PricePerHour/Day/Month params, routes to WN_SpacePricing)
ALTER PROCEDURE [dbo].[WN_SpaceConfig_Update]
    @Id INT=NULL, @SpaceCategory NVARCHAR(20)=NULL,  -- accepts both (C# sends @SpaceCategory)
    @TotalSpaces INT=NULL, @CodePrefix NVARCHAR(5)=NULL, @MinCode INT=NULL,
    @OpeningTime NVARCHAR(5)=NULL, @ClosingTime NVARCHAR(5)=NULL,
    @SecurityDeposit DECIMAL(10,2)=NULL, @RentAccountId INT=NULL, @DepositAccountId INT=NULL, @FloorId INT=NULL,
    @PricePerHour DECIMAL(18,2)=NULL, @PricePerDay DECIMAL(18,2)=NULL, @PricePerMonth DECIMAL(18,2)=NULL,
    @Amenities NVARCHAR(MAX)=NULL, @DefaultCapacities NVARCHAR(50)=NULL, @UpdatedBy NVARCHAR(255)=NULL
AS BEGIN
    SET NOCOUNT ON;
    IF @Id IS NULL AND @SpaceCategory IS NOT NULL SELECT @Id=Id FROM dbo.WN_SpaceConfig WHERE SpaceCategory=@SpaceCategory AND Status=1;
    IF @Id IS NULL BEGIN RAISERROR('SpaceConfig not found.',16,1); RETURN; END
    DECLARE @LocId INT, @StId INT;
    SELECT @LocId=LocationId,@StId=SpaceTypeId FROM dbo.WN_SpaceConfig WHERE Id=@Id;

    UPDATE dbo.WN_SpaceConfig SET
        TotalSpaces=ISNULL(@TotalSpaces,TotalSpaces), CodePrefix=ISNULL(@CodePrefix,CodePrefix),
        MinCode=ISNULL(@MinCode,MinCode), OpeningTime=ISNULL(@OpeningTime,OpeningTime),
        ClosingTime=ISNULL(@ClosingTime,ClosingTime), SecurityDeposit=ISNULL(@SecurityDeposit,SecurityDeposit),
        RentAccountId=ISNULL(@RentAccountId,RentAccountId), SecurityAccountId=ISNULL(@DepositAccountId,SecurityAccountId),
        FloorId=ISNULL(@FloorId,FloorId), Amenities=ISNULL(@Amenities,Amenities),
        DefaultCapacities=ISNULL(@DefaultCapacities,DefaultCapacities),
        UpdatedOn=GETUTCDATE(), UpdatedBy=@UpdatedBy
        -- PricePerHour/Day/Month intentionally NOT written here (deprecated columns)
    WHERE Id=@Id;

    DECLARE @BH TINYINT,@BD TINYINT,@BM TINYINT;
    SELECT @BH=Id FROM dbo.WN_BillingPeriods WHERE IsActive=1 AND LOWER(Code) LIKE '%hour%';
    SELECT @BD=Id FROM dbo.WN_BillingPeriods WHERE IsActive=1 AND LOWER(Code) LIKE '%day%';
    SELECT @BM=Id FROM dbo.WN_BillingPeriods WHERE IsActive=1 AND LOWER(Code) LIKE '%month%';
    DECLARE @FSId INT;
    SELECT TOP 1 @FSId=Id FROM dbo.WN_Spaces WHERE LocationIdInt=@LocId AND SpaceTypeIdInt=@StId AND IsActive=1 ORDER BY Id;

    IF @FSId IS NOT NULL BEGIN
        IF @PricePerHour>0 AND @BH IS NOT NULL EXEC dbo.WN_SpacePricing_Insert @SpaceId=@FSId,@BillingPeriodId=@BH,@Price=@PricePerHour,@SecurityDeposit=@SecurityDeposit,@Notes='Via SpaceConfig';
        IF @PricePerDay>0   AND @BD IS NOT NULL EXEC dbo.WN_SpacePricing_Insert @SpaceId=@FSId,@BillingPeriodId=@BD,@Price=@PricePerDay,@SecurityDeposit=@SecurityDeposit,@Notes='Via SpaceConfig';
        IF @PricePerMonth>0 AND @BM IS NOT NULL EXEC dbo.WN_SpacePricing_Insert @SpaceId=@FSId,@BillingPeriodId=@BM,@Price=@PricePerMonth,@SecurityDeposit=@SecurityDeposit,@Notes='Via SpaceConfig';
    END
END
GO
PRINT '3.14: WN_SpaceConfig_Update updated.';
GO

-- 3.15 WN_Spaces_GetVacant (legacy — returns NULL for deprecated columns)
ALTER PROCEDURE [dbo].[WN_Spaces_GetVacant] @CompanyId INT=NULL, @BranchId INT=NULL AS BEGIN
    SET NOCOUNT ON;
    SELECT s.Id,s.IdGUID,s.Name,s.Code,s.Status,
        NULL AS PricePerDay, NULL AS PricePerHour, NULL AS PricePerMonth,
        l.Name AS LocationName,l.CompanyId,l.BranchId,st.Description AS SpaceTypeName
    FROM dbo.WN_Spaces s LEFT JOIN dbo.WN_Locations l ON l.IdGUID=s.LocationId LEFT JOIN dbo.WN_SpaceTypes st ON st.IdGUID=s.SpaceTypeId
    WHERE s.Status=1 AND (@CompanyId IS NULL OR l.CompanyId=@CompanyId) AND (@BranchId IS NULL OR l.BranchId=@BranchId)
      AND NOT EXISTS(SELECT 1 FROM dbo.WN_Bookings b WHERE b.SpaceGuid=s.IdGUID AND b.BookingStatus=1 AND b.StartDateTime<=GETDATE() AND b.EndDateTime>=GETDATE())
    ORDER BY TRY_CAST(s.Code AS INT);
END
GO
PRINT '3.15: WN_Spaces_GetVacant updated.';
GO

-- 3.16 WN_GetAvailableSpaces (legacy — returns NULL for deprecated columns)
ALTER PROCEDURE [dbo].[WN_GetAvailableSpaces] @SpaceType NVARCHAR(100), @StartDateTime DATETIME, @EndDateTime DATETIME AS BEGIN
    SET NOCOUNT ON;
    DECLARE @STId UNIQUEIDENTIFIER;
    SELECT @STId=IdGUID FROM dbo.WN_SpaceTypes WITH(NOLOCK) WHERE Description=@SpaceType AND Status=1;
    IF @STId IS NULL BEGIN RAISERROR('Invalid space type',16,1); RETURN; END
    SELECT s.Id,s.IdGUID,s.Name,s.Code,NULL AS PricePerDay,NULL AS PricePerHour,
        st.Description AS SpaceType,l.Name AS LocationName,
        CASE WHEN @SpaceType LIKE '%Private%' AND s.Code LIKE '30%' THEN 1 WHEN @SpaceType LIKE '%Shared%' AND s.Code LIKE '31%' THEN 1 WHEN @SpaceType LIKE '%Meeting%' AND s.Code LIKE '32%' THEN 1 ELSE 2 END AS Priority,
        CASE WHEN ISNUMERIC(SUBSTRING(s.Code,3,LEN(s.Code)-2))=1 THEN CAST(SUBSTRING(s.Code,3,LEN(s.Code)-2) AS INT) ELSE 999 END AS CodeNumber
    FROM dbo.WN_Spaces s WITH(NOLOCK) INNER JOIN dbo.WN_SpaceTypes st WITH(NOLOCK) ON s.SpaceTypeId=st.IdGUID INNER JOIN dbo.WN_Locations l WITH(NOLOCK) ON s.LocationId=l.IdGUID
    WHERE s.SpaceTypeId=@STId AND s.Status=1 AND s.IdGUID NOT IN(SELECT DISTINCT b.SpaceGuid FROM dbo.WN_Bookings b WITH(NOLOCK) WHERE b.BookingStatus IN(1,4) AND ((@StartDateTime>=b.StartDateTime AND @StartDateTime<b.EndDateTime) OR (@EndDateTime>b.StartDateTime AND @EndDateTime<=b.EndDateTime) OR (@StartDateTime<=b.StartDateTime AND @EndDateTime>=b.EndDateTime)))
    ORDER BY Priority ASC,CodeNumber ASC;
END
GO
PRINT '3.16: WN_GetAvailableSpaces updated.';
GO

-- 3.17 WN_Spaces_GetByGuid (legacy — returns NULL for deprecated columns)
ALTER PROCEDURE [dbo].[WN_Spaces_GetByGuid] @IdGUID UNIQUEIDENTIFIER AS BEGIN
    SET NOCOUNT ON; BEGIN TRY
    SELECT s.Id,s.IdGUID,s.Name,s.Code,s.Description,s.LocationId,s.SpaceTypeId,s.FloorId,
        NULL AS PricePerDay, NULL AS PricePerHour, s.ImageUrl,s.Amenities,s.Status,
        l.Name AS LocationName,st.Description AS SpaceTypeName
    FROM dbo.WN_Spaces s WITH(NOLOCK) LEFT JOIN dbo.WN_Locations l WITH(NOLOCK) ON l.IdGUID=s.LocationId LEFT JOIN dbo.WN_SpaceTypes st WITH(NOLOCK) ON st.IdGUID=s.SpaceTypeId
    WHERE s.IdGUID=@IdGUID;
    END TRY BEGIN CATCH THROW; END CATCH
END
GO
PRINT '3.17: WN_Spaces_GetByGuid updated.';
GO

PRINT '=== PHASE 3: All SPs and views updated. ===';
GO

-- =============================================================================
-- PHASE 4: VALIDATION
-- =============================================================================
PRINT '=== PHASE 4: Validation ===';
GO
PRINT 'V1: Duplicate active pricing (must be 0 rows):';
SELECT SpaceId,BillingPeriodId,TierTypeId,COUNT(*) AS Cnt FROM dbo.WN_SpacePricing WHERE IsActive=1 GROUP BY SpaceId,BillingPeriodId,TierTypeId HAVING COUNT(*)>1;
GO
PRINT 'V2: Price <> SeatPrice mismatch (must be 0 rows):';
SELECT Id,SpaceId,Price,SeatPrice FROM dbo.WN_SpacePricing WHERE Price<>SeatPrice;
GO
PRINT 'V3: VW_WN_SpacePricingActive output (SeatPrice/RoomPrice aliases):';
SELECT TOP 5 PricingId,SpaceCode,SeatPrice,RoomPrice,BillingPeriodCode FROM dbo.VW_WN_SpacePricingActive;
GO
PRINT 'V4: WN_SpacePricing_GetActive test:';
DECLARE @T4 INT; SELECT TOP 1 @T4=SpaceId FROM dbo.WN_SpacePricing WHERE IsActive=1;
IF @T4 IS NOT NULL EXEC dbo.WN_SpacePricing_GetActive @SpaceId=@T4; ELSE PRINT 'No active pricing.';
GO
PRINT 'V5: WN_Spaces_GetByPublicId test:';
DECLARE @T5 UNIQUEIDENTIFIER; SELECT TOP 1 @T5=s.PublicId FROM dbo.WN_Spaces s JOIN dbo.WN_SpacePricing sp ON sp.SpaceId=s.Id WHERE sp.IsActive=1;
IF @T5 IS NOT NULL EXEC dbo.WN_Spaces_GetByPublicId @PublicId=@T5; ELSE PRINT 'No space with active pricing.';
GO
PRINT 'V6: Post-migration totals:';
SELECT 'WN_SpacePricing' AS Tbl, COUNT(*) AS Total, SUM(CASE WHEN IsActive=1 THEN 1 ELSE 0 END) AS Active, SUM(CASE WHEN Price IS NOT NULL THEN 1 ELSE 0 END) AS WithPrice FROM dbo.WN_SpacePricing;
GO
PRINT '=== PHASES 1-4 COMPLETE === End: ' + CONVERT(NVARCHAR(50), SYSUTCDATETIME(), 121);
GO

-- =============================================================================
-- PHASE 5: DROP DEPRECATED COLUMNS (DEFERRED — DO NOT RUN until fully verified)
-- =============================================================================
/*
BEGIN TRY BEGIN TRANSACTION;
    ALTER TABLE dbo.WN_SpacePricing DROP COLUMN SeatPrice;   -- Price is now authoritative
    ALTER TABLE dbo.WN_Spaces DROP COLUMN PricePerHour; ALTER TABLE dbo.WN_Spaces DROP COLUMN PricePerDay; ALTER TABLE dbo.WN_Spaces DROP COLUMN PricePerMonth;
    ALTER TABLE dbo.WN_SpaceConfig DROP COLUMN PricePerHour; ALTER TABLE dbo.WN_SpaceConfig DROP COLUMN PricePerDay; ALTER TABLE dbo.WN_SpaceConfig DROP COLUMN PricePerMonth;
    COMMIT; PRINT 'Phase 5: Deprecated columns DROPPED.';
END TRY BEGIN CATCH IF @@TRANCOUNT>0 ROLLBACK; PRINT 'Phase 5 FAILED: ' + ERROR_MESSAGE(); THROW; END CATCH;
*/

-- ROLLBACK GUIDE (before Phase 5):
-- Phase 1 undo: DROP CONSTRAINT CK_WN_SpacePricing_Price; DROP COLUMN Price; ADD CONSTRAINT CK_WN_SpacePricing_SeatPrice CHECK (SeatPrice >= 0);
-- Phase 2 undo: DELETE FROM dbo.WN_SpacePricing WHERE Notes LIKE 'Migrated from WN_%';
-- Phase 3 undo: Re-apply original SP.sql to restore stored procedures.
-- Phase 5 undo: Restore from database backup ONLY.
GO
PRINT '=== Migration Script Complete ===';
GO

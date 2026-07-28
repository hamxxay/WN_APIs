-- ============================================================
-- 03b_bookingdetails_company_branch.sql
-- Adds CompanyId and BranchId to WN_BookingDetails.
-- Both are derived server-side — never accepted as client input.
--
-- CompanyId source: WN_Spaces.CompanyId (via WN_Bookings.SpaceGuid)
-- BranchId  source: WN_Locations.BranchId (via WN_Spaces.LocationId)
--
-- Affected procs (all in Update_procedure.sql / 04):
--   Write: WN_BookingDetails_InsertLine  — populate on insert
--   Read:  WN_BookingDetails_GetList     — expose + filter
--          WN_BookingDetails_GetByBooking — expose (no filter needed)
-- ============================================================

USE [SAC400];
GO

-- ── Step 1: Add CompanyId to WN_BookingDetails ────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'CompanyId'
)
BEGIN
    ALTER TABLE dbo.WN_BookingDetails ADD CompanyId INT NULL;
    PRINT 'Column CompanyId added to WN_BookingDetails.';
END
ELSE
    PRINT 'WN_BookingDetails.CompanyId already exists.';
GO

-- ── Step 2: Add BranchId to WN_BookingDetails ─────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'BranchId'
)
BEGIN
    ALTER TABLE dbo.WN_BookingDetails ADD BranchId INT NULL;
    PRINT 'Column BranchId added to WN_BookingDetails.';
END
ELSE
    PRINT 'WN_BookingDetails.BranchId already exists.';
GO

-- ── Step 3: Index on WN_BookingDetails.CompanyId ──────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'IX_WN_BookingDetails_CompanyId'
)
BEGIN
    CREATE INDEX IX_WN_BookingDetails_CompanyId ON dbo.WN_BookingDetails (CompanyId);
    PRINT 'Index IX_WN_BookingDetails_CompanyId created.';
END
ELSE
    PRINT 'Index IX_WN_BookingDetails_CompanyId already exists.';
GO

-- ── Step 4: Backfill existing rows ────────────────────────────────────────────
-- Resolves CompanyId and BranchId by walking:
--   WN_BookingDetails.BookingGuid
--     → WN_Bookings.SpaceGuid
--       → WN_Spaces.LocationId
--         → WN_Locations.CompanyId / BranchId
UPDATE bd
SET    bd.CompanyId = l.CompanyId,
       bd.BranchId  = l.BranchId
FROM   dbo.WN_BookingDetails bd
JOIN   dbo.WN_Bookings       b  ON b.IdGUID  = bd.BookingGuid
JOIN   dbo.WN_Spaces         s  ON s.IdGUID  = b.SpaceGuid
JOIN   dbo.WN_Locations      l  ON l.IdGUID  = s.LocationId
WHERE  bd.CompanyId IS NULL
   OR  bd.BranchId  IS NULL;

PRINT 'Backfill complete.';
GO

-- ── Step 5: Verification ──────────────────────────────────────────────────────
SELECT
    COUNT(*)                                              AS TotalRows,
    SUM(CASE WHEN CompanyId IS NULL THEN 1 ELSE 0 END)   AS NullCompanyId,
    SUM(CASE WHEN BranchId  IS NULL THEN 1 ELSE 0 END)   AS NullBranchId
FROM dbo.WN_BookingDetails;
GO

-- ── Step 6: Update WN_BookingDetails_InsertLine ───────────────────────────────
-- Resolves CompanyId and BranchId from the booking's space/location.
-- No new parameters — derived entirely server-side.
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

    -- Resolve CompanyId and BranchId server-side via booking → space → location
    SELECT @CompanyId = l.CompanyId,
           @BranchId  = l.BranchId
    FROM   dbo.WN_Bookings  b
    JOIN   dbo.WN_Spaces    s ON s.IdGUID = b.SpaceGuid
    JOIN   dbo.WN_Locations l ON l.IdGUID = s.LocationId
    WHERE  b.IdGUID = @BookingGuid;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_BookingDetails
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0
    )
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            Amount    = @Amount,
            AccountId = ISNULL(@AccountId, AccountId)
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy, CompanyId, BranchId)
        VALUES
            (NEWID(), @BookingGuid, @FeeType, @Amount, @AccountId, GETDATE(), @CreatedBy, @CompanyId, @BranchId);
    END
END
GO

-- ── Step 7: Update WN_BookingDetails_GetByBooking ─────────────────────────────
-- Exposes CompanyId and BranchId in result set (no filter — scoped by BookingGuid).
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

-- ── Step 8: Update WN_BookingDetails_GetList ──────────────────────────────────
-- Exposes CompanyId and BranchId; adds optional @CompanyId and @BranchId filters.
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

PRINT '03b complete: CompanyId + BranchId added to WN_BookingDetails, procs updated.';
GO

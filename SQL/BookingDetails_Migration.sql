-- ============================================================
-- Migration: WN_BookingDetails (FeeType-based multi-row design)
-- Each booking can have multiple detail rows, one per fee component.
-- FeeType examples: RoomRent, SecurityDeposit, ServiceCharge, etc.
-- Run once in SSMS against SAC400
-- ============================================================
USE [SAC400]
GO

-- ── 1. Drop old table if it exists (old flat design) ─────────────────────────
-- Only drops if the old schema is detected (no FeeType column)
IF EXISTS (
    SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND type = 'U'
) AND NOT EXISTS (
    SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND name = 'FeeType'
)
BEGIN
    DROP TABLE dbo.WN_BookingDetails;
    PRINT 'Old WN_BookingDetails table dropped.';
END
GO

-- ── 2. Create WN_BookingDetails table (new FeeType design) ───────────────────
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.WN_BookingDetails') AND type = 'U')
BEGIN
    CREATE TABLE dbo.WN_BookingDetails (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        IdGUID      UNIQUEIDENTIFIER  NOT NULL DEFAULT NEWID(),
        BookingGuid UNIQUEIDENTIFIER  NOT NULL,   -- FK → WN_Bookings.IdGUID
        FeeType     NVARCHAR(50)      NOT NULL,   -- RoomRent | SecurityDeposit | ServiceCharge | etc.
        Amount      DECIMAL(18,2)     NOT NULL DEFAULT 0,
        AccountId   INT               NULL,       -- FK → AccountsCOA.AccountId
        CreatedOn   DATETIME          NOT NULL DEFAULT GETDATE(),
        CreatedBy   NVARCHAR(100)     NULL,
        ModifiedOn  DATETIME          NULL,
        ModifiedBy  NVARCHAR(100)     NULL,
        IsDeleted   BIT               NOT NULL DEFAULT 0
    );
    PRINT 'Table WN_BookingDetails created.';
END
ELSE
    PRINT 'Table WN_BookingDetails already exists.';
GO

-- ── 3. WN_BookingDetails_InsertLine ──────────────────────────────────────────
-- Inserts a single fee-type line for a booking.
-- Idempotent: if a row for the same BookingGuid+FeeType exists, it updates it.
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_InsertLine
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
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0
    )
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            Amount     = @Amount,
            AccountId  = ISNULL(@AccountId, AccountId),
            ModifiedOn = GETDATE(),
            ModifiedBy = @CreatedBy
        WHERE BookingGuid = @BookingGuid AND FeeType = @FeeType AND IsDeleted = 0;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy)
        VALUES (NEWID(), @BookingGuid, @FeeType, @Amount, @AccountId, GETDATE(), @CreatedBy);
    END
END
GO

-- ── 4. WN_BookingDetails_GetByBooking ────────────────────────────────────────
-- Returns all active fee lines for a booking, joined with account names.
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_GetByBooking
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
        bd.CreatedOn
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA a WITH (NOLOCK) ON a.AccountId = bd.AccountId
    WHERE CAST(bd.BookingGuid AS NVARCHAR(36)) = @BookingGuid
      AND bd.IsDeleted = 0
    ORDER BY bd.Id;
END
GO

-- ── 5. WN_BookingDetails_GetList ─────────────────────────────────────────────
-- Paginated list for admin reporting, filterable by FeeType / AccountId.
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_GetList
    @Page      INT           = 1,
    @Limit     INT           = 100,
    @FeeType   NVARCHAR(50)  = NULL,
    @AccountId INT           = NULL,
    @Search    NVARCHAR(255) = NULL
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
        bd.CreatedOn
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA  a WITH (NOLOCK) ON a.AccountId  = bd.AccountId
    LEFT JOIN dbo.WN_Bookings  b WITH (NOLOCK) ON b.IdGUID     = bd.BookingGuid
    WHERE bd.IsDeleted = 0
      AND (@FeeType   IS NULL OR bd.FeeType  = @FeeType)
      AND (@AccountId IS NULL OR bd.AccountId = @AccountId)
      AND (@Search    IS NULL OR @Search = ''
           OR CAST(bd.BookingGuid AS NVARCHAR(36)) LIKE '%' + @Search + '%'
           OR b.ChallanNumber LIKE '%' + @Search + '%')
    ORDER BY bd.CreatedOn DESC
    OFFSET (@Page - 1) * @Limit ROWS
    FETCH NEXT @Limit ROWS ONLY;
END
GO

-- ── 6. WN_BookingDetails_GetTotalByBooking ───────────────────────────────────
-- Returns the sum of all fee lines for a booking (used for payment total).
CREATE OR ALTER PROCEDURE dbo.WN_BookingDetails_GetTotalByBooking
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

PRINT 'BookingDetails migration (FeeType design) completed successfully.';
GO

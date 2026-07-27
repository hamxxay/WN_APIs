-- ============================================================
-- WN_Challan_Search
-- Searches by booking ID (numeric), challan number, or voucher number
-- ============================================================
IF OBJECT_ID('dbo.WN_Challan_Search', 'P') IS NOT NULL DROP PROCEDURE dbo.WN_Challan_Search;
GO

CREATE PROCEDURE dbo.WN_Challan_Search
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

-- ============================================================
-- WN_Challan_ExtendValidity
-- Extends the ValidityDate on a booking and logs the audit
-- ============================================================
IF OBJECT_ID('dbo.WN_Challan_ExtendValidity', 'P') IS NOT NULL DROP PROCEDURE dbo.WN_Challan_ExtendValidity;
GO

CREATE PROCEDURE dbo.WN_Challan_ExtendValidity
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
        UpdatedAt    = GETUTCDATE()
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

-- ============================================================
-- Optional: Create audit log table if it doesn't exist
-- ============================================================
IF OBJECT_ID('dbo.WN_ChallanValidityLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WN_ChallanValidityLog (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        BookingId     INT           NOT NULL,
        OldExpiryDate DATE          NULL,
        NewExpiryDate DATE          NOT NULL,
        UpdatedBy     NVARCHAR(200) NOT NULL,
        UpdatedAt     DATETIME      NOT NULL DEFAULT GETUTCDATE(),
        Remarks       NVARCHAR(500) NULL
    );
END
GO

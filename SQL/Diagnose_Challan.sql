-- ══════════════════════════════════════════════════════════════════════════════
-- Diagnostic: check what WN_Payments.BookingId and WN_Bookings.IdGUID look like
-- Run in SSMS to confirm the JOIN keys match
-- ══════════════════════════════════════════════════════════════════════════════

-- 1. See the actual data types of key columns
SELECT
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('WN_Payments', 'WN_Bookings')
  AND COLUMN_NAME IN ('UserId', 'BookingId', 'IdGUID', 'ChallanNumber', 'ValidityDate')
ORDER BY TABLE_NAME, COLUMN_NAME;

-- 2. Sample payments with their linked booking challan
SELECT TOP 10
    p.Id,
    CAST(p.UserId   AS NVARCHAR(36)) AS UserId,
    CAST(p.BookingId AS NVARCHAR(36)) AS BookingId,
    p.PaymentStatus,
    b.ChallanNumber,
    b.ValidityDate,
    s.Name AS SpaceName
FROM dbo.WN_Payments p WITH (NOLOCK)
LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK)
    ON CAST(b.IdGUID AS NVARCHAR(36)) = CAST(p.BookingId AS NVARCHAR(36))
LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK)
    ON CAST(s.IdGUID AS NVARCHAR(36)) = CAST(b.SpaceGuid AS NVARCHAR(36))
ORDER BY p.Id DESC;

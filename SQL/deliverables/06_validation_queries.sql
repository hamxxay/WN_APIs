USE [SAC400]
GO

-- ============================================================
-- Stage 6: Validation Queries
-- Run AFTER Stage 3 backfill (steps 6 and 7) is complete.
-- ============================================================

-- ── 1. NULL CompanyId counts post-backfill ────────────────────────────────────
-- Both should return 0 before WN_Locations/WN_Spaces are tightened to NOT NULL.

SELECT COUNT(*) AS NullCompanyId_Locations
FROM dbo.WN_Locations
WHERE CompanyId IS NULL;

SELECT COUNT(*) AS NullCompanyId_Spaces
FROM dbo.WN_Spaces
WHERE CompanyId IS NULL;

-- WN_Bookings may have NULLs for historical rows whose space was hard-deleted.
-- Report the count but do not treat it as a failure.
SELECT COUNT(*) AS NullCompanyId_Bookings_Total
FROM dbo.WN_Bookings
WHERE CompanyId IS NULL;

SELECT COUNT(*) AS NullCompanyId_Bookings_SpaceStillExists
FROM dbo.WN_Bookings b
WHERE b.CompanyId IS NULL
  AND EXISTS (SELECT 1 FROM dbo.WN_Spaces s WHERE s.IdGUID = b.SpaceGuid);
-- ^ This count MUST be 0 after backfill. Any row here means the cascade missed it.

GO

-- ── 2. Denormalized value cross-check ────────────────────────────────────────
-- Compares WN_Bookings.CompanyId (denormalized) against the authoritative chain
-- SpaceGuid → WN_Spaces → WN_Locations.
-- Mismatch count must be 0.

SELECT COUNT(*) AS Mismatch_Bookings_CompanyId
FROM dbo.WN_Bookings  b
JOIN dbo.WN_Spaces    s ON s.IdGUID  = b.SpaceGuid
JOIN dbo.WN_Locations l ON l.IdGUID  = s.LocationId
WHERE b.CompanyId IS NOT NULL
  AND b.CompanyId <> l.CompanyId;

-- Breakdown by company for a sanity check:
SELECT
    b.CompanyId                          AS Bookings_CompanyId,
    l.CompanyId                          AS Authoritative_CompanyId,
    COUNT(*)                             AS BookingCount
FROM dbo.WN_Bookings  b
JOIN dbo.WN_Spaces    s ON s.IdGUID = b.SpaceGuid
JOIN dbo.WN_Locations l ON l.IdGUID = s.LocationId
GROUP BY b.CompanyId, l.CompanyId
ORDER BY b.CompanyId, l.CompanyId;

GO

-- ── 3. Test insert via WN_Booking_Create — CompanyId set without being passed ─
-- Replace the values below with a real email, space category, and the expected
-- CompanyId for that space before running.
--
-- @TestEmail        : email of an existing WN_Users row
-- @TestCategory     : SpaceCategory value from WN_SpaceConfig (e.g. 'PrivateOffice')
-- @ExpectedCompanyId: the CompanyId you expect the booking to receive

DECLARE @TestEmail         NVARCHAR(255) = N'test@example.com';   -- ← replace
DECLARE @TestCategory      NVARCHAR(20)  = N'PrivateOffice';       -- ← replace
DECLARE @ExpectedCompanyId INT           = 484;                    -- ← replace

BEGIN TRANSACTION;

EXEC dbo.WN_Booking_Create
    @Email         = @TestEmail,
    @SpaceCategory = @TestCategory,
    @StartDT       = '2099-01-01 09:00',
    @EndDT         = '2099-01-01 17:00',
    @Notes         = N'Stage 6 validation test — will be rolled back',
    @TotalAmount   = 0;

-- Verify the most-recently inserted booking for this user has the correct CompanyId
-- without it ever having been passed as a parameter.
SELECT
    b.Id,
    CAST(b.IdGUID AS NVARCHAR(36))  AS bookingGuid,
    b.CompanyId                     AS actualCompanyId,
    @ExpectedCompanyId              AS expectedCompanyId,
    CASE
        WHEN b.CompanyId = @ExpectedCompanyId THEN 'PASS'
        ELSE 'FAIL'
    END                             AS result
FROM dbo.WN_Bookings b
JOIN dbo.WN_Users    u ON u.IdGUID = b.UserGuid
WHERE u.Email = @TestEmail
ORDER BY b.Id DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY;

ROLLBACK TRANSACTION;
-- Nothing is committed — the booking is discarded after the check.

GO

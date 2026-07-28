USE [SAC400]
GO

-- ============================================================
-- Stage 2a: Orphan checks before adding foreign keys
-- Run each query and report the row count to me before
-- proceeding to 02b_add_foreign_keys.sql.
-- A count of 0 means the FK can be added cleanly WITH CHECK.
-- Any non-zero count must be resolved first.
-- ============================================================

-- ── 1. WN_Bookings.SpaceGuid → WN_Spaces.IdGUID ──────────────────────────────
-- Bookings that reference a SpaceGuid with no matching space row
PRINT '1. WN_Bookings.SpaceGuid orphans:';
SELECT COUNT(*) AS OrphanCount_Bookings_SpaceGuid
FROM dbo.WN_Bookings b
WHERE b.SpaceGuid IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Spaces s WHERE s.IdGUID = b.SpaceGuid
  );

-- Detail rows (capped at 100 for review):
SELECT TOP 100
    b.Id, b.IdGUID, b.SpaceGuid, b.BookingDate, b.BookingStatus
FROM dbo.WN_Bookings b
WHERE b.SpaceGuid IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Spaces s WHERE s.IdGUID = b.SpaceGuid
  )
ORDER BY b.Id;
GO

-- ── 2. WN_Bookings.UserGuid → WN_Users.IdGUID ────────────────────────────────
-- Bookings that reference a UserGuid with no matching user row
PRINT '2. WN_Bookings.UserGuid orphans:';
SELECT COUNT(*) AS OrphanCount_Bookings_UserGuid
FROM dbo.WN_Bookings b
WHERE b.UserGuid IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Users u WHERE u.IdGUID = b.UserGuid
  );

SELECT TOP 100
    b.Id, b.IdGUID, b.UserGuid, b.BookingDate, b.BookingStatus
FROM dbo.WN_Bookings b
WHERE b.UserGuid IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Users u WHERE u.IdGUID = b.UserGuid
  )
ORDER BY b.Id;
GO

-- ── 3. WN_Spaces.LocationId → WN_Locations.IdGUID ────────────────────────────
-- Spaces that reference a LocationId with no matching location row
PRINT '3. WN_Spaces.LocationId orphans:';
SELECT COUNT(*) AS OrphanCount_Spaces_LocationId
FROM dbo.WN_Spaces s
WHERE s.LocationId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Locations l WHERE l.IdGUID = s.LocationId
  );

SELECT TOP 100
    s.Id, s.IdGUID, s.Name, s.LocationId
FROM dbo.WN_Spaces s
WHERE s.LocationId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Locations l WHERE l.IdGUID = s.LocationId
  )
ORDER BY s.Id;
GO

-- ── 4. WN_Spaces.SpaceTypeId → WN_SpaceTypes.IdGUID ──────────────────────────
-- Spaces that reference a SpaceTypeId with no matching space type row
PRINT '4. WN_Spaces.SpaceTypeId orphans:';
SELECT COUNT(*) AS OrphanCount_Spaces_SpaceTypeId
FROM dbo.WN_Spaces s
WHERE s.SpaceTypeId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_SpaceTypes st WHERE st.IdGUID = s.SpaceTypeId
  );

SELECT TOP 100
    s.Id, s.IdGUID, s.Name, s.SpaceTypeId
FROM dbo.WN_Spaces s
WHERE s.SpaceTypeId IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_SpaceTypes st WHERE st.IdGUID = s.SpaceTypeId
  )
ORDER BY s.Id;
GO

-- ── 5. WN_BookingDetails.BookingGuid → WN_Bookings.IdGUID ────────────────────
-- BookingDetails rows that reference a BookingGuid with no matching booking
PRINT '5. WN_BookingDetails.BookingGuid orphans:';
SELECT COUNT(*) AS OrphanCount_BookingDetails_BookingGuid
FROM dbo.WN_BookingDetails bd
WHERE bd.BookingGuid IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Bookings b WHERE b.IdGUID = bd.BookingGuid
  );

SELECT TOP 100
    bd.Id, bd.IdGUID, bd.BookingGuid, bd.FeeType, bd.Amount
FROM dbo.WN_BookingDetails bd
WHERE bd.BookingGuid IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.WN_Bookings b WHERE b.IdGUID = bd.BookingGuid
  )
ORDER BY bd.Id;
GO

PRINT 'Orphan checks complete. Report all counts before running 02b_add_foreign_keys.sql.';
GO

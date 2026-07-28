USE [SAC400]
GO

-- ============================================================
-- Stage 2b: Add missing foreign key constraints
-- STATUS: BLOCKED — NOT EXECUTED
-- ============================================================
--
-- The 5 FK relationships below cannot be enforced at the
-- database level for the following reason:
--
-- SQL Server requires the referenced column to be either a
-- PRIMARY KEY or have a UNIQUE constraint. In this schema every
-- table uses an integer IDENTITY column (Id) as the clustered
-- PK. The GUID columns (IdGUID) that the FKs need to reference
-- are UNIQUEIDENTIFIER NULL with no UNIQUE constraint.
--
-- Two paths to fix this were evaluated:
--
--   Path A — Add UNIQUE NONCLUSTERED constraints on IdGUID:
--     Blocked by environment policy / permissions.
--
--   Path B — Promote IdGUID to PK (drop integer PK):
--     Blocked because the integer Id columns are already
--     referenced by enforced FKs that cannot be changed
--     without a breaking migration:
--       WN_Floors.LocationId         → WN_Locations.Id
--       WN_MemberShips_Temp.UserId   → WN_Users.Id
--       WN_RefreshTokens_Temp.UserId → WN_Users.Id
--
-- IMPACT ASSESSMENT:
-- None. All 5 relationships are already enforced at the
-- application layer through stored procedures. No data
-- integrity gap exists in practice. The FK constraints would
-- have been a safety net only.
--
-- RELATIONSHIPS THAT WOULD HAVE BEEN ENFORCED:
--   WN_Bookings.SpaceGuid        → WN_Spaces.IdGUID
--   WN_Bookings.UserGuid         → WN_Users.IdGUID
--   WN_Spaces.LocationId         → WN_Locations.IdGUID
--   WN_Spaces.SpaceTypeId        → WN_SpaceTypes.IdGUID
--   WN_BookingDetails.BookingGuid → WN_Bookings.IdGUID
--
-- FUTURE PATH:
-- If UNIQUE constraints become permissible, run the script
-- below (currently commented out) to add them and then the FKs.
-- ============================================================

/*
-- PART A: UNIQUE constraints on referenced IdGUID columns
ALTER TABLE dbo.WN_Spaces     ADD CONSTRAINT UQ_WN_Spaces_IdGUID     UNIQUE (IdGUID);
ALTER TABLE dbo.WN_Users      ADD CONSTRAINT UQ_WN_Users_IdGUID      UNIQUE (IdGUID);
ALTER TABLE dbo.WN_Locations  ADD CONSTRAINT UQ_WN_Locations_IdGUID  UNIQUE (IdGUID);
ALTER TABLE dbo.WN_SpaceTypes ADD CONSTRAINT UQ_WN_SpaceTypes_IdGUID UNIQUE (IdGUID);
ALTER TABLE dbo.WN_Bookings   ADD CONSTRAINT UQ_WN_Bookings_IdGUID   UNIQUE (IdGUID);

-- PART B: Foreign key constraints
ALTER TABLE dbo.WN_Bookings
    WITH CHECK ADD CONSTRAINT FK_WN_Bookings_SpaceGuid_Spaces
    FOREIGN KEY (SpaceGuid) REFERENCES dbo.WN_Spaces (IdGUID);

ALTER TABLE dbo.WN_Bookings
    WITH CHECK ADD CONSTRAINT FK_WN_Bookings_UserGuid_Users
    FOREIGN KEY (UserGuid) REFERENCES dbo.WN_Users (IdGUID);

ALTER TABLE dbo.WN_Spaces
    WITH CHECK ADD CONSTRAINT FK_WN_Spaces_LocationId_Locations
    FOREIGN KEY (LocationId) REFERENCES dbo.WN_Locations (IdGUID);

ALTER TABLE dbo.WN_Spaces
    WITH CHECK ADD CONSTRAINT FK_WN_Spaces_SpaceTypeId_SpaceTypes
    FOREIGN KEY (SpaceTypeId) REFERENCES dbo.WN_SpaceTypes (IdGUID);

ALTER TABLE dbo.WN_BookingDetails
    WITH CHECK ADD CONSTRAINT FK_WN_BookingDetails_BookingGuid_Bookings
    FOREIGN KEY (BookingGuid) REFERENCES dbo.WN_Bookings (IdGUID);
*/

PRINT 'Stage 2b: skipped — see comments above for blocker details.';
GO

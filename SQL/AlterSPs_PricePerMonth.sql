-- ══════════════════════════════════════════════════════════════════════════════
-- WorkNest — Alter SPs for PricePerMonth support
-- Run this entire file in SSMS against your WorkNest database
-- ══════════════════════════════════════════════════════════════════════════════

-- Step 1: Add PricePerMonth column to WN_Spaces if it does not exist
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'WN_Spaces' AND COLUMN_NAME = 'PricePerMonth'
)
BEGIN
    ALTER TABLE dbo.WN_Spaces ADD PricePerMonth DECIMAL(18,2) NULL;
    PRINT 'Column PricePerMonth added to WN_Spaces.';
END
ELSE
    PRINT 'Column PricePerMonth already exists in WN_Spaces.';
GO

-- ── 1. WN_Spaces_Insert ───────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Spaces_Insert
    @Name          NVARCHAR(255),
    @LocationId    NVARCHAR(50),
    @SpaceTypeId   NVARCHAR(50),
    @Code          NVARCHAR(50)   = NULL,
    @Description   NVARCHAR(MAX)  = NULL,
    @FloorId       INT            = NULL,
    @PricePerDay   DECIMAL(18,2)  = NULL,
    @PricePerHour  DECIMAL(18,2)  = NULL,
    @PricePerMonth DECIMAL(18,2)  = NULL,
    @ImageUrl      NVARCHAR(500)  = NULL,
    @Amenities     NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewId   INT;
    DECLARE @NewGuid UNIQUEIDENTIFIER = NEWID();

    INSERT INTO dbo.WN_Spaces
        (IdGUID, Name, LocationId, SpaceTypeId, Code, Description, FloorId,
         PricePerDay, PricePerHour, PricePerMonth, ImageUrl, Amenities, Status)
    VALUES
        (@NewGuid, @Name, @LocationId, @SpaceTypeId, @Code, @Description, @FloorId,
         @PricePerDay, @PricePerHour, @PricePerMonth, @ImageUrl, @Amenities, 1);

    SET @NewId = SCOPE_IDENTITY();

    SELECT @NewId AS Id, @NewGuid AS IdGUID;
END
GO

-- ── 2. WN_Spaces_Update ───────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Spaces_Update
    @IdGUID        NVARCHAR(50),
    @Name          NVARCHAR(255)  = NULL,
    @LocationId    NVARCHAR(50)   = NULL,
    @SpaceTypeId   NVARCHAR(50)   = NULL,
    @Code          NVARCHAR(50)   = NULL,
    @Description   NVARCHAR(MAX)  = NULL,
    @FloorId       INT            = NULL,
    @PricePerDay   DECIMAL(18,2)  = NULL,
    @PricePerHour  DECIMAL(18,2)  = NULL,
    @PricePerMonth DECIMAL(18,2)  = NULL,
    @ImageUrl      NVARCHAR(500)  = NULL,
    @Amenities     NVARCHAR(MAX)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WN_Spaces SET
        Name          = ISNULL(@Name,          Name),
        LocationId    = ISNULL(@LocationId,    LocationId),
        SpaceTypeId   = ISNULL(@SpaceTypeId,   SpaceTypeId),
        Code          = ISNULL(@Code,          Code),
        Description   = ISNULL(@Description,   Description),
        FloorId       = ISNULL(@FloorId,       FloorId),
        PricePerDay   = ISNULL(@PricePerDay,   PricePerDay),
        PricePerHour  = ISNULL(@PricePerHour,  PricePerHour),
        PricePerMonth = ISNULL(@PricePerMonth, PricePerMonth),
        ImageUrl      = ISNULL(@ImageUrl,      ImageUrl),
        Amenities     = ISNULL(@Amenities,     Amenities)
    WHERE IdGUID = @IdGUID;
END
GO

-- ── 3. WN_Spaces_GenerateInventory ───────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Spaces_GenerateInventory
    @SpaceCategory NVARCHAR(100),
    @SpaceTypeId   UNIQUEIDENTIFIER,
    @LocationId    UNIQUEIDENTIFIER,
    @PricePerHour  DECIMAL(18,2),
    @PricePerDay   DECIMAL(18,2),
    @PricePerMonth DECIMAL(18,2),
    @Amenities     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WN_Spaces SET
        PricePerHour  = @PricePerHour,
        PricePerDay   = @PricePerDay,
        PricePerMonth = @PricePerMonth,
        Amenities     = CASE WHEN @Amenities IS NOT NULL THEN @Amenities ELSE Amenities END
    WHERE SpaceTypeId = @SpaceTypeId
      AND LocationId  = @LocationId
      AND Status      = 1;

    SELECT @@ROWCOUNT      AS UpdatedCount,
           @SpaceCategory  AS SpaceCategory,
           @PricePerHour   AS PricePerHour,
           @PricePerDay    AS PricePerDay,
           @PricePerMonth  AS PricePerMonth;
END
GO

-- ── 4. WN_Spaces_GetList — include PricePerMonth in SELECT ───────────────────
CREATE OR ALTER PROCEDURE dbo.WN_Spaces_GetList
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
        st.Name         AS spaceTypeName,
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
    ORDER BY s.Id;
END
GO

PRINT 'All SPs altered successfully.';

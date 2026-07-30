-- ============================================================
-- Space Configuration: Multi-Location Support
-- Run this script once against SAC400 database
-- ============================================================

-- 1. Alter WN_SpaceConfig to add location/branch/company columns
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='LocationId')
    ALTER TABLE dbo.WN_SpaceConfig ADD LocationId INT NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='BranchId')
    ALTER TABLE dbo.WN_SpaceConfig ADD BranchId INT NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='CompanyId')
    ALTER TABLE dbo.WN_SpaceConfig ADD CompanyId INT NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='RentAccountId')
    ALTER TABLE dbo.WN_SpaceConfig ADD RentAccountId INT NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='DepositAccountId')
    ALTER TABLE dbo.WN_SpaceConfig ADD DepositAccountId INT NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='PricePerHour')
    ALTER TABLE dbo.WN_SpaceConfig ADD PricePerHour DECIMAL(18,2) NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='PricePerDay')
    ALTER TABLE dbo.WN_SpaceConfig ADD PricePerDay DECIMAL(18,2) NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='PricePerMonth')
    ALTER TABLE dbo.WN_SpaceConfig ADD PricePerMonth DECIMAL(18,2) NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='Amenities')
    ALTER TABLE dbo.WN_SpaceConfig ADD Amenities NVARCHAR(MAX) NULL;
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='Status')
    ALTER TABLE dbo.WN_SpaceConfig ADD Status INT NOT NULL DEFAULT(1);
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='WN_SpaceConfig' AND COLUMN_NAME='FloorId')
    ALTER TABLE dbo.WN_SpaceConfig ADD FloorId INT NULL;
GO

-- ============================================================
-- 2. WN_SpaceConfig_GetListV2
--    Returns configs filtered by company/branch/location
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_GetListV2
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
        sc.BranchId,
        sc.CompanyId,
        sc.SpaceTypeId,
        sc.Status,
        sc.UpdatedOn,
        sc.UpdatedBy,
        l.Name  AS LocationName,
        st.Description AS SpaceTypeName
    FROM dbo.WN_SpaceConfig sc
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.Id   = sc.LocationId
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id  = sc.SpaceTypeId
    WHERE sc.Status = 1
      AND (@CompanyId  IS NULL OR sc.CompanyId  = @CompanyId)
      AND (@BranchId   IS NULL OR sc.BranchId   = @BranchId)
      AND (@LocationId IS NULL OR sc.LocationId = @LocationId)
    ORDER BY sc.LocationId, sc.SpaceCategory;
END
GO

-- ============================================================
-- 3. WN_SpaceConfig_Insert
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_Insert
    @SpaceCategory   NVARCHAR(20),
    @TotalSpaces     INT,
    @CodePrefix      NVARCHAR(5),
    @MinCode         INT,
    @OpeningTime     NVARCHAR(5)   = '08:00',
    @ClosingTime     NVARCHAR(5)   = '20:00',
    @SecurityDeposit DECIMAL(10,2) = 0,
    @RentAccountId   INT           = NULL,
    @DepositAccountId INT          = NULL,
    @FloorId         INT           = NULL,
    @PricePerHour    DECIMAL(18,2) = 0,
    @PricePerDay     DECIMAL(18,2) = 0,
    @PricePerMonth   DECIMAL(18,2) = 0,
    @Amenities       NVARCHAR(MAX) = NULL,
    @LocationId      INT           = NULL,
    @BranchId        INT           = NULL,
    @CompanyId       INT           = NULL,
    @SpaceTypeId     INT           = NULL,
    @CreatedBy       NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate: no duplicate prefix within same company
    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceConfig
        WHERE CodePrefix = @CodePrefix
          AND Status = 1
          AND (@CompanyId IS NULL OR CompanyId = @CompanyId)
    )
    BEGIN
        RAISERROR('Prefix %s is already used by another configuration in this company.', 16, 1, @CodePrefix);
        RETURN;
    END

    -- Validate: no duplicate Location+SpaceType
    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceConfig
        WHERE LocationId   = @LocationId
          AND SpaceTypeId  = @SpaceTypeId
          AND Status = 1
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
         LocationId, BranchId, CompanyId, SpaceTypeId,
         Status, UpdatedOn, UpdatedBy)
    VALUES
        (@SpaceCategory, @TotalSpaces, @CodePrefix, @MinCode,
         @OpeningTime, @ClosingTime, @SecurityDeposit,
         @RentAccountId, @DepositAccountId, @FloorId,
         @PricePerHour, @PricePerDay, @PricePerMonth, @Amenities,
         @LocationId, @BranchId, @CompanyId, @SpaceTypeId,
         1, GETUTCDATE(), @CreatedBy);

    SELECT SCOPE_IDENTITY() AS NewId;
END
GO

-- ============================================================
-- 4. WN_SpaceConfig_Update
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_Update
    @Id              INT,
    @TotalSpaces     INT,
    @CodePrefix      NVARCHAR(5),
    @MinCode         INT,
    @OpeningTime     NVARCHAR(5)   = NULL,
    @ClosingTime     NVARCHAR(5)   = NULL,
    @SecurityDeposit DECIMAL(10,2) = NULL,
    @RentAccountId   INT           = NULL,
    @DepositAccountId INT          = NULL,
    @FloorId         INT           = NULL,
    @PricePerHour    DECIMAL(18,2) = NULL,
    @PricePerDay     DECIMAL(18,2) = NULL,
    @PricePerMonth   DECIMAL(18,2) = NULL,
    @Amenities       NVARCHAR(MAX) = NULL,
    @UpdatedBy       NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate: no duplicate prefix within same company (excluding self)
    DECLARE @CompanyId INT;
    SELECT @CompanyId = CompanyId FROM dbo.WN_SpaceConfig WHERE Id = @Id;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceConfig
        WHERE CodePrefix = @CodePrefix
          AND Status = 1
          AND Id <> @Id
          AND (@CompanyId IS NULL OR CompanyId = @CompanyId)
    )
    BEGIN
        RAISERROR('Prefix %s is already used by another configuration in this company.', 16, 1, @CodePrefix);
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

-- ============================================================
-- 5. WN_SpaceConfig_Delete (soft)
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceConfig SET Status = 0, UpdatedOn = GETUTCDATE()
    WHERE Id = @Id;
END
GO

-- ============================================================
-- 6. WN_SpaceConfig_GenerateSpaces
--    Creates only missing spaces based on config.
--    Returns summary: created, skipped, total.
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_GenerateSpaces
    @ConfigId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SpaceCategory   NVARCHAR(20);
    DECLARE @TotalSpaces     INT;
    DECLARE @CodePrefix      NVARCHAR(5);
    DECLARE @MinCode         INT;
    DECLARE @OpeningTime     NVARCHAR(5);
    DECLARE @ClosingTime     NVARCHAR(5);
    DECLARE @SecurityDeposit DECIMAL(10,2);
    DECLARE @RentAccountId   INT;
    DECLARE @DepositAccountId INT;
    DECLARE @FloorId         INT;
    DECLARE @PricePerHour    DECIMAL(18,2);
    DECLARE @PricePerDay     DECIMAL(18,2);
    DECLARE @PricePerMonth   DECIMAL(18,2);
    DECLARE @Amenities       NVARCHAR(MAX);
    DECLARE @LocationId      INT;
    DECLARE @BranchId        INT;
    DECLARE @CompanyId       INT;
    DECLARE @SpaceTypeId     INT;
    DECLARE @LocationGuid    UNIQUEIDENTIFIER;
    DECLARE @SpaceTypeGuid   UNIQUEIDENTIFIER;

    SELECT
        @SpaceCategory    = SpaceCategory,
        @TotalSpaces      = TotalSpaces,
        @CodePrefix       = CodePrefix,
        @MinCode          = MinCode,
        @OpeningTime      = OpeningTime,
        @ClosingTime      = ClosingTime,
        @SecurityDeposit  = ISNULL(SecurityDeposit, 0),
        @RentAccountId    = RentAccountId,
        @DepositAccountId = SecurityAccountId,
        @FloorId          = FloorId,
        @PricePerHour     = ISNULL(PricePerHour, 0),
        @PricePerDay      = ISNULL(PricePerDay, 0),
        @PricePerMonth    = ISNULL(PricePerMonth, 0),
        @Amenities        = Amenities,
        @LocationId       = LocationId,
        @BranchId         = BranchId,
        @CompanyId        = CompanyId,
        @SpaceTypeId      = SpaceTypeId
    FROM dbo.WN_SpaceConfig WHERE Id = @ConfigId AND Status = 1;

    IF @SpaceCategory IS NULL
    BEGIN
        RAISERROR('Configuration not found or inactive.', 16, 1);
        RETURN;
    END

    SELECT @LocationGuid  = IdGUID FROM dbo.WN_Locations  WHERE Id = @LocationId;
    SELECT @SpaceTypeGuid = IdGUID FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    IF @LocationGuid IS NULL OR @SpaceTypeGuid IS NULL
    BEGIN
        RAISERROR('Location or SpaceType not found.', 16, 1);
        RETURN;
    END

    -- Determine name prefix based on category
    DECLARE @NamePrefix NVARCHAR(50);
    SET @NamePrefix = CASE
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

        IF NOT EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE Code = @Code
              AND LocationId = @LocationGuid
              AND Status IN (0, 1)
        )
        BEGIN
            INSERT INTO dbo.WN_Spaces
                (IdGUID, Name, Code, LocationId, SpaceTypeId, FloorId,
                 PricePerHour, PricePerDay, PricePerMonth,
                 Amenities, RentAccountId, SecurityDepositAccountId,
                 CompanyId, BranchId, Status, CreatedOn)
            VALUES
                (NEWID(), @Name, @Code, @LocationGuid, @SpaceTypeGuid, @FloorId,
                 @PricePerHour, @PricePerDay, @PricePerMonth,
                 @Amenities, @RentAccountId, @DepositAccountId,
                 @CompanyId, @BranchId, 1, GETUTCDATE());
            SET @Created = @Created + 1;
        END
        ELSE
            SET @Skipped = @Skipped + 1;

        SET @i = @i + 1;
    END

    SELECT @Created AS Created, @Skipped AS Skipped, @TotalSpaces AS Configured;
END
GO

-- ============================================================
-- 7. WN_SpaceConfig_GetSpaceStatus
--    Returns existing spaces for a config with booking check
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_GetSpaceStatus
    @ConfigId INT
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

    -- Return all spaces in the code range for this location
    SELECT
        s.Id,
        CAST(s.IdGUID AS NVARCHAR(36)) AS IdGuid,
        s.Code,
        s.Name,
        s.Status,
        CASE WHEN EXISTS (
            SELECT 1 FROM dbo.WN_Bookings b
            WHERE b.SpaceGuid = s.IdGUID AND b.BookingStatus IN (1,4)
        ) THEN 1 ELSE 0 END AS HasBookings
    FROM dbo.WN_Spaces s
    WHERE s.LocationId = @LocationGuid
      AND TRY_CAST(s.Code AS INT) BETWEEN @MinCode AND (@MinCode + @TotalSpaces - 1)
    ORDER BY TRY_CAST(s.Code AS INT);
END
GO

-- ============================================================
-- 8. WN_SpaceConfig_DeleteSpaces
--    Deletes spaces by GUID list; skips those with bookings.
--    @SpaceGuids: comma-separated GUIDs, or NULL = delete all for config
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WN_SpaceConfig_DeleteSpaces
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

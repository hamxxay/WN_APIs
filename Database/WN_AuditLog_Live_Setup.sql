-- ==============================================================================
-- WorkNest Audit Log - Live Database Setup Script
-- Target: Live Production Database (e.g. WorkNestAEO or similar)
--
-- Instructions:
-- 1. Open this script in SSMS, Azure Data Studio, or DBeaver.
-- 2. Make sure the target live database is selected (USE [YourLiveDatabaseName];).
-- 3. Execute the entire script. It is completely idempotent and safe to re-run.
-- ==============================================================================

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- ------------------------------------------------------------------------------
-- STEP 1: CREATE TABLE dbo.WN_AuditLog (IF NOT EXISTS)
-- ------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.WN_AuditLog', N'U') IS NULL
BEGIN
    PRINT 'Creating table dbo.WN_AuditLog...';

    CREATE TABLE dbo.WN_AuditLog (
        AuditId BIGINT IDENTITY(1,1) NOT NULL,
        EntityName NVARCHAR(256) NULL,
        RecordId NVARCHAR(128) NULL,
        Operation CHAR(1) NOT NULL,
        OldValues NVARCHAR(MAX) NULL,
        NewValues NVARCHAR(MAX) NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        ErrorSeverity NVARCHAR(40) NULL,
        StackTrace NVARCHAR(MAX) NULL,
        ChangedByUserId INT NULL,
        ChangedByUserEmail NVARCHAR(512) NULL,
        ChangedAt DATETIME2(7) NOT NULL CONSTRAINT DF_WN_AuditLog_ChangedAt DEFAULT (SYSUTCDATETIME()),
        SourceApp NVARCHAR(100) NULL,
        CONSTRAINT PK_WN_AuditLog PRIMARY KEY CLUSTERED (AuditId ASC)
    );

    PRINT 'Table dbo.WN_AuditLog created successfully.';
END
ELSE
BEGIN
    PRINT 'Table dbo.WN_AuditLog already exists. Skipping creation.';
END;
GO

-- ------------------------------------------------------------------------------
-- STEP 2: CREATE INDEXES FOR FAST QUERYING & FILTERING
-- ------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WN_AuditLog_Entity' AND object_id = OBJECT_ID(N'dbo.WN_AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WN_AuditLog_Entity 
        ON dbo.WN_AuditLog (EntityName, RecordId, ChangedAt);
    PRINT 'Created index IX_WN_AuditLog_Entity.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WN_AuditLog_Operation' AND object_id = OBJECT_ID(N'dbo.WN_AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WN_AuditLog_Operation 
        ON dbo.WN_AuditLog (Operation, ChangedAt);
    PRINT 'Created index IX_WN_AuditLog_Operation.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WN_AuditLog_User' AND object_id = OBJECT_ID(N'dbo.WN_AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WN_AuditLog_User 
        ON dbo.WN_AuditLog (ChangedByUserId, ChangedAt);
    PRINT 'Created index IX_WN_AuditLog_User.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WN_AuditLog_ChangedAt' AND object_id = OBJECT_ID(N'dbo.WN_AuditLog'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WN_AuditLog_ChangedAt 
        ON dbo.WN_AuditLog (ChangedAt DESC);
    PRINT 'Created index IX_WN_AuditLog_ChangedAt.';
END;
GO

-- ------------------------------------------------------------------------------
-- STEP 3: STORED PROCEDURE - dbo.WN_AuditLog_GetPaged
-- Used by: GET /api/audit-log
-- ------------------------------------------------------------------------------
PRINT 'Creating / Updating procedure dbo.WN_AuditLog_GetPaged...';
GO

CREATE OR ALTER PROCEDURE dbo.WN_AuditLog_GetPaged
    @Page INT = 1,
    @PageSize INT = 50,
    @FromDate DATETIME2(3) = NULL,
    @ToDate DATETIME2(3) = NULL,
    @Operation CHAR(1) = NULL,
    @EntityName NVARCHAR(256) = NULL,
    @ChangedByUserEmail NVARCHAR(512) = NULL,
    @RecordId NVARCHAR(128) = NULL,
    @SourceApp NVARCHAR(100) = NULL,
    @ErrorsOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- Clamping
    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    IF @PageSize > 200 SET @PageSize = 200;

    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    DECLARE @ToDateExclusive DATETIME2(3) = NULL;
    IF @ToDate IS NOT NULL
        SET @ToDateExclusive = DATEADD(DAY, 1, CAST(@ToDate AS DATE));

    DECLARE @EmailLike NVARCHAR(514) = NULL;
    IF @ChangedByUserEmail IS NOT NULL AND LEN(LTRIM(RTRIM(@ChangedByUserEmail))) > 0
        SET @EmailLike = N'%' + LTRIM(RTRIM(@ChangedByUserEmail)) + N'%';

    -- Result Set 1: Total Count
    SELECT COUNT(1) AS TotalCount
    FROM dbo.WN_AuditLog a WITH (NOLOCK)
    WHERE (@FromDate IS NULL OR a.ChangedAt >= @FromDate)
      AND (@ToDateExclusive IS NULL OR a.ChangedAt < @ToDateExclusive)
      AND (@Operation IS NULL OR a.Operation = @Operation)
      AND (@EntityName IS NULL OR a.EntityName = @EntityName)
      AND (@EmailLike IS NULL OR a.ChangedByUserEmail LIKE @EmailLike)
      AND (@RecordId IS NULL OR a.RecordId = @RecordId)
      AND (@SourceApp IS NULL OR 
           (@SourceApp IN ('[None]', '[Empty]') AND (a.SourceApp IS NULL OR a.SourceApp = '')) OR 
           (a.SourceApp = @SourceApp))
      AND (@ErrorsOnly = 0 OR (a.ErrorMessage IS NOT NULL AND LEN(a.ErrorMessage) > 0));

    -- Result Set 2: Paged Records
    SELECT a.AuditId,
           a.EntityName,
           a.RecordId,
           a.Operation,
           a.ChangedByUserId,
           a.ChangedByUserEmail,
           a.ChangedAt,
           a.SourceApp,
           a.ErrorSeverity,
           CASE WHEN a.ErrorMessage IS NOT NULL AND LEN(a.ErrorMessage) > 0 THEN 1 ELSE 0 END AS HasError
    FROM dbo.WN_AuditLog a WITH (NOLOCK)
    WHERE (@FromDate IS NULL OR a.ChangedAt >= @FromDate)
      AND (@ToDateExclusive IS NULL OR a.ChangedAt < @ToDateExclusive)
      AND (@Operation IS NULL OR a.Operation = @Operation)
      AND (@EntityName IS NULL OR a.EntityName = @EntityName)
      AND (@EmailLike IS NULL OR a.ChangedByUserEmail LIKE @EmailLike)
      AND (@RecordId IS NULL OR a.RecordId = @RecordId)
      AND (@SourceApp IS NULL OR 
           (@SourceApp IN ('[None]', '[Empty]') AND (a.SourceApp IS NULL OR a.SourceApp = '')) OR 
           (a.SourceApp = @SourceApp))
      AND (@ErrorsOnly = 0 OR (a.ErrorMessage IS NOT NULL AND LEN(a.ErrorMessage) > 0))
    ORDER BY a.AuditId DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

PRINT 'dbo.WN_AuditLog_GetPaged created/updated successfully.';
GO

-- ------------------------------------------------------------------------------
-- STEP 4: STORED PROCEDURE - dbo.WN_AuditLog_GetById
-- Used by: GET /api/audit-log/{id}
-- ------------------------------------------------------------------------------
PRINT 'Creating / Updating procedure dbo.WN_AuditLog_GetById...';
GO

CREATE OR ALTER PROCEDURE dbo.WN_AuditLog_GetById
    @AuditId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT a.AuditId,
           a.EntityName,
           a.RecordId,
           a.Operation,
           a.OldValues,
           a.NewValues,
           a.ErrorMessage,
           a.ErrorSeverity,
           a.StackTrace,
           a.ChangedByUserId,
           a.ChangedByUserEmail,
           a.ChangedAt,
           a.SourceApp,
           CASE WHEN a.ErrorMessage IS NOT NULL AND LEN(a.ErrorMessage) > 0 THEN 1 ELSE 0 END AS HasError
    FROM dbo.WN_AuditLog a WITH (NOLOCK)
    WHERE a.AuditId = @AuditId;
END;
GO

PRINT 'dbo.WN_AuditLog_GetById created/updated successfully.';
GO

-- ------------------------------------------------------------------------------
-- STEP 5: STORED PROCEDURE - dbo.WN_AuditLog_GetFilterOptions
-- Used by: GET /api/audit-log/filter-options
-- ------------------------------------------------------------------------------
PRINT 'Creating / Updating procedure dbo.WN_AuditLog_GetFilterOptions...';
GO

CREATE OR ALTER PROCEDURE dbo.WN_AuditLog_GetFilterOptions
AS
BEGIN
    SET NOCOUNT ON;

    -- Distinct Entity Names
    SELECT DISTINCT a.EntityName
    FROM dbo.WN_AuditLog a WITH (NOLOCK)
    WHERE a.EntityName IS NOT NULL AND LEN(a.EntityName) > 0
    ORDER BY a.EntityName;

    -- Distinct Source Apps
    SELECT DISTINCT a.SourceApp
    FROM dbo.WN_AuditLog a WITH (NOLOCK)
    WHERE a.SourceApp IS NOT NULL AND LEN(a.SourceApp) > 0
    ORDER BY a.SourceApp;
END;
GO

PRINT 'dbo.WN_AuditLog_GetFilterOptions created/updated successfully.';
GO

PRINT '==============================================================================';
PRINT 'WorkNest Audit Log Core Setup completed successfully!';
PRINT '==============================================================================';
GO

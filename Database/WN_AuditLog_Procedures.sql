-- ==============================================================================
-- WorkNest Audit Log Stored Procedures
-- Database: SAC400
-- ==============================================================================

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Paged audit logs with server-side pagination and filters
CREATE OR ALTER PROCEDURE dbo.WN_AuditLog_GetPaged
    @Page INT = 1,
    @PageSize INT = 50,
    @FromDate DATETIME2(3) = NULL,
    @ToDate DATETIME2(3) = NULL,
    @Operation CHAR(1) = NULL,
    @EntityName NVARCHAR(128) = NULL,
    @ChangedByUserEmail NVARCHAR(256) = NULL,
    @RecordId NVARCHAR(64) = NULL,
    @SourceApp NVARCHAR(50) = NULL,
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

    DECLARE @EmailLike NVARCHAR(258) = NULL;
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

-- 2. Audit log detail by AuditId (on-demand modal inspection)
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

-- 3. Distinct filter options for dropdowns
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

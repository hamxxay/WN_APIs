-- ==============================================================================
-- WorkNest Login History Setup
-- Database: SAC400
-- Standalone SQL script creating dbo.WN_LoginHistory and associated procedures.
-- Does not alter existing tables or stored procedures.
-- ==============================================================================

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Table dbo.WN_LoginHistory
IF OBJECT_ID('dbo.WN_LoginHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WN_LoginHistory (
        LoginId BIGINT IDENTITY(1,1) NOT NULL,
        UserId INT NULL,
        Email NVARCHAR(256) NULL,
        EventType VARCHAR(10) NOT NULL, -- LOGIN, FAILED, LOGOUT
        FailureReason NVARCHAR(100) NULL,
        IpAddress VARCHAR(45) NULL,
        UserAgent NVARCHAR(400) NULL,
        EventAt DATETIME2(3) NOT NULL CONSTRAINT DF_WN_LoginHistory_EventAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WN_LoginHistory PRIMARY KEY CLUSTERED (LoginId)
    );
END
GO

-- 2. Index on (UserId, LoginId DESC)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_WN_LoginHistory_UserId_LoginId' 
      AND object_id = OBJECT_ID('dbo.WN_LoginHistory')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_WN_LoginHistory_UserId_LoginId
    ON dbo.WN_LoginHistory (UserId, LoginId DESC);
END
GO

-- Index on (EventAt DESC) for efficient time-range filtering and stats
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_WN_LoginHistory_EventAt' 
      AND object_id = OBJECT_ID('dbo.WN_LoginHistory')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_WN_LoginHistory_EventAt
    ON dbo.WN_LoginHistory (EventAt DESC);
END
GO

-- 3. Procedure: Record a Login/Logout/Failed event
CREATE OR ALTER PROCEDURE dbo.WN_LoginHistory_Insert
    @UserId INT = NULL,
    @Email NVARCHAR(256) = NULL,
    @EventType VARCHAR(10),
    @FailureReason NVARCHAR(100) = NULL,
    @IpAddress VARCHAR(45) = NULL,
    @UserAgent NVARCHAR(400) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.WN_LoginHistory (
        UserId,
        Email,
        EventType,
        FailureReason,
        IpAddress,
        UserAgent,
        EventAt
    )
    VALUES (
        @UserId,
        @Email,
        @EventType,
        @FailureReason,
        @IpAddress,
        @UserAgent,
        SYSUTCDATETIME()
    );

    SELECT SCOPE_IDENTITY() AS LoginId;
END;
GO

-- 4. Procedure: Summary stats for today (Asia/Karachi timezone, UTC+5)
CREATE OR ALTER PROCEDURE dbo.WN_LoginHistory_GetStats
AS
BEGIN
    SET NOCOUNT ON;

    -- Compute today's boundary in Asia/Karachi (UTC+5) converted to UTC
    DECLARE @KarachiNow DATETIME2(3) = DATEADD(HOUR, 5, SYSUTCDATETIME());
    DECLARE @TodayKarachi DATE = CAST(@KarachiNow AS DATE);
    DECLARE @TodayUtcStart DATETIME2(3) = DATEADD(HOUR, -5, CAST(@TodayKarachi AS DATETIME2(3)));
    DECLARE @TomorrowUtcStart DATETIME2(3) = DATEADD(DAY, 1, @TodayUtcStart);

    SELECT 
        ISNULL(SUM(CASE WHEN EventType = 'LOGIN' THEN 1 ELSE 0 END), 0) AS LoginsToday,
        ISNULL(SUM(CASE WHEN EventType = 'FAILED' THEN 1 ELSE 0 END), 0) AS FailedToday,
        COUNT(DISTINCT CASE WHEN EventType = 'LOGIN' THEN COALESCE(CAST(UserId AS NVARCHAR(50)), Email) END) AS DistinctUsersToday
    FROM dbo.WN_LoginHistory WITH (NOLOCK)
    WHERE EventAt >= @TodayUtcStart 
      AND EventAt < @TomorrowUtcStart;
END;
GO

-- 5. Procedure: Paged login history with server-side pagination and filters
CREATE OR ALTER PROCEDURE dbo.WN_LoginHistory_GetPaged
    @Page INT = 1,
    @PageSize INT = 50,
    @FromDate DATETIME2(3) = NULL,
    @ToDate DATETIME2(3) = NULL,
    @Email NVARCHAR(256) = NULL,
    @EventType VARCHAR(10) = NULL,
    @IpAddress VARCHAR(45) = NULL,
    @LatestOnly BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- Clamping pagination
    IF @Page < 1 SET @Page = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    IF @PageSize > 200 SET @PageSize = 200;

    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    DECLARE @ToDateExclusive DATETIME2(3) = NULL;
    IF @ToDate IS NOT NULL
        SET @ToDateExclusive = DATEADD(DAY, 1, CAST(@ToDate AS DATE));

    DECLARE @EmailLike NVARCHAR(258) = NULL;
    IF @Email IS NOT NULL AND LEN(LTRIM(RTRIM(@Email))) > 0
        SET @EmailLike = N'%' + LTRIM(RTRIM(@Email)) + N'%';

    DECLARE @IpLike NVARCHAR(47) = NULL;
    IF @IpAddress IS NOT NULL AND LEN(LTRIM(RTRIM(@IpAddress))) > 0
        SET @IpLike = N'%' + LTRIM(RTRIM(@IpAddress)) + N'%';

    IF @LatestOnly = 1
    BEGIN
        -- Optional view: Latest LOGIN row per UserId
        WITH LatestLogins AS (
            SELECT 
                LoginId,
                UserId,
                Email,
                EventType,
                FailureReason,
                IpAddress,
                UserAgent,
                EventAt,
                ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY LoginId DESC) AS rn
            FROM dbo.WN_LoginHistory WITH (NOLOCK)
            WHERE EventType = 'LOGIN'
              AND UserId IS NOT NULL
        ),
        FilteredLogins AS (
            SELECT *
            FROM LatestLogins
            WHERE rn = 1
              AND (@FromDate IS NULL OR EventAt >= @FromDate)
              AND (@ToDateExclusive IS NULL OR EventAt < @ToDateExclusive)
              AND (@EmailLike IS NULL OR Email LIKE @EmailLike)
              AND (@IpLike IS NULL OR IpAddress LIKE @IpLike)
        )
        SELECT COUNT(1) AS TotalCount FROM FilteredLogins;

        WITH LatestLogins AS (
            SELECT 
                LoginId,
                UserId,
                Email,
                EventType,
                FailureReason,
                IpAddress,
                UserAgent,
                EventAt,
                ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY LoginId DESC) AS rn
            FROM dbo.WN_LoginHistory WITH (NOLOCK)
            WHERE EventType = 'LOGIN'
              AND UserId IS NOT NULL
        )
        SELECT 
            LoginId,
            UserId,
            Email,
            EventType,
            FailureReason,
            IpAddress,
            UserAgent,
            EventAt
        FROM LatestLogins
        WHERE rn = 1
          AND (@FromDate IS NULL OR EventAt >= @FromDate)
          AND (@ToDateExclusive IS NULL OR EventAt < @ToDateExclusive)
          AND (@EmailLike IS NULL OR Email LIKE @EmailLike)
          AND (@IpLike IS NULL OR IpAddress LIKE @IpLike)
        ORDER BY LoginId DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
    ELSE
    BEGIN
        -- Standard audit grid
        SELECT COUNT(1) AS TotalCount
        FROM dbo.WN_LoginHistory h WITH (NOLOCK)
        WHERE (@FromDate IS NULL OR h.EventAt >= @FromDate)
          AND (@ToDateExclusive IS NULL OR h.EventAt < @ToDateExclusive)
          AND (@EmailLike IS NULL OR h.Email LIKE @EmailLike)
          AND (@EventType IS NULL OR @EventType = '' OR h.EventType = @EventType)
          AND (@IpLike IS NULL OR h.IpAddress LIKE @IpLike);

        SELECT 
            h.LoginId,
            h.UserId,
            h.Email,
            h.EventType,
            h.FailureReason,
            h.IpAddress,
            h.UserAgent,
            h.EventAt
        FROM dbo.WN_LoginHistory h WITH (NOLOCK)
        WHERE (@FromDate IS NULL OR h.EventAt >= @FromDate)
          AND (@ToDateExclusive IS NULL OR h.EventAt < @ToDateExclusive)
          AND (@EmailLike IS NULL OR h.Email LIKE @EmailLike)
          AND (@EventType IS NULL OR @EventType = '' OR h.EventType = @EventType)
          AND (@IpLike IS NULL OR h.IpAddress LIKE @IpLike)
        ORDER BY h.LoginId DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
    END
END;
GO

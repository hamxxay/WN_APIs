-- Deploy after USR_01_sales_manager_link.sql. This file alters only the four manager-link procedures.
-- Run in the target WorkNest database using SQL Server 2016 SP1 or later.
IF COL_LENGTH(N'dbo.WN_Users', N'ManagerId') IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.WN_Roles WHERE Id = 18 AND RoleName = N'sales_manager')
BEGIN
    THROW 51030, 'Apply USR_01_sales_manager_link.sql before this procedure script.', 1;
END;
GO

/****** Object:  StoredProcedure [dbo].[WN_SalesMgr_Dashboard]    Script Date: 04/11/2026 10:31:37 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SalesMgr_Dashboard]
    @LocationIds       NVARCHAR(MAX) = NULL,  -- locations in scope, CSV (NULL = all locations, super admin)
    @PickerLocationIds NVARCHAR(MAX) = NULL,  -- locations for the location picker (those of the manager), CSV (NULL = all)
    @PMonths           INT = 1,               -- 1 = this month, 3 = this quarter, 12 = this year (to date)
    @BizNow            DATETIME2(0) = NULL,   -- business (Pakistan) wall-clock time from the app clock
    @VoidStatusIds     NVARCHAR(400) = NULL,  -- not used since SM_02 (no money); kept so an older API build still runs
    @ExpiringDays      INT = 90,              -- leases ending in the next N days
    @InquiriesNoBranch BIT = NULL,            -- BR_02: tour inquiries follow @LocationIds too: 1 = also those without a
                                              -- branch (all my locations), 0 = only those of @LocationIds (one location
                                              -- chosen); NULL = every inquiry of every branch (as before BR_02)
    @From              DATE = NULL,           -- CS_01: first day of a custom range (with @To; replaces @PMonths)
    @To                DATE = NULL,           -- CS_01: last day of a custom range (included)
    @CurrentManagerId  INT = NULL             -- Team only: assigned reports plus unassigned executives; NULL preserves super-admin view
AS
BEGIN
    -- Sales Manager dashboard (read-only), in desks, no money (SM_02). Same rules as the CEO dashboard
    -- (dbo.WN_Ceo_Dashboard) for what is counted, so the counts match it for the same location and period:
    --   spaces = WN_Spaces.Status = 1 at the locations in scope; quotations = active, on those spaces;
    --   accepted = Accepted, AgreementSent, Signed or Converted; new bookings = created in the period, not deleted,
    --   not rejected / cancelled / no show (3, 4, 6, 86);
    --   desks of a booking = its Capacity, else the Capacity of its space; of a quotation = its Capacity, else the
    --   Capacity of its space; meeting rooms (space type category MeetingRoom / Conference, P1) count 0 desks; no capacity = 0;
    --   desks sold = desks on Converted quotations; leases = confirmed (2, 33) bookings, not deleted: expiring = ending
    --   in the next @ExpiringDays days (and in the next 30 days), ended = ending in the period before now;
    --   tour inquiries (BR_02) = those of the locations in scope by WN_Contacts.LocationId, plus the ones without a
    --   branch (WN_BookTour bookings, LocationId NULL) when @InquiriesNoBranch = 1; every inquiry when @LocationIds is
    --   NULL (super admin, all locations) or @InquiriesNoBranch is NULL (as before BR_02); latest feedback per inquiry
    --   (dbo.WN_ContactFeedback). Team inquiries handled / follow-ups due count only those inquiries.
    -- Previous period = the same number of days, one period earlier. CS_01: @From / @To = a custom range for every
    -- period figure, previous = as many days just before it (the 12-month series and expiring leases stay as they are).
    -- Result sets: [0] picker locations, [1] funnel, [2] funnel last 12 months, [3] team people, [4] team per location,
    -- [5] inquiries, [6] inquiries by type, [7] not-interested reasons (top 6), [8] inquiries last 12 months,
    -- [9] renewals, [10] leases expiring (first 50), [11] renewals last 12 months and next 3.
    SET NOCOUNT ON;
    IF @PMonths IS NULL OR @PMonths NOT IN (1, 3, 12) SET @PMonths = 1;
    SET @LocationIds = NULLIF(LTRIM(RTRIM(@LocationIds)), N'');
    SET @PickerLocationIds = NULLIF(LTRIM(RTRIM(@PickerLocationIds)), N'');
    -- CS_01: a custom range (@From - @To, both days included) instead of the calendar period of @PMonths; previous
    -- period = as many days just before it. Without both days (or @From after @To): the calendar period, as before.
    DECLARE @Custom BIT = CASE WHEN @From IS NOT NULL AND @To IS NOT NULL AND @From <= @To THEN 1 ELSE 0 END;
    DECLARE @CustomDays INT = CASE WHEN @Custom = 1 THEN DATEDIFF(DAY, @From, @To) + 1 ELSE 0 END;
    DECLARE @Now DATETIME2(0) = ISNULL(@BizNow, SYSDATETIME());
    DECLARE @Today DATE = CAST(@Now AS DATE);
    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 1);
    DECLARE @PStart DATE = CASE @PMonths
        WHEN 12 THEN DATEFROMPARTS(YEAR(@Today), 1, 1)
        WHEN 3  THEN DATEFROMPARTS(YEAR(@Today), ((MONTH(@Today) - 1) / 3) * 3 + 1, 1)
        ELSE @MonthStart END;
    IF @Custom = 1 SET @PStart = @From;                                                     -- CS_01
    DECLARE @PrevStart DATE = CASE WHEN @Custom = 1 THEN DATEADD(DAY, -@CustomDays, @PStart) ELSE DATEADD(MONTH, -@PMonths, @PStart) END;
    DECLARE @PrevEnd DATE = CASE WHEN @Custom = 1 THEN @PStart                              -- CS_01: the days just before the range
                                 ELSE DATEADD(DAY, DATEDIFF(DAY, @PStart, @Today) + 1, @PrevStart) END;   -- same number of days, exclusive
    -- CS_01: created before this day (custom: the day after @To); the calendar periods have no end (to date, as before).
    DECLARE @CEnd DATETIME2(0) = CASE WHEN @Custom = 1 THEN CAST(DATEADD(DAY, 1, @To) AS DATETIME2(0)) ELSE CAST(N'9999-12-31' AS DATETIME2(0)) END;
    -- CS_01: the same bounds for the columns written in UTC (SYSUTCDATETIME: bookings CreatedOn, quotations CreatedDate,
    -- tour inquiries CreatedOn): a custom range is in Pakistan days (UTC+5, no daylight saving), so 5 hours earlier.
    -- The calendar periods keep their comparisons as they were.
    DECLARE @UShift INT = CASE WHEN @Custom = 1 THEN -5 ELSE 0 END;
    DECLARE @UStart DATETIME2(0) = DATEADD(HOUR, @UShift, CAST(@PStart AS DATETIME2(0)));
    DECLARE @UEnd DATETIME2(0) = CASE WHEN @Custom = 1 THEN DATEADD(HOUR, -5, @CEnd) ELSE @CEnd END;
    DECLARE @UPrevStart DATETIME2(0) = DATEADD(HOUR, @UShift, CAST(@PrevStart AS DATETIME2(0)));
    DECLARE @UPrevEnd DATETIME2(0) = DATEADD(HOUR, @UShift, CAST(@PrevEnd AS DATETIME2(0)));
    DECLARE @SeriesStart DATE = DATEADD(MONTH, -11, @MonthStart);                          -- 12 months incl. this one
    DECLARE @Earliest DATE = CASE WHEN @PrevStart < @SeriesStart THEN @PrevStart ELSE @SeriesStart END;
    DECLARE @ExpEnd DATETIME2(0) = DATEADD(DAY, ISNULL(@ExpiringDays, 90), @Now);
    DECLARE @Exp30End DATETIME2(0) = DATEADD(DAY, 30, @Now);
    DECLARE @All BIT = CASE WHEN @LocationIds IS NULL THEN 1 ELSE 0 END;
    DECLARE @Scope TABLE (Id INT PRIMARY KEY);
    INSERT INTO @Scope (Id) SELECT DISTINCT TRY_CAST(value AS INT) FROM STRING_SPLIT(ISNULL(@LocationIds, N''), N',') WHERE TRY_CAST(value AS INT) > 0;
    -- Locations: Shown = active, or still has active spaces (the CEO dashboard list). @Loc = the ones in scope.
    DECLARE @LocAll TABLE (Id INT PRIMARY KEY, Name NVARCHAR(200), Shown BIT NOT NULL);
    INSERT INTO @LocAll (Id, Name, Shown)
    SELECT l.Id, ISNULL(NULLIF(LTRIM(RTRIM(CAST(l.Name AS NVARCHAR(200)))), N''), CONCAT(N'Location ', l.Id)),
           CASE WHEN l.IsActive = 1 OR EXISTS (SELECT 1 FROM dbo.WN_Spaces s WITH (NOLOCK) WHERE s.LocationId = l.Id AND s.Status = 1) THEN 1 ELSE 0 END
      FROM dbo.WN_Locations l WITH (NOLOCK);
    DECLARE @Loc TABLE (Id INT PRIMARY KEY, Name NVARCHAR(200));
    INSERT INTO @Loc (Id, Name)
    SELECT Id, Name FROM @LocAll WHERE (@All = 1 AND Shown = 1) OR Id IN (SELECT Id FROM @Scope);
    -- Spaces in scope with their desks (Capacity) and whether they are a meeting room (category MeetingRoom / Conference, P1: no desks).
    DECLARE @Spaces TABLE (Id INT PRIMARY KEY, Name NVARCHAR(200), LocationId INT NULL, Capacity INT NOT NULL, Meeting BIT NOT NULL);
    INSERT INTO @Spaces (Id, Name, LocationId, Capacity, Meeting)
    SELECT s.Id, CAST(COALESCE(NULLIF(s.Name, N''), s.Code) AS NVARCHAR(200)), s.LocationId, ISNULL(s.Capacity, 0),
           CASE WHEN EXISTS (SELECT 1 FROM dbo.WN_SpaceTypes mst WITH (NOLOCK) JOIN dbo.WN_SpaceCategories msc WITH (NOLOCK) ON msc.Id = mst.CategoryId WHERE mst.Id = s.SpaceTypeId AND msc.Code IN (N'MeetingRoom', N'Conference')) THEN 1 ELSE 0 END
      FROM dbo.WN_Spaces s WITH (NOLOCK)
     WHERE s.Status = 1 AND (@All = 1 OR s.LocationId IN (SELECT Id FROM @Scope));
    -- New bookings since the earliest date any section needs, with their desks.
    DECLARE @New TABLE (Id INT PRIMARY KEY, LocationId INT NULL, CreatedOn DATETIME NULL, Desks INT NOT NULL);
    INSERT INTO @New (Id, LocationId, CreatedOn, Desks)
    SELECT b.Id, sp.LocationId, b.CreatedOn,
           CASE WHEN sp.Meeting = 1 THEN 0 ELSE ISNULL(COALESCE(NULLIF(b.Capacity, 0), sp.Capacity), 0) END
      FROM dbo.WN_Bookings b WITH (NOLOCK)
      JOIN @Spaces sp ON sp.Id = b.SpaceId
     WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId NOT IN (3, 4, 6, 86) AND b.CreatedOn >= DATEADD(HOUR, @UShift, CAST(@Earliest AS DATETIME2(0)));
    -- Active quotations on the spaces in scope (sales funnel, per location), with their desks.
    -- CP_02: Booked = Converted and its booking is live (not deleted, not rejected / cancelled / no show: 3, 4, 6, 86),
    -- as the CEO dashboard; "converted", conversion and desks sold count only those.
    DECLARE @Q TABLE (Id INT PRIMARY KEY, LocationId INT NULL, CreatedDate DATETIME2 NULL, Status NVARCHAR(100) NULL, Desks INT NOT NULL,
                      Booked BIT NOT NULL);   -- CP_02
    INSERT INTO @Q (Id, LocationId, CreatedDate, Status, Desks, Booked)
    SELECT q.Id, sp.LocationId, q.CreatedDate, q.Status,
           CASE WHEN sp.Meeting = 1 THEN 0 ELSE ISNULL(COALESCE(NULLIF(q.Capacity, 0), sp.Capacity), 0) END,
           CASE WHEN q.Status = 'Converted' AND bk.Id IS NOT NULL THEN 1 ELSE 0 END   -- CP_02
      FROM dbo.WN_Quotations q WITH (NOLOCK)
      JOIN @Spaces sp ON sp.Id = q.SpaceId
      LEFT JOIN dbo.WN_Bookings bk WITH (NOLOCK) ON bk.Id = q.BookingId AND ISNULL(bk.IsDeleted, 0) = 0 AND bk.BookingStatusId NOT IN (3, 4, 6, 86)   -- CP_02
     WHERE ISNULL(q.IsActive, 1) = 1 AND q.CreatedDate >= DATEADD(HOUR, @UShift, CAST(@Earliest AS DATETIME2(0)));
    -- The tour inquiries in scope with their latest feedback (BR_02: by branch; see above).
    DECLARE @InqLocationIds NVARCHAR(MAX) = CASE WHEN @InquiriesNoBranch IS NULL THEN NULL ELSE @LocationIds END;
    DECLARE @Inq TABLE (Id INT, Name NVARCHAR(MAX), PhoneNumber NVARCHAR(MAX), InquiryType NVARCHAR(20), ReferenceName NVARCHAR(MAX),
                        StatusId INT, CreatedOn DATETIME2, FeedbackOutcome NVARCHAR(50), FeedbackReason NVARCHAR(1000),
                        FollowUpOn DATE, FeedbackById INT);
    INSERT INTO @Inq EXEC dbo.WN_SalesMgr_InquiryRows @From = NULL, @To = NULL, @LocationIds = @InqLocationIds,
                          @IncludeNoLocation = @InquiriesNoBranch;
    -- [0] the location picker (never narrowed by the chosen location)
    SELECT Id AS LocationId, Name FROM @LocAll
     WHERE (@PickerLocationIds IS NULL AND Shown = 1)
        OR Id IN (SELECT TRY_CAST(value AS INT) FROM STRING_SPLIT(ISNULL(@PickerLocationIds, N''), N','))
     ORDER BY Name;
    -- [1] sales funnel: created in the period (and the previous period); desks quoted and booked
    SELECT (SELECT COUNT(*) FROM @Inq WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd) AS Inquiries,
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= @UStart AND CreatedDate < @UEnd) AS Quotations,
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= @UStart AND CreatedDate < @UEnd AND Status IN ('Accepted', 'AgreementSent', 'Signed', 'Converted')) AS Accepted,
           (SELECT ISNULL(SUM(Desks), 0) FROM @Q WHERE CreatedDate >= @UStart AND CreatedDate < @UEnd) AS QuotedDesks,
           (SELECT COUNT(*) FROM @New WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd) AS Bookings,
           (SELECT ISNULL(SUM(Desks), 0) FROM @New WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd) AS BookedDesks,
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= @UPrevStart AND CreatedDate < @UPrevEnd) AS QuotationsPrev,
           (SELECT COUNT(*) FROM @New WHERE CreatedOn >= @UPrevStart AND CreatedOn < @UPrevEnd) AS BookingsPrev,
           (SELECT ISNULL(SUM(Desks), 0) FROM @New WHERE CreatedOn >= @UPrevStart AND CreatedOn < @UPrevEnd) AS BookedDesksPrev,
           -- SM_04: conversion = quotations created in the period that were booked (Converted) / quotations created in it
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= @UStart AND CreatedDate < @UEnd AND Booked = 1) AS QuotationsBooked,   -- CP_02
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= @UPrevStart AND CreatedDate < @UPrevEnd AND Booked = 1) AS QuotationsBookedPrev;   -- CP_02
    -- [2] funnel, last 12 months, oldest first
    ;WITH m AS (
        SELECT DATEADD(MONTH, -n, @MonthStart) AS MStart
          FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) v(n)
    )
    SELECT CONVERT(CHAR(7), m.MStart, 126) AS [Month],
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= m.MStart AND CreatedDate < DATEADD(MONTH, 1, m.MStart)) AS Quotations,
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= m.MStart AND CreatedDate < DATEADD(MONTH, 1, m.MStart)
               AND Status IN ('Accepted', 'AgreementSent', 'Signed', 'Converted')) AS Accepted,
           (SELECT COUNT(*) FROM @New WHERE CreatedOn >= m.MStart AND CreatedOn < DATEADD(MONTH, 1, m.MStart)) AS Bookings,
           (SELECT ISNULL(SUM(Desks), 0) FROM @New WHERE CreatedOn >= m.MStart AND CreatedOn < DATEADD(MONTH, 1, m.MStart)) AS BookedDesks,
           (SELECT COUNT(*) FROM @Q WHERE CreatedDate >= m.MStart AND CreatedDate < DATEADD(MONTH, 1, m.MStart) AND Booked = 1) AS QuotationsBooked   -- CP_02
      FROM m
     ORDER BY m.MStart;
    -- Staff and their locations (as the CEO dashboard): main location + WN_UserLocations.
    DECLARE @UL TABLE (UserId INT, LocationId INT, PRIMARY KEY (UserId, LocationId));
    INSERT INTO @UL SELECT Id, LocationId FROM dbo.WN_Users WITH (NOLOCK) WHERE IsActive = 1 AND RoleId IN (2, 16) AND LocationId IS NOT NULL;
    IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
        INSERT INTO @UL SELECT DISTINCT ul.UserId, ul.LocationId FROM dbo.WN_UserLocations ul WITH (NOLOCK)
          JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = ul.UserId
         WHERE u.IsActive = 1 AND u.RoleId IN (2, 16)
           AND NOT EXISTS (SELECT 1 FROM @UL x WHERE x.UserId = ul.UserId AND x.LocationId = ul.LocationId);
    -- People: active admins and sales executives of the locations in scope (all of them when all locations).
    DECLARE @People TABLE (UserId INT PRIMARY KEY);
    INSERT INTO @People (UserId)
    SELECT u.Id FROM dbo.WN_Users u WITH (NOLOCK)
     WHERE u.IsActive = 1 AND u.RoleId IN (2, 16)
       AND (@All = 1 OR EXISTS (SELECT 1 FROM @UL ul JOIN @Scope sc ON sc.Id = ul.LocationId WHERE ul.UserId = u.Id))
       AND (u.RoleId <> 16 OR @CurrentManagerId IS NULL OR u.ManagerId = @CurrentManagerId OR u.ManagerId IS NULL);
    -- Inquiries each person gave feedback on in the period (Tour Inquiries page feedback); BR_02: only the inquiries in
    -- scope (@Inq), unless every inquiry counts.
    DECLARE @Handled TABLE (UserId INT PRIMARY KEY, Inquiries INT NOT NULL);
    IF OBJECT_ID(N'dbo.WN_ContactFeedback', N'U') IS NOT NULL
        INSERT INTO @Handled (UserId, Inquiries)
        SELECT f.CreatedById, COUNT(DISTINCT f.ContactId) FROM dbo.WN_ContactFeedback f WITH (NOLOCK)
         WHERE f.CreatedById IS NOT NULL AND f.CreatedOn >= @PStart AND f.CreatedOn < @CEnd
           AND (@InqLocationIds IS NULL OR f.ContactId IN (SELECT i.Id FROM @Inq i))
         GROUP BY f.CreatedById;
    -- [3] people: quotations created in the period (only on the spaces in scope, unless all locations), accepted,
    --     converted and the desks on the converted ones (desks sold); inquiries handled in the period; follow-ups due
    --     now (their latest feedback is a future prospect whose date has come, inquiry still open). Role name from
    --     WN_Roles.DisplayName. Ranked by desks sold, converted, quotations, name.
    SELECT u.Id AS UserId, COALESCE(NULLIF(u.Name, N''), NULLIF(u.UserName, N''), u.Email) AS Name, u.RoleId, u.ManagerId,
           COALESCE(NULLIF(r.DisplayName, N''), r.RoleName) AS RoleName,
           STUFF((SELECT N', ' + l.Name FROM @UL ul JOIN @LocAll l ON l.Id = ul.LocationId AND l.Shown = 1
                   WHERE ul.UserId = u.Id ORDER BY l.Name FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, N'') AS Locations,
           ISNULL(qs.Quotations, 0) AS Quotations, ISNULL(qs.Accepted, 0) AS Accepted, ISNULL(qs.Converted, 0) AS Converted,
           ISNULL(qs.DesksSold, 0) AS DesksSold, ISNULL(h.Inquiries, 0) AS InquiriesHandled,
           (SELECT COUNT(*) FROM @Inq i WHERE i.FeedbackById = u.Id AND i.FeedbackOutcome = N'future_prospect'
               AND i.FollowUpOn <= @Today AND ISNULL(i.StatusId, 1) <> 3) AS FollowUpsDue
      FROM @People p
      JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
      LEFT JOIN dbo.WN_Roles r WITH (NOLOCK) ON r.Id = u.RoleId
      LEFT JOIN @Handled h ON h.UserId = u.Id
     OUTER APPLY (SELECT COUNT(q.Id) AS Quotations,
                         SUM(CASE WHEN q.Status IN ('Accepted', 'AgreementSent', 'Signed', 'Converted') THEN 1 ELSE 0 END) AS Accepted,
                         SUM(CASE WHEN q.Status = 'Converted' AND bk.Id IS NOT NULL THEN 1 ELSE 0 END) AS Converted,   -- CP_02
                         SUM(CASE WHEN q.Status = 'Converted' AND bk.Id IS NOT NULL AND ISNULL(msc.Code, N'') NOT IN (N'MeetingRoom', N'Conference')
                                  THEN ISNULL(COALESCE(NULLIF(q.Capacity, 0), s.Capacity), 0) ELSE 0 END) AS DesksSold
                    FROM dbo.WN_Quotations q WITH (NOLOCK)
                    LEFT JOIN dbo.WN_Bookings bk WITH (NOLOCK) ON bk.Id = q.BookingId AND ISNULL(bk.IsDeleted, 0) = 0 AND bk.BookingStatusId NOT IN (3, 4, 6, 86)   -- CP_02
                    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = q.SpaceId
                    LEFT JOIN dbo.WN_SpaceTypes mst WITH (NOLOCK) ON mst.Id = s.SpaceTypeId
                    LEFT JOIN dbo.WN_SpaceCategories msc WITH (NOLOCK) ON msc.Id = mst.CategoryId  -- P1: meeting rooms by category (no subquery inside SUM)
                   WHERE q.CreatedById = u.Id AND q.CreatedDate >= @UStart AND q.CreatedDate < @UEnd AND ISNULL(q.IsActive, 1) = 1
                     AND (@All = 1 OR q.SpaceId IN (SELECT Id FROM @Spaces))) qs
     ORDER BY ISNULL(qs.DesksSold, 0) DESC, ISNULL(qs.Converted, 0) DESC, ISNULL(qs.Quotations, 0) DESC,
              COALESCE(NULLIF(u.Name, N''), NULLIF(u.UserName, N''), u.Email);
    -- [4] per location: sales executives assigned, quotations created in the period on its spaces, converted and
    --     desks sold (desks on the converted ones)
    SELECT l.Id AS LocationId, l.Name,
           (SELECT COUNT(*) FROM @UL ul JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = ul.UserId WHERE ul.LocationId = l.Id AND u.RoleId = 16) AS SalesExecutives,
           (SELECT COUNT(*) FROM @Q q WHERE q.LocationId = l.Id AND q.CreatedDate >= @UStart AND q.CreatedDate < @UEnd) AS Quotations,
           (SELECT COUNT(*) FROM @Q q WHERE q.LocationId = l.Id AND q.CreatedDate >= @UStart AND q.CreatedDate < @UEnd AND q.Booked = 1) AS Converted,   -- CP_02
           (SELECT ISNULL(SUM(q.Desks), 0) FROM @Q q WHERE q.LocationId = l.Id AND q.CreatedDate >= @UStart AND q.CreatedDate < @UEnd AND q.Booked = 1) AS DesksSold   -- CP_02
      FROM @Loc l
     ORDER BY l.Name;
    -- [5] tour inquiries created in the period by latest outcome; follow-ups due now (any inquiry still open);
    --     ByBranch = 1: the inquiries follow the locations in scope (BR_02, @InquiriesNoBranch given)
    SELECT (SELECT COUNT(*) FROM @Inq WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd) AS Total,
           (SELECT COUNT(*) FROM @Inq WHERE CreatedOn >= @UPrevStart AND CreatedOn < @UPrevEnd) AS TotalPrev,
           (SELECT COUNT(*) FROM @Inq WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd AND FeedbackOutcome = N'future_prospect') AS FollowedUp,
           (SELECT COUNT(*) FROM @Inq WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd AND FeedbackOutcome = N'not_interested') AS NotInterested,
           (SELECT COUNT(*) FROM @Inq WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd AND FeedbackOutcome = N'converted') AS Converted,
           (SELECT COUNT(*) FROM @Inq WHERE FeedbackOutcome = N'future_prospect' AND FollowUpOn <= @Today AND ISNULL(StatusId, 1) <> 3) AS FollowUpsDue,
           CAST(CASE WHEN @InquiriesNoBranch IS NULL THEN 0 ELSE 1 END AS BIT) AS ByBranch;
    -- [6] tour inquiries created in the period by type
    SELECT InquiryType, COUNT(*) AS Inquiries FROM @Inq WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd GROUP BY InquiryType;
    -- [7] not-interested reasons of the inquiries created in the period (case and spaces ignored), top 6
    SELECT TOP 6 MIN(LTRIM(RTRIM(FeedbackReason))) AS Reason, COUNT(*) AS Inquiries
      FROM @Inq
     WHERE CreatedOn >= @UStart AND CreatedOn < @UEnd AND FeedbackOutcome = N'not_interested' AND NULLIF(LTRIM(RTRIM(FeedbackReason)), N'') IS NOT NULL
     GROUP BY LOWER(LTRIM(RTRIM(FeedbackReason)))
     ORDER BY COUNT(*) DESC, MIN(LTRIM(RTRIM(FeedbackReason)));
    -- [8] tour inquiries by type, last 12 months, oldest first
    ;WITH m AS (
        SELECT DATEADD(MONTH, -n, @MonthStart) AS MStart
          FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) v(n)
    )
    SELECT CONVERT(CHAR(7), m.MStart, 126) AS [Month],
           ISNULL(SUM(CASE WHEN i.InquiryType = N'Website' THEN 1 ELSE 0 END), 0) AS Website,
           ISNULL(SUM(CASE WHEN i.InquiryType = N'WhatsApp' THEN 1 ELSE 0 END), 0) AS WhatsApp,
           ISNULL(SUM(CASE WHEN i.InquiryType = N'WalkIn' THEN 1 ELSE 0 END), 0) AS WalkIn,
           ISNULL(SUM(CASE WHEN i.InquiryType = N'Reference' THEN 1 ELSE 0 END), 0) AS Reference,
           ISNULL(SUM(CASE WHEN i.InquiryType NOT IN (N'Website', N'WhatsApp', N'WalkIn', N'Reference') THEN 1 ELSE 0 END), 0) AS NotSet
      FROM m
      LEFT JOIN @Inq i ON i.CreatedOn >= m.MStart AND i.CreatedOn < DATEADD(MONTH, 1, m.MStart)
     GROUP BY m.MStart
     ORDER BY m.MStart;
    -- Confirmed leases (bookings 2, 33, not deleted) on the spaces in scope with their desks, ending from the earliest
    -- date any section needs on (the first month of the series, or the start of the previous period).
    DECLARE @Leases TABLE (Id INT PRIMARY KEY, SpaceId INT, LocationId INT NULL, EndOn DATETIME2(0), UserId INT NULL,
                           CustomerCode NVARCHAR(50) NULL, Desks INT NOT NULL);
    INSERT INTO @Leases (Id, SpaceId, LocationId, EndOn, UserId, CustomerCode, Desks)
    SELECT b.Id, b.SpaceId, sp.LocationId, b.EndOn, b.UserId, b.CustomerCode,
           CASE WHEN sp.Meeting = 1 THEN 0 ELSE ISNULL(COALESCE(NULLIF(b.Capacity, 0), sp.Capacity), 0) END
      FROM dbo.WN_Bookings b WITH (NOLOCK)
      JOIN @Spaces sp ON sp.Id = b.SpaceId
     WHERE ISNULL(b.IsDeleted, 0) = 0 AND b.BookingStatusId IN (2, 33) AND b.EndOn >= @Earliest;
    -- [9] renewals in desks: leases ending in the next @ExpiringDays days and in the next 30 days; desks on the leases
    --     that ended in the period (before now) and in the previous period
    SELECT (SELECT COUNT(*) FROM @Leases WHERE EndOn >= @Now AND EndOn <= @ExpEnd) AS ExpiringCount,
           (SELECT ISNULL(SUM(Desks), 0) FROM @Leases WHERE EndOn >= @Now AND EndOn <= @ExpEnd) AS ExpiringDesks,
           (SELECT COUNT(*) FROM @Leases WHERE EndOn >= @Now AND EndOn <= @Exp30End) AS Expiring30Count,
           (SELECT ISNULL(SUM(Desks), 0) FROM @Leases WHERE EndOn >= @Now AND EndOn <= @Exp30End) AS Expiring30Desks,
           (SELECT ISNULL(SUM(Desks), 0) FROM @Leases WHERE EndOn >= @PStart AND EndOn < @Now AND EndOn < @CEnd) AS EndedDesks,
           (SELECT ISNULL(SUM(Desks), 0) FROM @Leases WHERE EndOn >= @PrevStart AND EndOn < @PrevEnd) AS EndedDesksPrev;
    -- [10] leases ending in the next @ExpiringDays days with their desks, soonest first (as the CEO dashboard), first 50
    SELECT TOP 50 lv.Id AS BookingId,
           COALESCE(NULLIF(cust.Name, N''), NULLIF(u.Name, N''), NULLIF(u.UserName, N''), NULLIF(u.Email, N''), N'Customer') AS Customer,
           sp.Name AS Space, loc.Name AS Location, lv.EndOn, lv.Desks,
           DATEDIFF(DAY, @Today, CAST(lv.EndOn AS DATE)) AS DaysLeft
      FROM @Leases lv
      JOIN @Spaces sp ON sp.Id = lv.SpaceId
      LEFT JOIN @LocAll loc ON loc.Id = sp.LocationId
      LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = lv.UserId
     OUTER APPLY (SELECT TOP 1 COALESCE(NULLIF(cu.Company, N''), NULLIF(LTRIM(RTRIM(CONCAT(cu.FirstName, N' ', cu.LastName))), N''), cu.Email) AS Name
                    FROM dbo.WN_Customers cu WITH (NOLOCK)
                   WHERE (lv.CustomerCode IS NOT NULL AND cu.Code = lv.CustomerCode)
                      OR (lv.UserId IS NOT NULL AND cu.UserId = lv.UserId)
                      OR (u.Email IS NOT NULL AND cu.Email = u.Email)
                   ORDER BY CASE WHEN lv.CustomerCode IS NOT NULL AND cu.Code = lv.CustomerCode THEN 0
                                 WHEN cu.UserId = lv.UserId THEN 1 ELSE 2 END, cu.Id) cust
     WHERE lv.EndOn >= @Now AND lv.EndOn <= @ExpEnd
     ORDER BY lv.EndOn, lv.Id;
    -- [11] renewals by month: last 12 months and the next 3, oldest first (desks on the bookings created in the month,
    --      desks on the leases ending in the month and how many leases)
    ;WITH m AS (
        SELECT DATEADD(MONTH, -n, @MonthStart) AS MStart
          FROM (VALUES (-3),(-2),(-1),(0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) v(n)
    )
    SELECT CONVERT(CHAR(7), m.MStart, 126) AS [Month],
           (SELECT ISNULL(SUM(Desks), 0) FROM @New WHERE CreatedOn >= m.MStart AND CreatedOn < DATEADD(MONTH, 1, m.MStart)) AS BookedDesks,
           (SELECT ISNULL(SUM(Desks), 0) FROM @Leases WHERE EndOn >= m.MStart AND EndOn < DATEADD(MONTH, 1, m.MStart)) AS EndingDesks,
           (SELECT COUNT(*) FROM @Leases WHERE EndOn >= m.MStart AND EndOn < DATEADD(MONTH, 1, m.MStart)) AS Expiring
      FROM m
     ORDER BY m.MStart;
END;

GO

/****** Object:  StoredProcedure [dbo].[WN_Users_Insert]    Script Date: 04/11/2026 10:31:38 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_Insert]
    @Email          NVARCHAR(256),
    @PasswordHash   NVARCHAR(500) = NULL,
    @Name           NVARCHAR(250) = NULL,
    @PhoneNumber    NVARCHAR(MAX) = NULL,
    @RoleId         INT           = NULL,
    @CompanyId      INT           = NULL,
    @CityId         INT           = NULL,
    @Address        NVARCHAR(500) = NULL,
    @CnicOrPassport NVARCHAR(50)  = NULL,
    @AvatarUrl      NVARCHAR(500) = NULL,
    @Notes          NVARCHAR(1000)= NULL,
    @CreatedById    INT           = NULL,
    @LocationId     INT           = NULL,
    @ManagerId      INT           = NULL,
    @LocationIds    NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @TargetLocations TABLE (LocationId INT PRIMARY KEY);
        IF @LocationId IS NOT NULL INSERT INTO @TargetLocations VALUES (@LocationId);
        IF @LocationIds IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM STRING_SPLIT(@LocationIds, N',')
                       WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) IS NULL
                          OR TRY_CAST(LTRIM(RTRIM(value)) AS INT) <= 0)
                THROW 51010, 'LocationIds must contain positive location IDs.', 1;
            INSERT INTO @TargetLocations
            SELECT DISTINCT TRY_CAST(LTRIM(RTRIM(value)) AS INT)
            FROM STRING_SPLIT(@LocationIds, N',')
            WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) > 0
              AND NOT EXISTS (SELECT 1 FROM @TargetLocations t WHERE t.LocationId = TRY_CAST(LTRIM(RTRIM(value)) AS INT));
        END;

        IF @ManagerId IS NOT NULL
        BEGIN
            IF ISNULL(@RoleId, 0) <> 16
                THROW 51011, 'Only a sales_executive can have a ManagerId.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.WN_Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = @ManagerId AND IsActive = 1 AND RoleId = 18)
                THROW 51012, 'ManagerId must identify an active sales_manager.', 1;
            IF NOT EXISTS (
                SELECT 1
                FROM @TargetLocations e
                JOIN (
                    SELECT LocationId FROM dbo.WN_Users WHERE Id = @ManagerId AND LocationId IS NOT NULL
                    UNION
                    SELECT LocationId FROM dbo.WN_UserLocations WHERE UserId = @ManagerId
                ) m ON m.LocationId = e.LocationId
            )
                THROW 51013, 'The sales executive and manager must share at least one location.', 1;
            IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NULL
               AND EXISTS (SELECT 1 FROM @TargetLocations WHERE LocationId <> @LocationId)
               AND NOT EXISTS (SELECT 1 FROM dbo.WN_Users WHERE Id = @ManagerId AND LocationId = @LocationId)
                THROW 51014, 'WN_UserLocations is required to assign a manager through a secondary location.', 1;
        END;

        INSERT INTO dbo.WN_Users (
            IdGUID, Email, PasswordHash, Name, PhoneNumber,
            RoleId, CompanyId, CityId, Address, CnicOrPassport,
            AvatarUrl, Notes, IsActive, CreatedOn, CreatedById, LocationId, ManagerId
        ) VALUES (
            NEWID(), @Email, @PasswordHash, @Name, @PhoneNumber,
            @RoleId, @CompanyId, @CityId, @Address, @CnicOrPassport,
            @AvatarUrl, @Notes, 1, GETDATE(), @CreatedById, @LocationId, @ManagerId
        );

        DECLARE @NewUserId INT = CAST(SCOPE_IDENTITY() AS INT);
        IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL AND @LocationIds IS NOT NULL
        BEGIN
            INSERT INTO dbo.WN_UserLocations (UserId, LocationId, CreatedById)
            SELECT @NewUserId, t.LocationId, @CreatedById
            FROM @TargetLocations t
            WHERE NOT EXISTS (SELECT 1 FROM dbo.WN_UserLocations ul WHERE ul.UserId = @NewUserId AND ul.LocationId = t.LocationId);
        END;

        IF @NewUserId IS NOT NULL AND @Email IS NOT NULL AND LTRIM(RTRIM(@Email)) <> ''
        BEGIN
            UPDATE dbo.WN_Customers
            SET UserId = @NewUserId
            WHERE (UserId IS NULL OR UserId <= 0)
              AND LOWER(LTRIM(RTRIM(Email))) = LOWER(LTRIM(RTRIM(@Email)))
              AND NOT EXISTS (SELECT 1 FROM dbo.WN_Customers c2 WITH (NOLOCK) WHERE c2.UserId = @NewUserId);
        END;

        COMMIT TRANSACTION;
        SELECT
            u.Id, u.IdGUID AS PublicId, u.IdGUID, u.Email, u.Name, u.PhoneNumber,
            u.RoleId, u.CompanyId, u.CityId, u.Address, u.CnicOrPassport, u.AvatarUrl,
            u.IsActive, u.Notes, u.CreatedOn, u.LocationId, u.ManagerId
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.Id = @NewUserId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/****** Object:  StoredProcedure [dbo].[WN_Users_SetRole]    Script Date: 04/11/2026 10:31:38 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_SetRole]
    @Id                    INT,
    @RoleId                INT,
    @LocationId            INT = NULL,
    @LocationIdSpecified   BIT = 0,
    @ManagerId             INT = NULL,
    @ManagerIdSpecified    BIT = 0,
    @LocationIds           NVARCHAR(MAX) = NULL,
    @LocationIdsSpecified  BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.WN_Users
        SET RoleId = @RoleId,
            LocationId = CASE WHEN @LocationIdSpecified = 1 THEN @LocationId ELSE LocationId END
        WHERE Id = @Id;
        IF @@ROWCOUNT = 0 THROW 51015, 'User not found.', 1;

        EXEC dbo.WN_Users_Update
            @Id = @Id,
            @LocationId = @LocationId,
            @ManagerId = @ManagerId,
            @ManagerIdSpecified = @ManagerIdSpecified,
            @LocationIds = @LocationIds,
            @LocationIdsSpecified = @LocationIdsSpecified,
            @LocationIdSpecified = @LocationIdSpecified;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
GO

/****** Object:  StoredProcedure [dbo].[WN_Users_Update]    Script Date: 04/11/2026 10:31:38 am ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_Update]
    @Id             INT,
    @Name           NVARCHAR(200)  = NULL,
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @CompanyId      INT            = NULL,
    @CityId         INT            = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @AvatarUrl      NVARCHAR(500)  = NULL,
    @Notes          NVARCHAR(1000) = NULL,
    @LocationId            INT            = NULL,
    @Email                 NVARCHAR(256)  = NULL,
    @ManagerId             INT            = NULL,
    @ManagerIdSpecified    BIT            = 0,
    @LocationIds           NVARCHAR(MAX)  = NULL,
    @LocationIdsSpecified  BIT            = 0,
    @LocationIdSpecified   BIT            = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @RoleId INT, @CurrentManagerId INT, @CurrentLocationId INT;
        SELECT @RoleId = RoleId, @CurrentManagerId = ManagerId, @CurrentLocationId = LocationId
        FROM dbo.WN_Users WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id;
        IF @@ROWCOUNT = 0 THROW 51016, 'User not found.', 1;

        DECLARE @TargetLocationId INT = CASE WHEN @LocationIdSpecified = 1 THEN @LocationId ELSE @CurrentLocationId END;
        DECLARE @TargetLocations TABLE (LocationId INT PRIMARY KEY);
        IF @TargetLocationId IS NOT NULL INSERT INTO @TargetLocations VALUES (@TargetLocationId);

        IF @LocationIdsSpecified = 1
        BEGIN
            IF @LocationIds IS NOT NULL AND EXISTS (SELECT 1 FROM STRING_SPLIT(@LocationIds, N',')
                       WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) IS NULL
                          OR TRY_CAST(LTRIM(RTRIM(value)) AS INT) <= 0)
                THROW 51017, 'LocationIds must contain positive location IDs.', 1;

            IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.WN_UserLocations WHERE UserId = @Id;
                IF @LocationIds IS NOT NULL
                    INSERT INTO dbo.WN_UserLocations (UserId, LocationId)
                    SELECT @Id, parsed.LocationId
                    FROM (
                        SELECT DISTINCT TRY_CAST(LTRIM(RTRIM(value)) AS INT) AS LocationId
                        FROM STRING_SPLIT(@LocationIds, N',')
                        WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) > 0
                    ) parsed
                    WHERE NOT EXISTS (SELECT 1 FROM dbo.WN_UserLocations ul WHERE ul.UserId = @Id AND ul.LocationId = parsed.LocationId);
            END;
            IF @LocationIds IS NOT NULL
                INSERT INTO @TargetLocations
                SELECT DISTINCT TRY_CAST(LTRIM(RTRIM(value)) AS INT)
                FROM STRING_SPLIT(@LocationIds, N',')
                WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) > 0
                  AND NOT EXISTS (SELECT 1 FROM @TargetLocations t WHERE t.LocationId = TRY_CAST(LTRIM(RTRIM(value)) AS INT));
        END
        ELSE IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
            INSERT INTO @TargetLocations
            SELECT DISTINCT LocationId FROM dbo.WN_UserLocations
            WHERE UserId = @Id AND NOT EXISTS (SELECT 1 FROM @TargetLocations t WHERE t.LocationId = dbo.WN_UserLocations.LocationId);

        DECLARE @TargetManagerId INT = CASE WHEN @ManagerIdSpecified = 1 THEN @ManagerId ELSE @CurrentManagerId END;
        IF @ManagerIdSpecified = 1 AND @TargetManagerId = @Id
            THROW 51021, 'A user cannot be their own ManagerId.', 1;
        IF ISNULL(@RoleId, 0) <> 16
        BEGIN
            IF @ManagerIdSpecified = 1 AND @ManagerId IS NOT NULL
                THROW 51018, 'Only a sales_executive can have a ManagerId.', 1;
            SET @TargetManagerId = NULL;
        END;

        IF @TargetManagerId IS NOT NULL
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.WN_Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TargetManagerId AND IsActive = 1 AND RoleId = 18)
            BEGIN
                IF @ManagerIdSpecified = 1 AND ISNULL(@TargetManagerId, -1) <> ISNULL(@CurrentManagerId, -1)
                    THROW 51019, 'ManagerId must identify an active sales_manager.', 1;
                SET @TargetManagerId = NULL;
            END
            ELSE IF NOT EXISTS (
                SELECT 1 FROM @TargetLocations e
                JOIN (
                    SELECT LocationId FROM dbo.WN_Users WHERE Id = @TargetManagerId AND LocationId IS NOT NULL
                    UNION
                    SELECT LocationId FROM dbo.WN_UserLocations WHERE UserId = @TargetManagerId
                ) m ON m.LocationId = e.LocationId)
            BEGIN
                IF @ManagerIdSpecified = 1 AND ISNULL(@TargetManagerId, -1) <> ISNULL(@CurrentManagerId, -1)
                    THROW 51020, 'The sales executive and manager must share at least one location.', 1;
                SET @TargetManagerId = NULL;
            END;
        END;

        UPDATE dbo.WN_Users SET
            Name           = ISNULL(@Name,           Name),
            PhoneNumber    = ISNULL(@PhoneNumber,     PhoneNumber),
            CompanyId      = ISNULL(@CompanyId,       CompanyId),
            CityId         = ISNULL(@CityId,          CityId),
            Address        = ISNULL(@Address,         Address),
            CnicOrPassport = ISNULL(@CnicOrPassport,  CnicOrPassport),
            AvatarUrl      = ISNULL(@AvatarUrl,       AvatarUrl),
            Notes          = ISNULL(@Notes,           Notes),
            LocationId     = CASE WHEN @LocationIdSpecified = 1 THEN @LocationId ELSE LocationId END,
            Email          = ISNULL(@Email,           Email),
            ManagerId      = @TargetManagerId
        WHERE Id = @Id;

        IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
        BEGIN
            UPDATE report
            SET ManagerId = NULL
            FROM dbo.WN_Users report
            WHERE report.ManagerId = @Id
              AND (
                    NOT EXISTS (
                        SELECT 1
                        FROM dbo.WN_Users manager
                        WHERE manager.Id = @Id
                          AND (
                            (manager.LocationId IS NOT NULL AND manager.LocationId = report.LocationId)
                            OR (manager.LocationId IS NOT NULL AND EXISTS (
                                SELECT 1 FROM dbo.WN_UserLocations reportLocation
                                WHERE reportLocation.UserId = report.Id AND reportLocation.LocationId = manager.LocationId))
                            OR (report.LocationId IS NOT NULL AND EXISTS (
                                SELECT 1 FROM dbo.WN_UserLocations managerLocation
                                WHERE managerLocation.UserId = @Id AND managerLocation.LocationId = report.LocationId))
                            OR EXISTS (
                                SELECT 1
                                FROM dbo.WN_UserLocations reportLocation
                                JOIN dbo.WN_UserLocations managerLocation ON managerLocation.LocationId = reportLocation.LocationId
                                WHERE reportLocation.UserId = report.Id AND managerLocation.UserId = @Id)
                          )
                    )
                  );
        END
        ELSE
        BEGIN
            UPDATE report
            SET ManagerId = NULL
            FROM dbo.WN_Users report
            WHERE report.ManagerId = @Id
              AND (report.LocationId IS NULL
                   OR NOT EXISTS (SELECT 1 FROM dbo.WN_Users manager WHERE manager.Id = @Id AND manager.LocationId = report.LocationId));
        END;

        UPDATE report
        SET ManagerId = NULL
        FROM dbo.WN_Users report
        WHERE report.ManagerId = @Id
          AND NOT EXISTS (SELECT 1 FROM dbo.WN_Users manager WHERE manager.Id = @Id AND manager.IsActive = 1 AND manager.RoleId = 18);

        COMMIT TRANSACTION;
        SELECT 1 AS AffectedRows;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
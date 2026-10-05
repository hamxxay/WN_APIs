USE [SAC400]
GO
/****** Object:  StoredProcedure [dbo].[WN_AccessCards_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 5. WN_AccessCards_Delete
CREATE OR ALTER PROCEDURE [dbo].[WN_AccessCards_Delete]
    @Id NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.WN_AccessCards
    WHERE (TRY_CAST(@Id AS INT) IS NOT NULL AND Id = TRY_CAST(@Id AS INT))
       OR (TRY_CAST(@Id AS UNIQUEIDENTIFIER) IS NOT NULL AND IdGUID = TRY_CAST(@Id AS UNIQUEIDENTIFIER))
       OR (CardNumber = @Id);
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AccessCards_GetById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 2. WN_AccessCards_GetById
CREATE OR ALTER PROCEDURE [dbo].[WN_AccessCards_GetById]
    @Id NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        ac.Id,
        ac.IdGUID,
        ac.LocationId,
        l.Name AS LocationName,
        ac.CustomerId,
        COALESCE(NULLIF(RTRIM(LTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), u.Name, 'Customer') AS CustomerName,
        COALESCE(c.Email, u.Email) AS CustomerEmail,
        ac.BookingId,
        ac.SpaceId,
        s.Name AS SpaceName,
        s.Code AS SpaceCode,
        ac.CardNumber,
        ac.StartDate,
        ac.EndDate,
        ac.Status,
        CASE 
            WHEN ac.Status = 1 THEN 'Active'
            WHEN ac.Status = 2 THEN 'Restricted'
            WHEN ac.Status = 3 THEN 'Inactive'
            ELSE 'Unknown'
        END AS StatusLabel,
        ac.CreatedOn,
        ac.UpdatedOn
    FROM dbo.WN_AccessCards ac WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = ac.LocationId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Id = ac.CustomerId
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = c.UserId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = ac.SpaceId
    WHERE (TRY_CAST(@Id AS INT) IS NOT NULL AND ac.Id = TRY_CAST(@Id AS INT))
       OR (TRY_CAST(@Id AS UNIQUEIDENTIFIER) IS NOT NULL AND ac.IdGUID = TRY_CAST(@Id AS UNIQUEIDENTIFIER))
       OR (ac.CardNumber = @Id);
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AccessCards_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 1. WN_AccessCards_GetList
CREATE OR ALTER PROCEDURE [dbo].[WN_AccessCards_GetList]
    @Page INT = 1,
    @Limit INT = 20,
    @Search NVARCHAR(255) = NULL,
    @BookingId INT = NULL,
    @CustomerId INT = NULL,
    @SpaceId INT = NULL,
    @Status INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @Page = ISNULL(NULLIF(@Page, 0), 1);
    SET @Limit = ISNULL(NULLIF(@Limit, 0), 20);

    SELECT 
        ac.Id,
        ac.IdGUID,
        ac.LocationId,
        l.Name AS LocationName,
        ac.CustomerId,
        COALESCE(NULLIF(RTRIM(LTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), u.Name, 'Customer') AS CustomerName,
        COALESCE(c.Email, u.Email) AS CustomerEmail,
        ac.BookingId,
        ac.SpaceId,
        s.Name AS SpaceName,
        s.Code AS SpaceCode,
        ac.CardNumber,
        ac.StartDate,
        ac.EndDate,
        ac.Status,
        CASE 
            WHEN ac.Status = 1 THEN 'Active'
            WHEN ac.Status = 2 THEN 'Restricted'
            WHEN ac.Status = 3 THEN 'Inactive'
            ELSE 'Unknown'
        END AS StatusLabel,
        ac.CreatedOn,
        ac.UpdatedOn,
        COUNT(*) OVER() AS TotalCount
    FROM dbo.WN_AccessCards ac WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = ac.LocationId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Id = ac.CustomerId
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = c.UserId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = ac.SpaceId
    WHERE (@BookingId IS NULL OR ac.BookingId = @BookingId)
      AND (@CustomerId IS NULL OR ac.CustomerId = @CustomerId)
      AND (@SpaceId IS NULL OR ac.SpaceId = @SpaceId)
      AND (@Status IS NULL OR ac.Status = @Status)
      AND (
          @Search IS NULL OR @Search = '' 
          OR ac.CardNumber LIKE '%' + @Search + '%' 
          OR c.FirstName LIKE '%' + @Search + '%' 
          OR c.LastName LIKE '%' + @Search + '%' 
          OR c.Email LIKE '%' + @Search + '%'
          OR s.Name LIKE '%' + @Search + '%'
      )
    ORDER BY ac.Id DESC
    OFFSET (@Page - 1) * @Limit ROWS
    FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AccessCards_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3. WN_AccessCards_Insert
CREATE OR ALTER PROCEDURE [dbo].[WN_AccessCards_Insert]
    @LocationId INT,
    @CustomerId INT,
    @BookingId INT,
    @SpaceId INT,
    @CardNumber NVARCHAR(100) = NULL,
    @StartDate DATETIME,
    @EndDate DATETIME,
    @Status INT = 1,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @CardNumber IS NULL OR RTRIM(LTRIM(@CardNumber)) = ''
    BEGIN
        SET @CardNumber = 'AC-' + CAST(@BookingId AS NVARCHAR(20)) + '-' + RIGHT(REPLACE(CAST(NEWID() AS NVARCHAR(36)), '-', ''), 4);
    END;

    INSERT INTO dbo.WN_AccessCards (
        IdGUID, LocationId, CustomerId, BookingId, SpaceId, CardNumber, StartDate, EndDate, Status, CreatedOn, CreatedById
    )
    VALUES (
        NEWID(), @LocationId, @CustomerId, @BookingId, @SpaceId, @CardNumber, @StartDate, @EndDate, @Status, SYSUTCDATETIME(), @CreatedById
    );

    DECLARE @NewId INT = SCOPE_IDENTITY();

    EXEC dbo.WN_AccessCards_GetById @Id = @NewId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AccessCards_PopulateAllBookings]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3. Stored Procedure to populate and sync cards for active/valid bookings using Invoice Rent Periods
CREATE OR ALTER PROCEDURE [dbo].[WN_AccessCards_PopulateAllBookings]
    @TargetBookingId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Clean up cards if target booking was cancelled or deleted
    IF @TargetBookingId IS NOT NULL
    BEGIN
        DELETE ac
        FROM dbo.WN_AccessCards ac
        JOIN dbo.WN_Bookings b ON b.Id = ac.BookingId
        WHERE ac.BookingId = @TargetBookingId
          AND (b.IsDeleted = 1 OR b.BookingStatusId IN (3, 4));
    END;

    -- Update existing access cards with correct invoice rent period start/end dates and status
    UPDATE ac
    SET 
        ac.StartDate = ISNULL(inv.BillingPeriodStart, b.StartOn),
        ac.EndDate   = ISNULL(inv.BillingPeriodEnd, DATEADD(MONTH, ISNULL(NULLIF(b.AdvanceRentMonths, 0), 1), b.StartOn)),
        ac.Status    = CASE 
                        WHEN ISNULL(inv.BillingPeriodEnd, DATEADD(MONTH, ISNULL(NULLIF(b.AdvanceRentMonths, 0), 1), b.StartOn)) < SYSUTCDATETIME() THEN 3 -- Inactive/Expired
                        ELSE 1 -- Active
                       END,
        ac.UpdatedOn = SYSUTCDATETIME()
    FROM dbo.WN_AccessCards ac
    JOIN dbo.WN_Bookings b ON b.Id = ac.BookingId
    OUTER APPLY (
        SELECT TOP 1 BillingPeriodStart, BillingPeriodEnd
        FROM dbo.WN_Invoices
        WHERE BookingId = b.Id AND IsDeleted = 0
        ORDER BY Id ASC
    ) inv
    WHERE (@TargetBookingId IS NULL OR b.Id = @TargetBookingId);

    -- Insert missing cards up to (Capacity + 1)
    WITH Numbers AS (
        SELECT TOP 500 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Seq
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    )
    INSERT INTO dbo.WN_AccessCards (
        IdGUID,
        LocationId,
        CustomerId,
        BookingId,
        SpaceId,
        CardNumber,
        StartDate,
        EndDate,
        Status,
        CreatedOn,
        UpdatedOn,
        CreatedById,
        UpdatedById
    )
    SELECT 
        NEWID() AS IdGUID,
        CASE 
            WHEN EXISTS (SELECT 1 FROM dbo.WN_Locations l WHERE l.Id = s.LocationId) THEN s.LocationId
            ELSE (SELECT TOP 1 Id FROM dbo.WN_Locations ORDER BY Id ASC)
        END AS LocationId,
        COALESCE(
            c_code.Id,
            c_user.Id,
            c_email.Id,
            (SELECT TOP 1 Id FROM dbo.WN_Customers ORDER BY Id ASC)
        ) AS CustomerId,
        b.Id AS BookingId,
        b.SpaceId AS SpaceId,
        'AC-' + CAST(b.Id AS NVARCHAR(20)) + '-' + RIGHT('00' + CAST(n.Seq AS NVARCHAR(10)), 2) AS CardNumber,
        ISNULL(inv.BillingPeriodStart, b.StartOn) AS StartDate,
        ISNULL(inv.BillingPeriodEnd, DATEADD(MONTH, ISNULL(NULLIF(b.AdvanceRentMonths, 0), 1), b.StartOn)) AS EndDate,
        CASE 
            WHEN ISNULL(inv.BillingPeriodEnd, DATEADD(MONTH, ISNULL(NULLIF(b.AdvanceRentMonths, 0), 1), b.StartOn)) < SYSUTCDATETIME() THEN 3 -- Inactive/Expired
            ELSE 1 -- Active
        END AS Status,
        SYSUTCDATETIME() AS CreatedOn,
        NULL AS UpdatedOn,
        CASE 
            WHEN EXISTS (SELECT 1 FROM dbo.WN_Users u WHERE u.Id = b.CreatedById) THEN b.CreatedById
            WHEN EXISTS (SELECT 1 FROM dbo.WN_Users u WHERE u.Id = b.UserId) THEN b.UserId
            ELSE NULL
        END AS CreatedById,
        NULL AS UpdatedById
    FROM dbo.WN_Bookings b
    JOIN dbo.WN_Spaces s ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Users u ON u.Id = b.UserId
    LEFT JOIN dbo.WN_Customers c_code ON c_code.Code = b.CustomerCode
    LEFT JOIN dbo.WN_Customers c_user ON c_user.UserId = b.UserId
    LEFT JOIN dbo.WN_Customers c_email ON c_email.Email = u.Email
    OUTER APPLY (
        SELECT TOP 1 BillingPeriodStart, BillingPeriodEnd
        FROM dbo.WN_Invoices
        WHERE BookingId = b.Id AND IsDeleted = 0
        ORDER BY Id ASC
    ) inv
    CROSS JOIN Numbers n
    WHERE b.IsDeleted = 0
      AND b.BookingStatusId NOT IN (3, 4) -- Exclude Cancelled (3) and Rejected (4)
      AND (@TargetBookingId IS NULL OR b.Id = @TargetBookingId)
      AND n.Seq <= (ISNULL(NULLIF(s.Capacity, 0), 1) + 1)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_AccessCards ac 
          WHERE ac.BookingId = b.Id AND ac.CardNumber = 'AC-' + CAST(b.Id AS NVARCHAR(20)) + '-' + RIGHT('00' + CAST(n.Seq AS NVARCHAR(10)), 2)
      )
    ORDER BY b.Id, n.Seq;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AccessCards_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 4. WN_AccessCards_Update
CREATE OR ALTER PROCEDURE [dbo].[WN_AccessCards_Update]
    @Id NVARCHAR(100),
    @LocationId INT = NULL,
    @CustomerId INT = NULL,
    @BookingId INT = NULL,
    @SpaceId INT = NULL,
    @CardNumber NVARCHAR(100) = NULL,
    @StartDate DATETIME = NULL,
    @EndDate DATETIME = NULL,
    @Status INT = NULL,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WN_AccessCards
    SET 
        LocationId  = ISNULL(@LocationId, LocationId),
        CustomerId  = ISNULL(@CustomerId, CustomerId),
        BookingId   = ISNULL(@BookingId, BookingId),
        SpaceId     = ISNULL(@SpaceId, SpaceId),
        CardNumber  = ISNULL(@CardNumber, CardNumber),
        StartDate   = ISNULL(@StartDate, StartDate),
        EndDate     = ISNULL(@EndDate, EndDate),
        Status      = ISNULL(@Status, Status),
        UpdatedOn   = SYSUTCDATETIME(),
        UpdatedById = ISNULL(@UpdatedById, UpdatedById)
    WHERE (TRY_CAST(@Id AS INT) IS NOT NULL AND Id = TRY_CAST(@Id AS INT))
       OR (TRY_CAST(@Id AS UNIQUEIDENTIFIER) IS NOT NULL AND IdGUID = TRY_CAST(@Id AS UNIQUEIDENTIFIER))
       OR (CardNumber = @Id);
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AccountsCOA_GetById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Returns a single account by its primary key.
CREATE OR ALTER PROCEDURE [dbo].[WN_AccountsCOA_GetById]
    @AccountId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
      Id,
        Description
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE Id = @AccountId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AccountsCOA_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Returns all accounts from dbo.AccountsCOA sorted alphabetically by Description.
CREATE OR ALTER PROCEDURE [dbo].[WN_AccountsCOA_GetList]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Description
    FROM dbo.AccountsCOA WITH (NOLOCK)
    ORDER BY Description ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AddAttendant]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_AddAttendant]
    @Name NVARCHAR(150),
    @Email NVARCHAR(150),
    @Phone NVARCHAR(20),
    @IdType VARCHAR(10),
    @IdNumber VARCHAR(30),
    @CustomerId INT,
    @PersonId INT OUTPUT,
    @PersonGuid UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Clean & Normalize inputs to raw formats (no spaces, no special characters)
    SET @Name = TRIM(@Name);
    SET @Email = LOWER(TRIM(@Email));
    SET @Phone = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(TRIM(@Phone), '+', ''), '-', ''), ' ', ''), '(', ''), ')', ''), '.', '');
    SET @IdType = CASE WHEN UPPER(TRIM(@IdType)) = 'PASSPORT' THEN 'Passport' ELSE 'CNIC' END;
    SET @IdNumber = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(TRIM(@IdNumber), '-', ''), ' ', ''), '.', ''), '/', ''), '\', '');

    -- Upsert WN_Persons based on (IdType, IdNumber)
    SELECT @PersonId = PersonId, @PersonGuid = PersonGuid
    FROM [dbo].[WN_Persons]
    WHERE IdType = @IdType AND IdNumber = @IdNumber;

    IF @PersonId IS NULL
    BEGIN
        SET @PersonGuid = NEWID();
        INSERT INTO [dbo].[WN_Persons] (PersonGuid, Name, Email, Phone, IdType, IdNumber, CreatedAt)
        VALUES (@PersonGuid, @Name, @Email, @Phone, @IdType, @IdNumber, SYSUTCDATETIME());
        
        SET @PersonId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        -- Update contact details if person already exists
        UPDATE [dbo].[WN_Persons]
        SET Name = @Name, Email = @Email, Phone = @Phone
        WHERE PersonId = @PersonId;
    END

    -- Link to CustomerAttendants
    IF EXISTS (SELECT 1 FROM [dbo].[WN_CustomerAttendants] WHERE PersonId = @PersonId AND CustomerId = @CustomerId)
    BEGIN
        UPDATE [dbo].[WN_CustomerAttendants]
        SET IsActive = 1, DeactivatedAt = NULL
        WHERE PersonId = @PersonId AND CustomerId = @CustomerId;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[WN_CustomerAttendants] (PersonId, CustomerId, IsActive, CreatedAt)
        VALUES (@PersonId, @CustomerId, 1, SYSUTCDATETIME());
    END

    SELECT @PersonId AS PersonId, @PersonGuid AS PersonGuid;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Agreements_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ====================================================================
-- 2. Stored Procedure: dbo.WN_Agreements_GetList
-- ====================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Agreements_GetList]
    @Page INT = 1,
    @Limit INT = 50,
    @Search NVARCHAR(100) = NULL,
    @Status NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Offset INT = (@Page - 1) * @Limit;

    -- Total Count
    SELECT COUNT(1)
    FROM dbo.WN_Agreements a WITH (NOLOCK)
    JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
    WHERE (@Status IS NULL OR a.Status = @Status)
      AND (@Search IS NULL OR 
           a.CustomerName LIKE '%' + @Search + '%' OR 
           a.CompanyName LIKE '%' + @Search + '%' OR 
           q.QuotationNumber LIKE '%' + @Search + '%' OR
           a.CustomerCnic LIKE '%' + @Search + '%');

    -- Paginated Rows
    SELECT 
        a.Id,
        a.QuotationId,
        q.QuotationNumber,
        a.BookingId,
        a.CustomerId,
        a.EntityType,
        a.Status,
        a.SentDate,
        a.SignedDate,
        a.RefundDays,
        a.FeeAmount,
        a.SecurityDeposit,
        a.OperatingHours,
        a.CustomerName,
        a.CustomerCnic,
        a.CustomerPhone,
        a.CustomerAddress,
        a.CompanyName,
        a.Ntn,
        a.SecpRegistrationNo,
        a.TemplateVersionId,
        t.Name AS TemplateName,
        a.SignedPdfPath,
        a.SignedPdfUploadedAt,
        a.CreatedOn,
        a.UpdatedOn
    FROM dbo.WN_Agreements a WITH (NOLOCK)
    JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
    LEFT JOIN dbo.WN_LeaseTemplates t WITH (NOLOCK) ON t.Id = a.TemplateVersionId
    WHERE (@Status IS NULL OR a.Status = @Status)
      AND (@Search IS NULL OR 
           a.CustomerName LIKE '%' + @Search + '%' OR 
           a.CompanyName LIKE '%' + @Search + '%' OR 
           q.QuotationNumber LIKE '%' + @Search + '%' OR
           a.CustomerCnic LIKE '%' + @Search + '%')
    ORDER BY a.Id DESC
    OFFSET @Offset ROWS
    FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Amenities_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Amenities_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Icon, IsActive
    FROM dbo.WN_Amenities WITH (NOLOCK)
    WHERE IsActive = 1
    ORDER BY Name ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Amenities_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Amenities_Insert]
    @Name NVARCHAR(100),
    @Icon NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.WN_Amenities WHERE Name = @Name)
    BEGIN
        SELECT Id FROM dbo.WN_Amenities WHERE Name = @Name;
        RETURN;
    END
    INSERT INTO dbo.WN_Amenities (Name, Icon) VALUES (@Name, @Icon);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AmountFields_GetByEntityField]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_AmountFields_GetByEntityField]
    @Entity NVARCHAR(100),
    @Field  NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Entity, Field, Label, Currency
    FROM dbo.WN_AmountFields
    WHERE Entity = @Entity AND Field = @Field;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AmountFields_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 1. Repoint dbo.WN_AmountFields_GetList to dbo.WN_ChargeTypeAccountMapping
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_AmountFields_GetList]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        ct.Id,
        ct.Code,
        ct.Label,
        ct.IsDebit,
        ct.IsActive,
        m.RentAccountId,
        m.AccountReceivableId,
        m.ServicesIncomeId,
        m.SalesTaxId,
        m.SecurityReceivedId,
        -- Backward compatibility for frontend / AmountFieldDto
        COALESCE(m.RentAccountId, m.ServicesIncomeId, m.SecurityReceivedId, m.SalesTaxId, m.AccountReceivableId) AS AccountId,
        coa.Description AS AccountDescription
    FROM dbo.WN_ChargeTypes ct
    LEFT JOIN dbo.WN_ChargeTypeAccountMapping m 
           ON m.ChargeTypeId = ct.Id 
          AND (m.EffectiveTo IS NULL OR m.EffectiveTo >= CAST(GETDATE() AS DATE))
    LEFT JOIN dbo.AccountsCOA coa 
           ON coa.Id = COALESCE(m.RentAccountId, m.ServicesIncomeId, m.SecurityReceivedId, m.SalesTaxId, m.AccountReceivableId)
    WHERE ct.IsActive = 1
    ORDER BY ct.Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_AmountFields_UpdateAccount]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 2. Repoint dbo.WN_AmountFields_UpdateAccount to dbo.WN_ChargeTypeAccountMapping
-- ==========================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_AmountFields_UpdateAccount]
    @Id        INT,
    @AccountId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = @Id)
    BEGIN
        UPDATE dbo.WN_ChargeTypeAccountMapping
        SET RentAccountId      = CASE WHEN @Id = 1 THEN @AccountId ELSE RentAccountId END,
            SecurityReceivedId = CASE WHEN @Id = 2 THEN @AccountId ELSE SecurityReceivedId END,
            SalesTaxId         = CASE WHEN @Id = 3 THEN @AccountId ELSE SalesTaxId END,
            ServicesIncomeId   = CASE WHEN @Id = 4 THEN @AccountId ELSE ServicesIncomeId END
        WHERE ChargeTypeId = @Id;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_ChargeTypeAccountMapping 
            (ChargeTypeId, RentAccountId, SecurityReceivedId, SalesTaxId, ServicesIncomeId, EffectiveFrom)
        VALUES 
            (@Id, 
             CASE WHEN @Id = 1 THEN @AccountId ELSE NULL END,
             CASE WHEN @Id = 2 THEN @AccountId ELSE NULL END,
             CASE WHEN @Id = 3 THEN @AccountId ELSE NULL END,
             CASE WHEN @Id = 4 THEN @AccountId ELSE NULL END,
             CAST(GETDATE() AS DATE));
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_CheckTerminalStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 11. Stored Procedure: dbo.WN_Announcements_CheckTerminalStatus
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_CheckTerminalStatus]
    @AnnouncementId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- If all recipients are in terminal states ('Sent', 'Failed', 'Read')
    IF NOT EXISTS (
        SELECT 1 
        FROM dbo.WN_AnnouncementRecipient WITH (NOLOCK)
        WHERE AnnouncementId = @AnnouncementId AND Status = 'Pending'
    )
    BEGIN
        UPDATE dbo.WN_Announcement
        SET Status = 'Sent',
            SentAt = ISNULL(SentAt, SYSUTCDATETIME())
        WHERE Id = @AnnouncementId
          AND Status IN ('Queued', 'Sending', 'Draft');
    END
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_Create]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 4. Stored Procedure: dbo.WN_Announcements_Create
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_Create]
    @Title           NVARCHAR(200),
    @Body            NVARCHAR(MAX),
    @Type            NVARCHAR(20),
    @TargetScope     NVARCHAR(20),
    @LocationId      INT           = NULL,
    @SpaceId         INT           = NULL,
    @CustomUserIds   NVARCHAR(MAX) = NULL,
    @ScheduledAt     DATETIME2     = NULL,
    @CreatedBy       INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    DECLARE @InitialStatus NVARCHAR(20);

    IF @ScheduledAt IS NOT NULL AND @ScheduledAt > SYSUTCDATETIME()
        SET @InitialStatus = 'Draft';
    ELSE
        SET @InitialStatus = 'Queued';

    -- Insert Announcement Header
    INSERT INTO dbo.WN_Announcement (
        Id, Title, Body, Type, TargetScope, LocationId, SpaceId, ScheduledAt, Status, CreatedBy, CreatedAt
    )
    VALUES (
        @NewId, @Title, @Body, @Type, @TargetScope, @LocationId, @SpaceId, @ScheduledAt, @InitialStatus, @CreatedBy, SYSUTCDATETIME()
    );

    -- Audience Resolution Table
    DECLARE @ResolvedUsers TABLE (UserId INT PRIMARY KEY);

    IF @TargetScope = 'All'
    BEGIN
        INSERT INTO @ResolvedUsers (UserId)
        SELECT DISTINCT u.Id
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.IsActive = 1;
    END
    ELSE IF @TargetScope = 'Location' AND @LocationId IS NOT NULL
    BEGIN
        INSERT INTO @ResolvedUsers (UserId)
        SELECT DISTINCT u.Id
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.IsActive = 1
          AND (
              u.LocationId = @LocationId
              OR
              u.Id IN (
                  SELECT b.UserId 
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  INNER JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                  WHERE b.UserId IS NOT NULL 
                    AND s.LocationId = @LocationId
                    AND ISNULL(b.IsDeleted, 0) = 0
              )
          );
    END
    ELSE IF @TargetScope = 'Space' AND @SpaceId IS NOT NULL
    BEGIN
        INSERT INTO @ResolvedUsers (UserId)
        SELECT DISTINCT u.Id
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.IsActive = 1
          AND (
              u.Id IN (
                  SELECT b.UserId
                  FROM dbo.WN_Bookings b WITH (NOLOCK)
                  WHERE b.UserId IS NOT NULL 
                    AND b.SpaceId = @SpaceId
                    AND ISNULL(b.IsDeleted, 0) = 0
              )
          );
    END
    ELSE IF @TargetScope = 'CustomList' AND @CustomUserIds IS NOT NULL
    BEGIN
        INSERT INTO @ResolvedUsers (UserId)
        SELECT DISTINCT u.Id
        FROM dbo.WN_Users u WITH (NOLOCK)
        INNER JOIN (
            SELECT CAST(LTRIM(RTRIM(value)) AS INT) AS TargetUserId
            FROM STRING_SPLIT(@CustomUserIds, ',')
            WHERE LTRIM(RTRIM(value)) <> '' AND ISNUMERIC(LTRIM(RTRIM(value))) = 1
        ) parsed ON parsed.TargetUserId = u.Id
        WHERE u.IsActive = 1;
    END

    -- Bulk insert recipients at creation/queue time for both Push and Email channels
    INSERT INTO dbo.WN_AnnouncementRecipient (
        AnnouncementId, UserId, Channel, Status, RetryCount, SentAt
    )
    SELECT @NewId, ru.UserId, 'Push', 'Pending', 0, NULL
    FROM @ResolvedUsers ru;

    INSERT INTO dbo.WN_AnnouncementRecipient (
        AnnouncementId, UserId, Channel, Status, RetryCount, SentAt
    )
    SELECT @NewId, ru.UserId, 'Email', 'Pending', 0, NULL
    FROM @ResolvedUsers ru;

    -- Return Created Announcement with delivery stats
    EXEC dbo.WN_Announcements_GetById @AnnouncementId = @NewId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_GetById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 6. Stored Procedure: dbo.WN_Announcements_GetById
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_GetById]
    @AnnouncementId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: Header + Aggregate Stats
    SELECT 
        a.Id,
        a.Title,
        a.Body,
        a.Type,
        a.TargetScope,
        a.LocationId,
        loc.Name AS LocationName,
        a.SpaceId,
        sp.Name AS SpaceName,
        a.ScheduledAt,
        a.SentAt,
        a.Status,
        a.CreatedBy,
        u.Name AS CreatedByName,
        a.CreatedAt,
        COUNT(r.Id) AS TotalRecipients,
        COUNT(DISTINCT r.UserId) AS TotalUsers,
        SUM(CASE WHEN r.Status = 'Sent' THEN 1 ELSE 0 END) AS SentCount,
        SUM(CASE WHEN r.Status = 'Failed' THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN r.Status = 'Read' THEN 1 ELSE 0 END) AS ReadCount,
        SUM(CASE WHEN r.Status = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
        SUM(CASE WHEN r.Channel = 'Push' AND r.Status IN ('Sent','Read') THEN 1 ELSE 0 END) AS PushSentCount,
        SUM(CASE WHEN r.Channel = 'Email' AND r.Status IN ('Sent','Read') THEN 1 ELSE 0 END) AS EmailSentCount
    FROM dbo.WN_Announcement a WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = a.LocationId
    LEFT JOIN dbo.WN_Spaces sp WITH (NOLOCK) ON sp.Id = a.SpaceId
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = a.CreatedBy
    LEFT JOIN dbo.WN_AnnouncementRecipient r WITH (NOLOCK) ON r.AnnouncementId = a.Id
    WHERE a.Id = @AnnouncementId
    GROUP BY 
        a.Id, a.Title, a.Body, a.Type, a.TargetScope, a.LocationId, loc.Name,
        a.SpaceId, sp.Name, a.ScheduledAt, a.SentAt, a.Status, a.CreatedBy, u.Name, a.CreatedAt;

    -- Result set 2: Recipients breakdown
    SELECT 
        r.Id,
        r.AnnouncementId,
        r.UserId,
        usr.Name AS UserName,
        usr.Email AS UserEmail,
        r.Channel,
        r.Status,
        r.RetryCount,
        r.SentAt
    FROM dbo.WN_AnnouncementRecipient r WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users usr WITH (NOLOCK) ON usr.Id = r.UserId
    WHERE r.AnnouncementId = @AnnouncementId
    ORDER BY r.Id ASC;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_GetForUser]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 7. Stored Procedure: dbo.WN_Announcements_GetForUser (Mobile endpoint)
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_GetForUser]
    @UserId INT,
    @Page   INT = 1,
    @Limit  INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT 
        a.Id,
        a.Title,
        a.Body,
        a.Type,
        a.TargetScope,
        a.ScheduledAt,
        a.SentAt,
        a.CreatedAt,
        CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.WN_AnnouncementRecipient ar WITH (NOLOCK) 
            WHERE ar.AnnouncementId = a.Id AND ar.UserId = @UserId AND ar.Status = 'Read'
        ) THEN 1 ELSE 0 END AS BIT) AS IsRead,
        (
            SELECT MAX(ar.SentAt) 
            FROM dbo.WN_AnnouncementRecipient ar WITH (NOLOCK) 
            WHERE ar.AnnouncementId = a.Id AND ar.UserId = @UserId AND ar.Status = 'Read'
        ) AS ReadAt,
        COUNT(1) OVER() AS TotalRecords
    FROM dbo.WN_Announcement a WITH (NOLOCK)
    WHERE a.Status IN ('Sending', 'Sent')
      AND EXISTS (
          SELECT 1 FROM dbo.WN_AnnouncementRecipient ar WITH (NOLOCK)
          WHERE ar.AnnouncementId = a.Id AND ar.UserId = @UserId
      )
    ORDER BY a.CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 5. Stored Procedure: dbo.WN_Announcements_GetList
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_GetList]
    @Page   INT = 1,
    @Limit  INT = 20,
    @Search NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT 
        a.Id,
        a.Title,
        a.Body,
        a.Type,
        a.TargetScope,
        a.LocationId,
        loc.Name AS LocationName,
        a.SpaceId,
        sp.Name AS SpaceName,
        a.ScheduledAt,
        a.SentAt,
        a.Status,
        a.CreatedBy,
        u.Name AS CreatedByName,
        a.CreatedAt,
        COUNT(r.Id) AS TotalRecipients,
        COUNT(DISTINCT r.UserId) AS TotalUsers,
        SUM(CASE WHEN r.Status = 'Sent' THEN 1 ELSE 0 END) AS SentCount,
        SUM(CASE WHEN r.Status = 'Failed' THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN r.Status = 'Read' THEN 1 ELSE 0 END) AS ReadCount,
        SUM(CASE WHEN r.Status = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
        SUM(CASE WHEN r.Channel = 'Push' AND r.Status IN ('Sent','Read') THEN 1 ELSE 0 END) AS PushSentCount,
        SUM(CASE WHEN r.Channel = 'Email' AND r.Status IN ('Sent','Read') THEN 1 ELSE 0 END) AS EmailSentCount,
        COUNT(1) OVER() AS TotalRecords
    FROM dbo.WN_Announcement a WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = a.LocationId
    LEFT JOIN dbo.WN_Spaces sp WITH (NOLOCK) ON sp.Id = a.SpaceId
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = a.CreatedBy
    LEFT JOIN dbo.WN_AnnouncementRecipient r WITH (NOLOCK) ON r.AnnouncementId = a.Id
    WHERE (@Search IS NULL OR a.Title LIKE '%' + @Search + '%' OR a.Body LIKE '%' + @Search + '%')
    GROUP BY 
        a.Id, a.Title, a.Body, a.Type, a.TargetScope, a.LocationId, loc.Name,
        a.SpaceId, sp.Name, a.ScheduledAt, a.SentAt, a.Status, a.CreatedBy, u.Name, a.CreatedAt
    ORDER BY a.CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_GetPendingDeliveries]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 9. Stored Procedure: dbo.WN_Announcements_GetPendingDeliveries (Job worker batch pickup)
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_GetPendingDeliveries]
    @BatchSize INT = 200
AS
BEGIN
    SET NOCOUNT ON;

    -- Update parent announcements to 'Sending' if Queued and due
    UPDATE a
    SET a.Status = 'Sending'
    FROM dbo.WN_Announcement a
    WHERE a.Status = 'Queued'
      AND (a.ScheduledAt IS NULL OR a.ScheduledAt <= SYSUTCDATETIME());

    -- Select batch of pending recipients
    SELECT TOP (@BatchSize)
        r.Id AS RecipientId,
        r.AnnouncementId,
        r.UserId,
        r.Channel,
        r.RetryCount,
        a.Title,
        a.Body,
        a.Type,
        u.Email AS UserEmail,
        u.Name AS UserName
    FROM dbo.WN_AnnouncementRecipient r WITH (NOLOCK)
    JOIN dbo.WN_Announcement a WITH (NOLOCK) ON a.Id = r.AnnouncementId
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = r.UserId
    WHERE r.Status = 'Pending'
      AND a.Status IN ('Queued', 'Sending')
      AND (a.ScheduledAt IS NULL OR a.ScheduledAt <= SYSUTCDATETIME())
    ORDER BY r.Id ASC;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_MarkRead]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 8. Stored Procedure: dbo.WN_Announcements_MarkRead
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_MarkRead]
    @AnnouncementId UNIQUEIDENTIFIER,
    @UserId         INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WN_AnnouncementRecipient
    SET Status = 'Read',
        SentAt = ISNULL(SentAt, SYSUTCDATETIME())
    WHERE AnnouncementId = @AnnouncementId
      AND UserId = @UserId;

    SELECT @@ROWCOUNT AS AffectedRows;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Announcements_UpdateDeliveryStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 10. Stored Procedure: dbo.WN_Announcements_UpdateDeliveryStatus
CREATE OR ALTER PROCEDURE [dbo].[WN_Announcements_UpdateDeliveryStatus]
    @RecipientId BIGINT,
    @Status      NVARCHAR(20),
    @RetryCount  INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WN_AnnouncementRecipient
    SET Status = @Status,
        RetryCount = @RetryCount,
        SentAt = CASE WHEN @Status IN ('Sent', 'Read') THEN SYSUTCDATETIME() ELSE SentAt END
    WHERE Id = @RecipientId;

    SELECT @@ROWCOUNT AS AffectedRows;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_AssignAttendantToBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_AssignAttendantToBooking]
    @BookingDetailId INT,
    @PersonId INT,
    @CustomerId INT,
    @AssignedFrom DATE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RoomCapacity INT = 0;
    DECLARE @SpaceCategory NVARCHAR(100) = '';
    DECLARE @RentAmount DECIMAL(18,2) = 0;
    DECLARE @SpacePrice DECIMAL(18,2) = NULL;
    DECLARE @SeatPrice DECIMAL(10,2) = 0;
    DECLARE @CurrentAttendantCount INT = 0;
    DECLARE @IsOverCapacity BIT = 0;
    DECLARE @ExcessSeatCount INT = 0;
    DECLARE @SurchargeApplied DECIMAL(10,2) = 0;
    DECLARE @BookingEndTo DATE = NULL;

    -- Fetch Booking Detail & Space Capacity/Price/EndDateTime info
    SELECT TOP 1
        @SpaceCategory = ISNULL(bd.SpaceCategory, ''),
        @RentAmount = ISNULL(bd.RentAmount, ISNULL(bd.Amount, 0)),
        @RoomCapacity = ISNULL(s.Capacity, 1),
        @SpacePrice = s.Price,
        @BookingEndTo = CAST(bd.EndDateTime AS DATE)
    FROM [dbo].[WN_BookingDetails] bd WITH (NOLOCK)
    LEFT JOIN [dbo].[WN_Spaces] s WITH (NOLOCK) ON s.Code = bd.SpaceCode OR s.Name = bd.SpaceName
    WHERE bd.Id = @BookingDetailId;

    IF @RoomCapacity <= 0 SET @RoomCapacity = 1;

    -- Seat Price formula per rule: 0.5 * (column Price, table dbo.WN_Spaces)
    IF @SpacePrice IS NOT NULL AND @SpacePrice > 0
        SET @SeatPrice = @SpacePrice;
    ELSE
        SET @SeatPrice = @RentAmount / @RoomCapacity;

    -- Count active attendants currently assigned to this BookingDetailId
    SELECT @CurrentAttendantCount = COUNT(1)
    FROM [dbo].[WN_BookingAttendants]
    WHERE BookingDetailId = @BookingDetailId AND (AssignedTo IS NULL OR AssignedTo >= CAST(SYSUTCDATETIME() AS DATE));

    -- Capacity Check Strategy
    -- Meeting rooms & shared spaces hard block over-capacity
    IF (@SpaceCategory LIKE '%Meeting%' OR @SpaceCategory LIKE '%Shared%') AND (@CurrentAttendantCount >= @RoomCapacity)
    BEGIN
        RAISERROR('Over-capacity attendant assignment is not allowed for meeting rooms and shared spaces.', 16, 1);
        RETURN;
    END

    -- Check if this new addition exceeds capacity
    IF (@CurrentAttendantCount + 1) > @RoomCapacity
    BEGIN
        SET @IsOverCapacity = 1;
        SET @ExcessSeatCount = (@CurrentAttendantCount + 1) - @RoomCapacity;
        -- SurchargeApplied = ExcessSeatCount * 0.5 * SeatPrice
        SET @SurchargeApplied = CAST((@ExcessSeatCount * 0.5 * @SeatPrice) AS DECIMAL(10,2));
    END

    -- Check if person is already active on this booking detail
    IF EXISTS (SELECT 1 FROM [dbo].[WN_BookingAttendants] WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId AND (AssignedTo IS NULL OR AssignedTo >= CAST(SYSUTCDATETIME() AS DATE)))
    BEGIN
        RAISERROR('Attendant is already actively assigned to this booking.', 16, 1);
        RETURN;
    END

    -- Insert into WN_BookingAttendants with AssignedTo populated from Booking EndDate
    INSERT INTO [dbo].[WN_BookingAttendants] (
        BookingDetailId,
        PersonId,
        CustomerId,
        AssignedFrom,
        AssignedTo,
        IsOverCapacity,
        ExcessSeatCount,
        SurchargeApplied
    )
    VALUES (
        @BookingDetailId,
        @PersonId,
        @CustomerId,
        @AssignedFrom,
        @BookingEndTo,
        @IsOverCapacity,
        @ExcessSeatCount,
        @SurchargeApplied
    );

    DECLARE @NewAssignmentId INT = SCOPE_IDENTITY();

    -- Ensure WN_AccessStatus row exists and is enabled
    IF EXISTS (SELECT 1 FROM [dbo].[WN_AccessStatus] WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId)
    BEGIN
        UPDATE [dbo].[WN_AccessStatus]
        SET IsEnabled = 1, GrantedAt = SYSUTCDATETIME(), RevokedAt = NULL
        WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[WN_AccessStatus] (BookingDetailId, PersonId, CustomerId, IsEnabled, GrantedAt)
        VALUES (@BookingDetailId, @PersonId, @CustomerId, 1, SYSUTCDATETIME());
    END

    SELECT 
        @NewAssignmentId AS BookingAttendantId,
        @IsOverCapacity AS IsOverCapacity,
        @ExcessSeatCount AS ExcessSeatCount,
        @SurchargeApplied AS SurchargeApplied;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Billing_GetDueBookings]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Billing_GetDueBookings]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);
    DECLARE @EndOfCurrentMonth DATE = EOMONTH(@Today);
    DECLARE @DayOfMonth INT = DAY(@Today);

    -- Fetch active bookings due for next billing cycle (trigger on 18th of the going month)
    SELECT 
        b.Id AS BookingId,
        b.UserId,
        b.SpaceId,
        b.StartOn AS ContractStart,
        b.EndOn AS ContractEnd,
        ISNULL(b.MonthlyRent, CASE WHEN ISNULL(b.BillingPeriodMonths, 3) > 0 AND b.SubtotalAmount > 0 THEN b.SubtotalAmount / b.BillingPeriodMonths ELSE b.TotalAmount END) AS MonthlyRent,
        b.TotalAmount AS ContractTotalAmount,
        ISNULL(b.BillingPeriodMonths, 3) AS BillingPeriodMonths,
        ISNULL(b.SecurityDepositMonths, 2) AS SecurityDepositMonths,
        CAST(1 AS BIT) AS SecurityDepositCharged,
        latestInv.BillingPeriodStart AS CurrentPeriodStart,
        latestInv.BillingPeriodEnd AS CurrentPeriodEnd,
        DATEADD(day, 1, latestInv.BillingPeriodEnd) AS NextBillingDate
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    CROSS APPLY (
        SELECT TOP 1 
            i.BillingPeriodStart, 
            i.BillingPeriodEnd
        FROM dbo.WN_Invoices i WITH (NOLOCK)
        WHERE i.BookingId = b.Id
          AND i.StatusId NOT IN (5, ISNULL((SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Cancelled' ORDER BY Id), 5)) -- void: old 5 / OrderStatus 'Cancelled'
        ORDER BY i.BillingPeriodEnd DESC, i.Id DESC
    ) latestInv
    WHERE b.IsDeleted = 0
      AND b.BookingStatusId IN (1, 2)
      AND latestInv.BillingPeriodEnd IS NOT NULL
      AND latestInv.BillingPeriodEnd < b.EndOn
      AND (
          (@DayOfMonth >= 18 AND latestInv.BillingPeriodEnd <= @EndOfCurrentMonth)
          OR
          (latestInv.BillingPeriodEnd <= DATEADD(day, 14, @Today))
      )
      AND NOT EXISTS (
          SELECT 1 
          FROM dbo.WN_Invoices nextInv WITH (NOLOCK)
          WHERE nextInv.BookingId = b.Id
            AND nextInv.BillingPeriodStart > latestInv.BillingPeriodEnd
            AND nextInv.StatusId NOT IN (5, ISNULL((SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Cancelled' ORDER BY Id), 5))
      );
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_BillingPeriods_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_BillingPeriods_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Code, Label, DurationDays, SortOrder
    FROM dbo.WN_BillingPeriods WITH (NOLOCK)
    WHERE IsActive = 1
    ORDER BY SortOrder ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_AssignClosestSpace]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ------------------------------------------------------------
-- 4.2  WN_Booking_AssignClosestSpace
-- Filter via l.CompanyId / l.BranchId (Space Location)
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_AssignClosestSpace]
    @SpaceCategory NVARCHAR(20),
    @StartDT       DATETIME,
    @EndDT         DATETIME,
    @Capacity      INT = NULL,
    @CompanyId     INT = NULL,
    @BranchId      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SpaceTypeId INT;

    SELECT @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN RAISERROR('Unknown SpaceCategory',16,1); RETURN; END

    SELECT TOP 1
        s.Id, s.IdGUID, s.Name, s.Code, st.Capacity, l.Name AS LocationName
    FROM  dbo.WN_Spaces     s
    JOIN  dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
    JOIN  dbo.WN_Locations  l  ON l.Id  = s.LocationId
    WHERE s.Status = 1
      AND s.SpaceTypeId = @SpaceTypeId
      AND (@Capacity  IS NULL OR st.Capacity >= @Capacity)
      AND (@CompanyId IS NULL OR l.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk
            WHERE bk.SpaceId = s.Id
              AND bk.BookingStatusId IN (1,4)
              AND @StartDT < bk.EndOn
              AND @EndDT   > bk.StartOn
          )
    ORDER BY TRY_CAST(s.Code AS INT) ASC, s.Id ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_CheckOverlap]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- WN_Booking_CheckOverlap
-- Resolves SpaceGuid from WN_Spaces then checks WN_Bookings
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_CheckOverlap]
    @SpaceNumericId   INT,
    @StartDT          DATETIME,
    @EndDT            DATETIME,
    @ExcludeBookingId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.WN_Spaces WHERE Id = @SpaceNumericId) 
    BEGIN 
        SELECT 0 AS IsOverlapping; 
        RETURN; 
    END

    IF EXISTS (
        SELECT 1 FROM dbo.WN_Bookings bk
        WHERE bk.SpaceId = @SpaceNumericId
          AND bk.BookingStatusId IN (1,4)
          AND (@ExcludeBookingId IS NULL OR bk.Id != @ExcludeBookingId)
          AND @StartDT < bk.EndOn
          AND @EndDT   > bk.StartOn
    )
        SELECT 1 AS IsOverlapping;
    ELSE
        SELECT 0 AS IsOverlapping;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_Create]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_Create]
    @Email             NVARCHAR(255),
    @SpaceCategory     NVARCHAR(20),
    @StartDT           DATETIME,
    @EndDT             DATETIME,
    @Notes             NVARCHAR(MAX)    = '',
    @TotalAmount       DECIMAL(10,2)    = 0,
    @PaymentMethod     NVARCHAR(50)     = NULL,
    @PaymentRef        NVARCHAR(100)    = NULL,
    @Capacity          INT              = NULL,
    @AccountId         INT              = NULL,
    @ShiftType         NVARCHAR(20)     = '24_7'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserId            INT;
    DECLARE @UserName          NVARCHAR(255);
    DECLARE @SpaceId           INT;
    DECLARE @SpaceTypeId       INT;
    DECLARE @BookingId         INT;
    DECLARE @BookingGuid       UNIQUEIDENTIFIER;
    DECLARE @AssignedSpaceId   INT;
    DECLARE @AssignedSpaceName NVARCHAR(255);
    DECLARE @AssignedSpaceCode NVARCHAR(20);
    DECLARE @RentAccountId     INT;
    DECLARE @SecurityRecivedId INT;
    DECLARE @SecurityDeposit   DECIMAL(18,2) = 0;
    DECLARE @ChallanNumber     NVARCHAR(50);
    DECLARE @ValidityDate      DATETIME;
    DECLARE @Today             DATE;
    DECLARE @SeqNum            INT;

    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    SELECT @UserId = Id, @UserName = Name
    FROM dbo.WN_Users WHERE Email = @Email;

    IF @UserId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS challanNumber, NULL AS validity, NULL AS securityDeposit,
               'User not found' AS errorMessage;
        RETURN;
    END

    SELECT @SpaceTypeId = SpaceTypeId, @SecurityDeposit = ISNULL(SecurityDeposit, 0)
    FROM dbo.WN_SpaceConfig WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN
        SELECT TOP 1 @SpaceTypeId = Id FROM dbo.WN_SpaceTypes WHERE Name LIKE '%' + @SpaceCategory + '%';
    END

    IF @SpaceTypeId IS NULL
    BEGIN
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS challanNumber, NULL AS validity, NULL AS securityDeposit,
               'Unknown SpaceCategory' AS errorMessage;
        RETURN;
    END

    SET @Today = CAST(GETDATE() AS DATE);

    BEGIN TRANSACTION;

    UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
    SET    LastNumber = LastNumber + 1
    WHERE  CounterDate = @Today;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
        SELECT @Today, 1
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
            WHERE CounterDate = @Today
        );

    SELECT @SeqNum = LastNumber
    FROM   dbo.WN_ChallanCounter WITH (NOLOCK)
    WHERE  CounterDate = @Today;

    SET @ChallanNumber = 'WN-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                       + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);
    SET @ValidityDate  = DATEADD(DAY, 5, GETDATE());

    SELECT TOP 1
        @SpaceId           = s.Id,
        @AssignedSpaceName = s.Name,
        @AssignedSpaceCode = s.Code,
        @RentAccountId     = s.RentAccountId,
        @SecurityRecivedId = s.SecurityRecivedId
    FROM  dbo.WN_Spaces     s WITH (UPDLOCK)
    JOIN  dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
    WHERE s.Status = 1
      AND s.SpaceTypeId = @SpaceTypeId
      AND (@Capacity IS NULL OR st.Capacity >= @Capacity)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk
            WHERE bk.SpaceId = s.Id
              AND bk.BookingStatusId IN (5, 33)
              AND bk.IsDeleted = 0
              AND @StartDT < bk.EndOn
              AND @EndDT   > bk.StartOn
              AND (
                  @NormalizedShift = '24_7'
                  OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                  OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
              )
          )
    ORDER BY TRY_CAST(s.Code AS INT) ASC, s.Id ASC;

    IF @SpaceId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        SELECT NULL AS bookingId, NULL AS bookingGuid, NULL AS assignedSpaceId,
               NULL AS assignedSpaceName, NULL AS assignedSpaceCode,
               NULL AS challanNumber, NULL AS validity, NULL AS securityDeposit,
               'No available space for the requested period' AS errorMessage;
        RETURN;
    END

    SET @BookingGuid = NEWID();

    INSERT INTO dbo.WN_Bookings
        (IdGUID, UserId, SpaceId, StartOn, EndOn,
         Notes, TotalAmount, BookingStatusId,
         RentAccountId, SecurityRecivedId,
         ChallanNumber, ValidityDate,
         BookingDate, CreatedOn, CreatedById, ShiftType)
    VALUES
        (@BookingGuid, @UserId, @SpaceId, @StartDT, @EndDT,
         @Notes, @TotalAmount, 5,
         @RentAccountId, @SecurityRecivedId,
         @ChallanNumber, @ValidityDate,
         GETUTCDATE(), GETUTCDATE(), @UserId, @NormalizedShift);

    SET @BookingId       = SCOPE_IDENTITY();
    SET @AssignedSpaceId = @SpaceId;

    IF @PaymentMethod IS NOT NULL AND @TotalAmount > 0
    BEGIN
        INSERT INTO dbo.WN_Payments
            (IdGUID, UserIdInt, BookingIdInt, Amount, CurrencyCode,
             PaymentMethodId, StatusId, TransactionRef, CreatedOn)
        VALUES
            (NEWID(), @UserId, @BookingId, @TotalAmount, N'PKR',
             1, 1, @PaymentRef, GETUTCDATE());
    END

    EXEC dbo.WN_BookingDetails_InsertLine
        @BookingGuid = @BookingGuid,
        @FeeType     = 'RoomRent',
        @Amount      = @TotalAmount,
        @AccountId   = @RentAccountId,
        @CreatedBy   = @Email;

    IF @SecurityDeposit > 0 AND @SecurityRecivedId IS NOT NULL
        EXEC dbo.WN_BookingDetails_InsertLine
            @BookingGuid = @BookingGuid,
            @FeeType     = 'SecurityDeposit',
            @Amount      = @SecurityDeposit,
            @AccountId   = @SecurityRecivedId,
            @CreatedBy   = @Email;

    COMMIT TRANSACTION;

    SELECT
        @BookingId                         AS bookingId,
        CAST(@BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        @AssignedSpaceId                   AS assignedSpaceId,
        @AssignedSpaceName                 AS assignedSpaceName,
        @AssignedSpaceCode                 AS assignedSpaceCode,
        @RentAccountId                     AS rentAccountId,
        @SecurityRecivedId                 AS securityRecivedId,
        @SecurityDeposit                   AS securityDeposit,
        @ChallanNumber                     AS challanNumber,
        @ValidityDate                      AS validity,
        @NormalizedShift                   AS shiftType,
        NULL                               AS errorMessage;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_GetAvailableSpaces]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 8. PROCEDURE: dbo.WN_Booking_GetAvailableSpaces
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_GetAvailableSpaces]
    @SpaceCategory NVARCHAR(20),
    @StartDT       DATETIME,
    @EndDT         DATETIME,
    @Capacity      INT = NULL,
    @CompanyId     INT = NULL,
    @BranchId      INT = NULL,
    @ShiftType     NVARCHAR(20) = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    DECLARE @SpaceTypeId INT;

    SELECT @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE SpaceCategory = @SpaceCategory;

    IF @SpaceTypeId IS NULL
    BEGIN
        SELECT TOP 1 @SpaceTypeId = Id FROM dbo.WN_SpaceTypes WHERE Name LIKE '%' + @SpaceCategory + '%';
    END

    SELECT
        s.Id,
        s.IdGUID,
        s.Name,
        s.Code,
        s.Price,
        s.BillingPeriodId,
        bp.Code                 AS BillingPeriodCode,
        bp.Label                AS BillingPeriodLabel,
        s.Price                 AS PricePerDay,
        s.Price                 AS PricePerHour,
        st.Name                 AS SpaceType,
        st.Capacity             AS Capacity,
        l.Name                  AS LocationName,
        TRY_CAST(s.Code AS INT) AS CodeNumber
    FROM  dbo.WN_Spaces         s
    JOIN  dbo.WN_SpaceTypes     st ON st.Id = s.SpaceTypeId
    JOIN  dbo.WN_Locations      l  ON l.Id  = s.LocationId
    LEFT JOIN dbo.WN_BillingPeriods bp ON bp.Id = s.BillingPeriodId
    WHERE s.IsActive = 1
      AND (@SpaceTypeId IS NULL OR s.SpaceTypeId = @SpaceTypeId)
      AND (@Capacity  IS NULL OR st.Capacity >= @Capacity)
      AND (@CompanyId IS NULL OR l.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
      AND NOT EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk
            WHERE bk.SpaceId = s.Id
              AND bk.BookingStatusId IN (5, 33)
              AND bk.IsDeleted = 0
              AND @StartDT < bk.EndOn
              AND @EndDT   > bk.StartOn
              AND (
                  @NormalizedShift = '24_7'
                  OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                  OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
              )
          )
    ORDER BY TRY_CAST(s.Code AS INT) ASC, s.Id ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Booking_GetBillingSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Procedure 4: WN_Booking_GetBillingSummary
CREATE OR ALTER PROCEDURE [dbo].[WN_Booking_GetBillingSummary]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalContractRent DECIMAL(18,2),
            @RentInvoiced DECIMAL(18,2),
            @RentPaid DECIMAL(18,2),
            @RemainingRent DECIMAL(18,2),
            @SecRequired DECIMAL(18,2),
            @SecInvoiced DECIMAL(18,2),
            @SecPaid DECIMAL(18,2),
            @SecOutstanding DECIMAL(18,2),
            @SecCharged BIT,
            @MonthlyRent DECIMAL(18,2),
            @NextBillingDate DATETIME,
            @PeriodStart DATETIME,
            @PeriodEnd DATETIME,
            @ITable NVARCHAR(128),
            @StatusCol NVARCHAR(128);

    SELECT @ITable = CASE 
        WHEN OBJECT_ID('dbo.WN_Invoice') IS NOT NULL THEN 'dbo.WN_Invoice'
        WHEN OBJECT_ID('dbo.Invoice') IS NOT NULL THEN 'dbo.Invoice'
        WHEN OBJECT_ID('dbo.Invoices') IS NOT NULL THEN 'dbo.Invoices'
        ELSE 'dbo.WN_Invoice'
    END;

    SELECT @StatusCol = CASE 
        WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@ITable) AND name = 'Status') THEN 'Status'
        WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@ITable) AND name = 'InvoiceStatus') THEN 'InvoiceStatus'
        WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@ITable) AND name = 'PaymentStatus') THEN 'PaymentStatus'
        ELSE 'Status'
    END;

    SELECT 
        @TotalContractRent = ISNULL(TotalAmount, 0),
        @MonthlyRent = ISNULL(MonthlyRent, TotalAmount / 12.0),
        @SecRequired = ISNULL(MonthlyRent * SecurityDepositMonths, 0),
        @SecCharged = ISNULL(SecurityDepositCharged, 0),
        @NextBillingDate = NextBillingDate,
        @PeriodStart = BillingPeriodStart,
        @PeriodEnd = BillingPeriodEnd
    FROM dbo.WN_Booking
    WHERE Id = @BookingId;

    DECLARE @SumSql NVARCHAR(MAX) = '
    SELECT 
        @RentInvoiced = ISNULL(SUM(Subtotal - SecurityDepositAmount), 0),
        @RentPaid = ISNULL(SUM(CASE WHEN ' + @StatusCol + ' = ''Paid'' THEN (Subtotal - SecurityDepositAmount) ELSE 0 END), 0),
        @SecInvoiced = ISNULL(SUM(SecurityDepositAmount), 0),
        @SecPaid = ISNULL(SUM(CASE WHEN ' + @StatusCol + ' = ''Paid'' THEN SecurityDepositAmount ELSE 0 END), 0)
    FROM ' + @ITable + '
    WHERE BookingId = @BookingId AND ' + @StatusCol + ' != ''Cancelled'';';

    EXEC sp_executesql @SumSql,
        N'@BookingId INT, @RentInvoiced DECIMAL(18,2) OUTPUT, @RentPaid DECIMAL(18,2) OUTPUT, @SecInvoiced DECIMAL(18,2) OUTPUT, @SecPaid DECIMAL(18,2) OUTPUT',
        @BookingId, @RentInvoiced OUTPUT, @RentPaid OUTPUT, @SecInvoiced OUTPUT, @SecPaid OUTPUT;

    SET @RemainingRent = ISNULL(@TotalContractRent - @RentInvoiced, 0);
    IF @RemainingRent < 0 SET @RemainingRent = 0;

    SET @SecOutstanding = ISNULL(@SecRequired - @SecPaid, 0);
    IF @SecOutstanding < 0 SET @SecOutstanding = 0;

    SELECT 
        @BookingId AS BookingId,
        @TotalContractRent AS TotalContractRent,
        @RentInvoiced AS RentInvoiced,
        @RentPaid AS RentPaid,
        @RemainingRent AS RemainingRent,
        @SecRequired AS SecurityDepositRequired,
        @SecInvoiced AS SecurityDepositInvoiced,
        @SecPaid AS SecurityDepositPaid,
        @SecOutstanding AS SecurityDepositOutstanding,
        @SecCharged AS SecurityDepositCharged,
        @NextBillingDate AS NextBillingDate,
        @PeriodStart AS CurrentBillingPeriodStart,
        @PeriodEnd AS CurrentBillingPeriodEnd;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_GetByBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_GetByBooking]
    @BookingIdentifier NVARCHAR(50),
    @UserEmail         NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ResolvedBookingId INT;
    SELECT TOP 1 @ResolvedBookingId = b.Id
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
    WHERE (
        TRY_CAST(@BookingIdentifier AS UNIQUEIDENTIFIER) IS NOT NULL AND b.IdGUID = TRY_CAST(@BookingIdentifier AS UNIQUEIDENTIFIER)
        OR TRY_CAST(@BookingIdentifier AS INT) IS NOT NULL AND b.Id = TRY_CAST(@BookingIdentifier AS INT)
    )
    AND (@UserEmail IS NULL OR u.Email = @UserEmail);

    -- Result Set 1: Summary Header
    SELECT TOP 1
        v.BookingId,
        v.BookingPublicId,
        v.StartOn,
        v.EndOn,
        v.ContractStartDate,
        v.ContractEndDate,
        v.BookingStatusCode,
        v.BookingStatusLabel,
        v.UserName        AS CustomerName,
        v.UserEmail       AS CustomerEmail,
        v.SpaceCode,
        v.SpaceNumber,
        v.SpaceName,
        v.SpaceCapacity,
        v.SpaceTypeName,
        v.LocationName,
        v.BranchName,
        v.CompanyName,
        v.BillingPeriodCode,
        v.BillingPeriodLabel,
        v.BillingPeriod,
        v.BillingPeriodMonths,
        v.NumberOfMonths,
        v.ContractDuration,
        v.MonthlyRent,
        v.CurrentCycleAmount,
        v.TotalContractAmount,
        v.TotalPaidAmount,
        v.BalanceLeft,
        v.NextBillDueDate,
        v.NextBillingDate,
        v.SeatPrice,
        v.RoomPrice,
        v.SecurityDeposit,
        v.BookedOn
    FROM dbo.WN_vw_BookingSummary v WITH (NOLOCK)
    WHERE v.BookingId = @ResolvedBookingId;

    -- Result Set 2: Line Breakdown
    IF EXISTS (
        SELECT 1 
        FROM dbo.WN_BookingLines bl WITH (NOLOCK)
        JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = bl.BookingId
        WHERE b.Id = @ResolvedBookingId AND bl.IsDeleted = 0
    )
    BEGIN
        SELECT
            bl.Id                                             AS lineId,
            bl.Id                                             AS Id,
            CAST(b.IdGUID AS NVARCHAR(36))                     AS idGuid,
            CAST(b.IdGUID AS NVARCHAR(36))                     AS bookingGuid,
            ISNULL(ct.Label, bl.Description)                  AS FeeType,
            ISNULL(ct.Label, bl.Description)                  AS chargeTypeLabel,
            bl.Description                                    AS description,
            ISNULL(bl.LineTotal, (bl.UnitPrice * bl.Quantity) - bl.DiscountAmount) AS Amount,
            ISNULL(bl.LineTotal, (bl.UnitPrice * bl.Quantity) - bl.DiscountAmount) AS lineTotal,
            bl.AccountId                                      AS AccountId,
            bl.AccountId                                      AS accountId,
            a.Description                                     AS accountName,
            bl.CreatedOn                                      AS CreatedOn,
            l.CompanyId                                       AS CompanyId,
            l.BranchId                                        AS BranchId
        FROM dbo.WN_BookingLines bl WITH (NOLOCK)
        JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = bl.BookingId
        LEFT JOIN dbo.WN_ChargeTypes ct WITH (NOLOCK) ON ct.Id = bl.ChargeTypeId
        LEFT JOIN dbo.AccountsCOA a WITH (NOLOCK) ON a.Id = bl.AccountId
        LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
        LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
        WHERE b.Id = @ResolvedBookingId
          AND (@UserEmail IS NULL OR u.Email = @UserEmail)
          AND bl.IsDeleted = 0
        ORDER BY bl.Id;
    END
    ELSE
    BEGIN
        SELECT
            bd.Id                                             AS lineId,
            bd.Id                                             AS Id,
            CAST(bd.IdGUID      AS NVARCHAR(36))              AS idGuid,
            CAST(bd.BookingGuid AS NVARCHAR(36))              AS bookingGuid,
            bd.FeeType                                        AS FeeType,
            bd.FeeType                                        AS chargeTypeLabel,
            bd.FeeType                                        AS description,
            bd.Amount                                         AS Amount,
            bd.Amount                                         AS lineTotal,
            bd.AccountId                                      AS AccountId,
            bd.AccountId                                      AS accountId,
            a.Description                                     AS accountName,
            bd.CreatedOn                                      AS CreatedOn,
            l.CompanyId                                       AS CompanyId,
            l.BranchId                                        AS BranchId
        FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
        LEFT JOIN dbo.AccountsCOA   a  WITH (NOLOCK) ON a.Id        = bd.AccountId
        LEFT JOIN dbo.WN_Bookings   b  WITH (NOLOCK) ON b.IdGUID    = bd.BookingGuid
        LEFT JOIN dbo.WN_Spaces     s  WITH (NOLOCK) ON s.Id        = b.SpaceId
        LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.Id        = s.LocationId
        LEFT JOIN dbo.WN_Users      u  WITH (NOLOCK) ON u.Id        = b.UserId
        WHERE b.Id = @ResolvedBookingId
          AND (@UserEmail IS NULL OR u.Email = @UserEmail)
          AND bd.IsDeleted = 0
        ORDER BY bd.Id;
    END
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ------------------------------------------------------------
-- 4.5  WN_BookingDetails_GetList
-- SELECT + WHERE derive CompanyId/BranchId via
-- BookingDetail  Booking Space  Location
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_GetList]
    @Page      INT           = 1,
    @Limit     INT           = 100,
    @FeeType   NVARCHAR(50)  = NULL,
    @AccountId INT           = NULL,
    @Search    NVARCHAR(255) = NULL,
    @CompanyId INT           = NULL,
    @BranchId  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        bd.Id,
        CAST(bd.IdGUID      AS NVARCHAR(36)) AS idGuid,
        CAST(bd.BookingGuid AS NVARCHAR(36)) AS bookingGuid,
        bd.FeeType,
        bd.Amount,
        bd.AccountId,
        a.Description  AS accountName,
        b.ChallanNumber,
        bd.CreatedOn,
        l.CompanyId    AS CompanyId,
        l.BranchId     AS BranchId
    FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
    LEFT JOIN dbo.AccountsCOA  a  WITH (NOLOCK) ON a.Id        = bd.AccountId
    LEFT JOIN dbo.WN_Bookings  b  WITH (NOLOCK) ON b.IdGUID    = bd.BookingGuid
    LEFT JOIN dbo.WN_Spaces    s  WITH (NOLOCK) ON s.Id        = b.SpaceId
    LEFT JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id        = s.LocationId
    WHERE bd.IsDeleted = 0
      AND (@FeeType   IS NULL OR bd.FeeType   = @FeeType)
      AND (@AccountId IS NULL OR bd.AccountId = @AccountId)
      AND (@CompanyId IS NULL OR l.CompanyId  = @CompanyId)
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
      AND (@Search    IS NULL OR @Search = ''
           OR CAST(bd.BookingGuid AS NVARCHAR(36)) LIKE '%' + @Search + '%'
           OR b.ChallanNumber LIKE '%' + @Search + '%')
    ORDER BY bd.CreatedOn DESC
    OFFSET (@Page - 1) * @Limit ROWS
    FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_GetTotalByBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Returns the sum of all fee lines for a booking (used for payment total).
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_GetTotalByBooking]
    @BookingGuid NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ISNULL(SUM(Amount), 0) AS TotalAmount
    FROM dbo.WN_BookingDetails WITH (NOLOCK)
    WHERE CAST(BookingGuid AS NVARCHAR(36)) = @BookingGuid
      AND IsDeleted = 0;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 2. Update dbo.WN_BookingDetails_Insert
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_Insert]
    @BookingGuid             UNIQUEIDENTIFIER,
    @CustomerCode            NVARCHAR(50)   = NULL,
    @CustomerName            NVARCHAR(255)  = NULL,
    @CustomerEmail           NVARCHAR(255)  = NULL,
    @SpaceName               NVARCHAR(255)  = NULL,
    @SpaceCode               NVARCHAR(50)   = NULL,
    @SpaceCategory           NVARCHAR(50)   = NULL,
    @StartDateTime           DATETIME       = NULL,
    @EndDateTime             DATETIME       = NULL,
    @RentAmount              DECIMAL(18,2)  = 0,
    @SecurityDeposit         DECIMAL(18,2)  = 0,
    @RentAccountId           INT            = NULL,
    @DepositAccountId        INT            = NULL,
    @PaymentMethod           NVARCHAR(50)   = NULL,
    @Notes                   NVARCHAR(MAX)  = NULL,
    @SupportChargesId        TINYINT        = NULL,
    @AppliedChargePercentage DECIMAL(5,2)   = 0,
    @AppliedTaxPercentage    DECIMAL(5,2)   = 0,
    @SupportChargeAmount     DECIMAL(18,2)  = NULL,
    @TaxAmount               DECIMAL(18,2)  = NULL,
    @AccountReceivableId     INT            = NULL,
    @ServicesIncomeId        INT            = NULL,
    @SalesTaxId              INT            = NULL,
    @SecurityReceivedId      INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @EffectiveSecRecId INT = COALESCE(@SecurityReceivedId, @DepositAccountId);

    DECLARE @SupportChargeAmt DECIMAL(18,2) = @SupportChargeAmount;
    IF @SupportChargeAmt IS NULL OR @SupportChargeAmt <= 0
    BEGIN
        SET @SupportChargeAmt = ROUND(@RentAmount * (@AppliedChargePercentage / 100.0), 2);
    END

    DECLARE @TaxAmt DECIMAL(18,2) = @TaxAmount;
    IF @TaxAmt IS NULL OR @TaxAmt < 0
    BEGIN
        SET @TaxAmt = ROUND(@SupportChargeAmt * (@AppliedTaxPercentage / 100.0), 2);
    END

    DECLARE @CalculatedTotal DECIMAL(18,2) = @RentAmount + @TaxAmt + ISNULL(@SecurityDeposit, 0);

    IF EXISTS (SELECT 1 FROM dbo.WN_BookingDetails WHERE BookingGuid = @BookingGuid)
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            CustomerCode            = ISNULL(@CustomerCode,    CustomerCode),
            CustomerName            = ISNULL(@CustomerName,    CustomerName),
            CustomerEmail           = ISNULL(@CustomerEmail,   CustomerEmail),
            SpaceName               = ISNULL(@SpaceName,       SpaceName),
            SpaceCode               = ISNULL(@SpaceCode,       SpaceCode),
            SpaceCategory           = ISNULL(@SpaceCategory,   SpaceCategory),
            StartDateTime           = ISNULL(@StartDateTime,   StartDateTime),
            EndDateTime             = ISNULL(@EndDateTime,     EndDateTime),
            Amount                  = @RentAmount,
            RentAmount              = @RentAmount,
            SecurityDeposit         = @SecurityDeposit,
            SupportChargesId        = ISNULL(@SupportChargesId, SupportChargesId),
            AppliedChargePercentage = @AppliedChargePercentage,
            AppliedTaxPercentage    = @AppliedTaxPercentage,
            SupportChargeAmount     = @SupportChargeAmt,
            TaxAmount               = @TaxAmt,
            TotalAmount             = @CalculatedTotal,
            RentAccountId           = ISNULL(@RentAccountId,   RentAccountId),
            DepositAccountId        = @EffectiveSecRecId,
            PaymentMethod           = ISNULL(@PaymentMethod,   PaymentMethod),
            Notes                   = ISNULL(@Notes,           Notes),
            AccountReceivableId     = ISNULL(@AccountReceivableId, AccountReceivableId),
            ServicesIncomeId        = ISNULL(@ServicesIncomeId, ServicesIncomeId),
            SalesTaxId              = ISNULL(@SalesTaxId, SalesTaxId),
            SecurityReceivedId      = ISNULL(@EffectiveSecRecId, SecurityReceivedId)
        WHERE BookingGuid = @BookingGuid;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, FeeType, Amount, CustomerCode, CustomerName, CustomerEmail,
             SpaceName, SpaceCode, SpaceCategory, StartDateTime, EndDateTime,
             RentAmount, SecurityDeposit, SupportChargesId, AppliedChargePercentage,
             AppliedTaxPercentage, SupportChargeAmount, TaxAmount, TotalAmount,
             RentAccountId, DepositAccountId, PaymentMethod, Notes, CreatedOn, IsDeleted,
             AccountReceivableId, ServicesIncomeId, SalesTaxId, SecurityReceivedId)
        VALUES
            (NEWID(), @BookingGuid, 'Room Rent', @RentAmount, @CustomerCode, @CustomerName, @CustomerEmail,
             @SpaceName, @SpaceCode, @SpaceCategory, @StartDateTime, @EndDateTime,
             @RentAmount, @SecurityDeposit, @SupportChargesId, @AppliedChargePercentage,
             @AppliedTaxPercentage, @SupportChargeAmt, @TaxAmt, @CalculatedTotal,
             @RentAccountId, @EffectiveSecRecId, @PaymentMethod, @Notes, SYSUTCDATETIME(), 0,
             @AccountReceivableId, @ServicesIncomeId, @SalesTaxId, @EffectiveSecRecId);
    END

    -- Synchronize TotalAmount and Accounts on WN_Bookings
    UPDATE dbo.WN_Bookings
    SET TotalAmount         = @CalculatedTotal,
        AccountReceivableId = COALESCE(AccountReceivableId, @AccountReceivableId),
        RentAccountId       = COALESCE(RentAccountId, @RentAccountId),
        ServicesIncomeId    = COALESCE(ServicesIncomeId, @ServicesIncomeId),
        SalesTaxId          = COALESCE(SalesTaxId, @SalesTaxId),
        SecurityReceivedId  = COALESCE(SecurityReceivedId, @EffectiveSecRecId),
        SecurityRecivedId   = COALESCE(SecurityRecivedId, @EffectiveSecRecId)
    WHERE IdGUID = @BookingGuid;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingDetails_InsertLine]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ------------------------------------------------------------
-- 4.6  WN_BookingDetails_InsertLine
-- CompanyId/BranchId columns removed from table.
-- No longer stored derived at read time via join chain.
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingDetails_InsertLine]
    @BookingGuid UNIQUEIDENTIFIER,
    @FeeType     NVARCHAR(50),
    @Amount      DECIMAL(18,2),
    @AccountId   INT           = NULL,
    @CreatedBy   NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_BookingDetails
        WHERE BookingGuid = @BookingGuid
          AND FeeType     = @FeeType
          AND IsDeleted   = 0
    )
    BEGIN
        UPDATE dbo.WN_BookingDetails SET
            Amount    = @Amount,
            AccountId = ISNULL(@AccountId, AccountId)
        WHERE BookingGuid = @BookingGuid
          AND FeeType     = @FeeType
          AND IsDeleted   = 0;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_BookingDetails
            (IdGUID, BookingGuid, FeeType, Amount, AccountId, CreatedOn, CreatedBy)
        VALUES
            (NEWID(), @BookingGuid, @FeeType, @Amount, @AccountId, GETDATE(), @CreatedBy);
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingLines_GetByBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 8: BOOKING LINES
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_BookingLines_GetByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        bl.Id, bl.BookingId, bl.ChargeTypeId,
        ct.Code AS ChargeTypeCode, ct.Label AS ChargeTypeLabel,
        bl.Description, bl.Quantity, bl.UnitPrice,
        bl.DiscountAmount, bl.TaxRate, bl.TaxAmount, bl.LineTotal,
        bl.AccountId, bl.CreatedOn
    FROM dbo.WN_BookingLines bl WITH (NOLOCK)
    JOIN dbo.WN_ChargeTypes  ct WITH (NOLOCK) ON ct.Id = bl.ChargeTypeId
    WHERE bl.BookingId = @BookingId AND bl.IsDeleted = 0
    ORDER BY bl.Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingLines_GetTotal]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingLines_GetTotal]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ISNULL(SUM(LineTotal), 0)      AS GrandTotal,
        ISNULL(SUM(TaxAmount), 0)      AS TaxTotal,
        ISNULL(SUM(DiscountAmount), 0) AS DiscountTotal
    FROM dbo.WN_BookingLines WITH (NOLOCK)
    WHERE BookingId = @BookingId AND IsDeleted = 0;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingLines_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingLines_Insert]
    @BookingId      INT,
    @ChargeTypeId   TINYINT,
    @Description    NVARCHAR(200) = NULL,
    @Quantity       DECIMAL(10,4) = 1,
    @UnitPrice      DECIMAL(18,4),
    @DiscountAmount DECIMAL(18,4) = 0,
    @TaxRate        DECIMAL(6,4)  = 0,
    @AccountId      INT           = NULL,
    @CreatedById    INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_BookingLines
        (BookingId, ChargeTypeId, Description, Quantity, UnitPrice,
         DiscountAmount, TaxRate, AccountId, CreatedById)
    VALUES
        (@BookingId, @ChargeTypeId, @Description, @Quantity, @UnitPrice,
         @DiscountAmount, @TaxRate, @AccountId, @CreatedById);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingMeetingRoomEntitlements_Generate]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- =========================================================================
-- 2. Stored Procedure: WN_BookingMeetingRoomEntitlements_Generate
-- =========================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingMeetingRoomEntitlements_Generate]
    @BookingId INT,
    @UserId INT,
    @StartOn DATETIME2,
    @EndOn DATETIME2,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Verify User exists in WN_Users to satisfy FK_Entitlement_User
    IF NOT EXISTS (SELECT 1 FROM dbo.WN_Users WHERE Id = @UserId)
    BEGIN
        RETURN;
    END

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @CurrDate DATE = CAST(DATEFROMPARTS(YEAR(@StartOn), MONTH(@StartOn), 1) AS DATE);
        DECLARE @EndDate DATE = CAST(@EndOn AS DATE);

        WHILE @CurrDate <= @EndDate
        BEGIN
            DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@CurrDate), MONTH(@CurrDate), 1);
            DECLARE @MonthEnd DATE = EOMONTH(@CurrDate);

            -- Only insert if it doesn't already exist to prevent duplicates
            IF NOT EXISTS (
                SELECT 1 FROM dbo.WN_BookingMeetingRoomEntitlements
                WHERE BookingId = @BookingId AND MonthStartDate = @MonthStart
            )
            BEGIN
                INSERT INTO dbo.WN_BookingMeetingRoomEntitlements (
                    BookingId, UserId, MonthStartDate, MonthEndDate, AllocatedMinutes, Status, CreatedDate, CreatedById
                )
                VALUES (
                    @BookingId, @UserId, @MonthStart, @MonthEnd, 360, 'Active', SYSUTCDATETIME(), @CreatedById
                );
            END

            SET @CurrDate = DATEADD(MONTH, 1, @CurrDate);
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_BookingMeetingRoomEntitlements_GetActive]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- =========================================================================
-- 3. Stored Procedure: WN_BookingMeetingRoomEntitlements_GetActive
-- =========================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_BookingMeetingRoomEntitlements_GetActive]
    @UserId INT,
    @Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PrivateBookingId INT = NULL;
    DECLARE @StartOn DATETIME2 = NULL;
    DECLARE @EndOn DATETIME2 = NULL;

    SELECT TOP 1 
        @PrivateBookingId = b.Id,
        @StartOn = b.StartOn,
        @EndOn = b.EndOn
    FROM dbo.WN_Bookings b
    JOIN dbo.WN_Spaces s ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
    WHERE b.UserId = @UserId
      AND (b.IsDeleted = 0 OR b.IsDeleted IS NULL)
      AND (
          st.CategoryId = 1 
          OR LOWER(ISNULL(st.Name, '')) LIKE '%private%' 
          OR LOWER(ISNULL(s.Name, '')) LIKE '%private%'
      )
      AND @Date >= CAST(b.StartOn AS DATE)
      AND @Date <= CAST(b.EndOn AS DATE)
    ORDER BY b.StartOn ASC;

    IF @PrivateBookingId IS NULL
    BEGIN
        SELECT TOP 0 
            CAST(0 AS INT) AS Id,
            CAST(NEWID() AS UNIQUEIDENTIFIER) AS Guid,
            CAST(0 AS INT) AS BookingId,
            CAST(0 AS INT) AS UserId,
            CAST(GETDATE() AS DATE) AS MonthStartDate,
            CAST(GETDATE() AS DATE) AS MonthEndDate,
            CAST(0 AS INT) AS AllocatedMinutes,
            CAST(0 AS INT) AS UsedMinutes,
            CAST(0 AS INT) AS RemainingMinutes,
            CAST('' AS NVARCHAR(50)) AS Status;
        RETURN;
    END

    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Date), MONTH(@Date), 1);
    DECLARE @MonthEnd DATE = EOMONTH(@Date);

    IF NOT EXISTS (
        SELECT 1 FROM dbo.WN_BookingMeetingRoomEntitlements
        WHERE BookingId = @PrivateBookingId AND MonthStartDate = @MonthStart
    )
    BEGIN
        INSERT INTO dbo.WN_BookingMeetingRoomEntitlements (
            BookingId, UserId, MonthStartDate, MonthEndDate, AllocatedMinutes, Status, CreatedDate
        )
        VALUES (
            @PrivateBookingId, @UserId, @MonthStart, @MonthEnd, 360, 'Active', SYSUTCDATETIME()
        );
    END

    SELECT 
        e.Id,
        e.Guid,
        e.BookingId,
        e.UserId,
        e.MonthStartDate,
        e.MonthEndDate,
        e.AllocatedMinutes,
        ISNULL((
            SELECT SUM(u.UsedMinutes)
            FROM dbo.WN_MeetingRoomUsageLogs u
            WHERE u.EntitlementId = e.Id AND u.Status <> 'Cancelled'
        ), 0) AS UsedMinutes,
        e.AllocatedMinutes - ISNULL((
            SELECT SUM(u.UsedMinutes)
            FROM dbo.WN_MeetingRoomUsageLogs u
            WHERE u.EntitlementId = e.Id AND u.Status <> 'Cancelled'
        ), 0) AS RemainingMinutes,
        e.Status
    FROM dbo.WN_BookingMeetingRoomEntitlements e
    WHERE e.BookingId = @PrivateBookingId 
      AND e.MonthStartDate = @MonthStart
      AND e.Status = 'Active';
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Cancel]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_Cancel]
    @Id           INT,
    @UserEmail    NVARCHAR(256) = NULL,
    @CancelReason NVARCHAR(500) = NULL,
    @UpdatedById  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @UserEmail IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM dbo.WN_Bookings b
        JOIN dbo.WN_Users u ON u.Id = b.UserId
        WHERE b.Id = @Id AND u.Email = @UserEmail
    )
    BEGIN
        RAISERROR('Booking not found or access denied.', 16, 1); RETURN;
    END

    UPDATE dbo.WN_Bookings SET
        BookingStatusId = 3,
        CancelReason    = @CancelReason,
        UpdatedOn       = SYSUTCDATETIME(),
        UpdatedById     = @UpdatedById
    WHERE Id = @Id AND IsDeleted = 0;

    UPDATE dbo.WN_Challans SET StatusId = 4
    WHERE BookingId = @Id AND StatusId = 1;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_CancelFreeMeetingRoom]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- WN_Bookings_CancelFreeMeetingRoom
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_CancelFreeMeetingRoom]
    @BookingId      INT,
    @UserEmail      NVARCHAR(256) = NULL,
    @CancelReason   NVARCHAR(500) = NULL,
    @UpdatedById    INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        -- Update Booking Status to Cancelled (3)
        UPDATE dbo.WN_Bookings
        SET BookingStatusId = 3, -- Cancelled
            CancelReason = @CancelReason,
            UpdatedOn = SYSUTCDATETIME(),
            UpdatedById = @UpdatedById
        WHERE Id = @BookingId AND IsFree = 1;

        IF @@ROWCOUNT = 0
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50000, 'Free meeting room booking not found or already cancelled.', 1;
        END

        -- Update Usage Log Status to Cancelled
        UPDATE dbo.WN_MeetingRoomUsageLogs
        SET Status = 'Cancelled',
            CancelledDate = SYSUTCDATETIME(),
            CancelledById = @UpdatedById,
            Remarks = ISNULL(Remarks, '') + ' ' + ISNULL(@CancelReason, '')
        WHERE MeetingRoomBookingId = @BookingId;

        -- Update Challan Status to Cancelled (3)
        UPDATE dbo.WN_Challans
        SET StatusId = 3
        WHERE BookingId = @BookingId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_CheckOverlap]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 3. PROCEDURE: dbo.WN_Bookings_CheckOverlap
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_CheckOverlap]
    @SpaceId          INT,
    @StartOn          DATETIME2,
    @EndOn            DATETIME2,
    @ExcludeBookingId INT = NULL,
    @ShiftType        NVARCHAR(20) = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    IF EXISTS (
        SELECT 1 FROM dbo.WN_Bookings
        WHERE SpaceId         = @SpaceId
          AND IsDeleted       = 0
          AND BookingStatusId IN (5, 33)
          AND (@ExcludeBookingId IS NULL OR Id <> @ExcludeBookingId)
          AND @StartOn        < EndOn
          AND @EndOn          > StartOn
          AND (
              @NormalizedShift = '24_7'
              OR ISNULL(ShiftType, '24_7') = '24_7'
              OR @NormalizedShift = ISNULL(ShiftType, '24_7')
          )
    )
        SELECT 1 AS IsOverlapping;
    ELSE
        SELECT 0 AS IsOverlapping;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetAvailableSpaces]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 4. PROCEDURE: dbo.WN_Bookings_GetAvailableSpaces
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetAvailableSpaces]
    @SpaceTypeId INT,
    @StartOn     DATETIME2,
    @EndOn       DATETIME2,
    @Capacity    INT = NULL,
    @ShiftType   NVARCHAR(20) = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name,
        s.LocationId AS LocationId, l.Name AS LocationName,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode
    FROM dbo.WN_Spaces    s  WITH (NOLOCK)
    JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = s.LocationId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive        = 1
      AND s.SpaceTypeId  = @SpaceTypeId
      AND (@Capacity        IS NULL OR s.Capacity >= @Capacity)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (5, 33)
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
            AND (
                @NormalizedShift = '24_7'
                OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
            )
      )
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetAvailableSpacesForReassignment]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetAvailableSpacesForReassignment]
    @SpaceTypeId      INT,
    @StartOn          DATETIME2,
    @EndOn            DATETIME2,
    @ExcludeBookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name,
        s.LocationId AS LocationId, l.Name AS LocationName,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit
    FROM dbo.WN_Spaces    s  WITH (NOLOCK)
    JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = s.LocationId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit
        FROM dbo.WN_SpacePricing sp
        WHERE sp.SpaceId      = s.Id
          AND sp.IsActive     = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive       = 1
      AND s.SpaceTypeId = @SpaceTypeId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (1, 2)
            AND bk.Id             <> @ExcludeBookingId
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
      )
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetByChallan]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 4. BOOKINGS / CHALLAN STORED PROCEDURES

CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetByChallan]
    @ChallanNumber NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        b.Id,
        CAST(b.IdGUID AS NVARCHAR(36)) AS idGuid,
        b.ChallanNumber,
        b.ValidityDate,
        b.TotalAmount,
        b.BookingStatusId   AS BookingStatus,
        b.StartOn           AS StartDateTime,
        b.EndOn             AS EndDateTime,
        b.Notes,
        b.CustomerCode,
        s.Name              AS spaceName,
        s.Code              AS spaceCode,
        st.Description      AS spaceTypeName,
        l.Name              AS locationName,
        l.CompanyId         AS companyId,
        co.CompanyName      AS companyName,
        l.BranchId          AS branchId,
        br.[Description]    AS branchName,
        u.Name              AS customerName,
        u.Email             AS customerEmail
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces       s  WITH (NOLOCK) ON s.Id        = b.SpaceId
    LEFT JOIN dbo.WN_SpaceTypes   st WITH (NOLOCK) ON st.Id       = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations    l  WITH (NOLOCK) ON l.Id        = s.LocationId
    LEFT JOIN dbo.Company         co WITH (NOLOCK) ON l.CompanyId = co.Id
    LEFT JOIN dbo.Branches        br WITH (NOLOCK) ON l.BranchId  = br.Id
    LEFT JOIN dbo.WN_Users        u  WITH (NOLOCK) ON u.Id        = b.UserId
    WHERE b.ChallanNumber = @ChallanNumber;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetByGuid]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetByGuid]
    @IdGUID NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        b.Id,
        CAST(b.IdGUID AS NVARCHAR(36)) AS idGuid,
        b.ChallanNumber,
        b.ValidityDate,
        b.TotalAmount,
        b.BookingStatusId   AS BookingStatus,
        b.StartOn           AS StartDateTime,
        b.EndOn             AS EndDateTime,
        b.Notes,
        b.CustomerCode,
        s.Name              AS spaceName,
        s.Code              AS spaceCode,
        st.Description      AS spaceTypeName,
        l.Name              AS locationName,
        l.CompanyId         AS companyId,
        co.CompanyName      AS companyName,
        l.BranchId          AS branchId,
        br.[Description]    AS branchName,
        u.Name              AS customerName,
        u.Email             AS customerEmail
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces       s  WITH (NOLOCK) ON s.Id        = b.SpaceId
    LEFT JOIN dbo.WN_SpaceTypes   st WITH (NOLOCK) ON st.Id       = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations    l  WITH (NOLOCK) ON l.Id        = s.LocationId
    LEFT JOIN dbo.Company         co WITH (NOLOCK) ON l.CompanyId = co.Id
    LEFT JOIN dbo.Branches        br WITH (NOLOCK) ON l.BranchId  = br.Id
    LEFT JOIN dbo.WN_Users        u  WITH (NOLOCK) ON u.Id        = b.UserId
    WHERE CAST(b.IdGUID AS NVARCHAR(36)) = @IdGUID;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetByPublicId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 6. PROCEDURE: dbo.WN_Bookings_GetByPublicId
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetByPublicId]
    @PublicId  UNIQUEIDENTIFIER,
    @UserEmail NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        v.*
    FROM dbo.WN_vw_BookingSummary v WITH (NOLOCK)
    WHERE v.BookingPublicId = @PublicId
      AND (@UserEmail IS NULL OR v.UserEmail = @UserEmail OR v.CustomerEmail = @UserEmail);
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetBySpaceGuid]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetBySpaceGuid]
    @SpaceGuid UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            b.Id,
            b.IdGUID,
            u.IdGUID                                            AS UserGuid,
            s.IdGUID                                            AS SpaceGuid,
            b.StartOn                                           AS StartDateTime,
            b.EndOn                                             AS EndDateTime,
            b.TotalAmount,
            b.Notes,
            b.BookingStatusId                                   AS BookingStatus,
            CASE WHEN b.IsDeleted = 1 THEN 0 ELSE 1 END        AS Status,
            b.CreatedOn,
            s.Name                                              AS SpaceName,
            DATEDIFF(DAY, b.StartOn, b.EndOn)                   AS ReservedDays
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        INNER JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
        WHERE s.IdGUID = @SpaceGuid;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetCalendar]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetCalendar]
    @SpaceId INT,
    @Year    INT,
    @Month   INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Start DATETIME2 = DATEFROMPARTS(@Year, @Month, 1);
    DECLARE @End   DATETIME2 = DATEADD(MONTH, 1, @Start);
    SELECT
        b.Id AS BookingId, b.IdGUID AS BookingPublicId,
        b.StartOn, b.EndOn,
        bs.Code  AS BookingStatusCode,
        bs.Label AS BookingStatusLabel,
        u.Name   AS UserName,
        u.Email  AS UserEmail
    FROM dbo.WN_Bookings        b  WITH (NOLOCK)
    JOIN dbo.WN_BookingStatuses bs WITH (NOLOCK) ON bs.Id = b.BookingStatusId
    JOIN dbo.WN_Users           u  WITH (NOLOCK) ON u.Id  = b.UserId
    WHERE b.SpaceId   = @SpaceId
      AND (b.IsDeleted = 0 OR b.IsDeleted IS NULL)
      AND b.StartOn   < @End
      AND b.EndOn     > @Start
    ORDER BY b.StartOn;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 5. PROCEDURE: dbo.WN_Bookings_GetList (Pass all summary columns to UI)
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetList]
    @Page        INT           = 1,
    @Limit       INT           = 20,
    @Search      NVARCHAR(255) = NULL,
    @LocationId  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @Page < 1 SET @Page = 1;
    IF @Limit < 1 SET @Limit = 20;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT
        v.*,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_vw_BookingSummary v WITH (NOLOCK)
    WHERE (@LocationId IS NULL OR v.LocationId = @LocationId)
      AND (@Search IS NULL OR @Search = ''
           OR v.UserName      LIKE '%' + @Search + '%'
           OR v.CustomerName  LIKE '%' + @Search + '%'
           OR v.UserEmail     LIKE '%' + @Search + '%'
           OR v.CustomerEmail LIKE '%' + @Search + '%'
           OR v.SpaceCode     LIKE '%' + @Search + '%'
           OR v.SpaceName     LIKE '%' + @Search + '%'
           OR v.ChallanNumber LIKE '%' + @Search + '%')
    ORDER BY v.BookedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetListByUserId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 14. WN_Bookings_GetListByUserId --
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetListByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT b.IdGUID         AS IdGuid,
           b.Id             AS Id,
           s.Name           AS SpaceName,
           b.StartOn        AS StartDateTime,
           b.EndOn          AS EndDateTime,
           b.TotalAmount    AS TotalAmount,
           b.Notes          AS Notes,
           b.BookingDate    AS CreatedAt,
           CASE b.BookingStatusId
               WHEN 5 THEN 'Pending'
               WHEN 86 THEN 'Cancelled'
               WHEN 3 THEN 'Rejected'
               WHEN 33 THEN 'Confirmed'
               ELSE 'Confirmed'
           END AS BookingStatus
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON b.SpaceId = s.Id
    WHERE b.UserId = @UserId AND (b.IsDeleted = 0 OR b.IsDeleted IS NULL)
    ORDER BY b.BookingDate DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetMyList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3. GetMyList
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetMyList]
    @UserEmail NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        v.BookingId, v.BookingPublicId,
        v.StartOn, v.EndOn,
        v.BookingStatusCode, v.BookingStatusLabel,
        v.SpaceCode, v.SpaceName, v.SpaceCapacity,
        v.SpaceTypeName, v.LocationName,
        v.SeatPrice, v.RoomPrice, v.SecurityDeposit,
        v.DiscountPercentage, v.DiscountAmount, v.SubtotalAmount, v.TotalAmount,
        v.BillingPeriodCode, v.BillingPeriodLabel,
        v.ChallanNumber, v.ChallanValidUntil,
        v.BookedOn
    FROM dbo.WN_vw_BookingSummary v
    WHERE v.UserEmail = @UserEmail
    ORDER BY v.BookedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetRecent]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 4. GetRecent
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetRecent]
    @Top INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
        v.BookingId, v.BookingPublicId,
        v.StartOn, v.EndOn,
        v.BookingStatusCode, v.BookingStatusLabel,
        v.UserName, v.UserEmail,
        v.SpaceCode, v.SpaceName,
        v.LocationName, v.CompanyName,
        v.SeatPrice, v.RoomPrice, v.SecurityDeposit,
        v.DiscountPercentage, v.DiscountAmount, v.SubtotalAmount, v.TotalAmount,
        v.BillingPeriodCode,
        v.ChallanNumber, v.BookedOn
    FROM dbo.WN_vw_BookingSummary v
    ORDER BY v.BookedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_GetSmartAvailable]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_GetSmartAvailable]
    @CategoryCode NVARCHAR(30),
    @StartOn      DATETIME2,
    @EndOn        DATETIME2,
    @Capacity     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name,
        s.LocationId AS LocationId, l.Name AS LocationName,
        s.SpaceTypeId AS SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit,
        vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationId
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice,
            sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit,
            bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId       = s.Id
          AND sp.IsActive      = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive  = 1
      AND sc.Code     = @CategoryCode
      AND (@Capacity  IS NULL OR s.Capacity >= @Capacity)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId         = s.Id
            AND bk.IsDeleted       = 0
            AND bk.BookingStatusId IN (1, 2)
            AND @StartOn           < bk.EndOn
            AND @EndOn             > bk.StartOn
      )
    ORDER BY s.Capacity ASC, TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 3. Update dbo.WN_Bookings_Insert
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_Insert]
    @UserId                INT           = NULL,
    @SpaceId               INT,
    @PricingId             INT           = 0,
    @StartOn               DATETIME2,
    @EndOn                 DATETIME2,
    @Notes                 NVARCHAR(MAX) = NULL,
    @CreatedById           INT           = NULL,
    @UserEmail             NVARCHAR(255) = NULL,
    @CustomerEmail         NVARCHAR(255) = NULL,
    @CustomerFirstName     NVARCHAR(100) = NULL,
    @CustomerLastName      NVARCHAR(100) = NULL,
    @CustomerPhone         NVARCHAR(50)  = NULL,
    @CustomerCnic          NVARCHAR(50)  = NULL,
    @CustomerAddress       NVARCHAR(500) = NULL,
    @CustomerCityId        INT           = NULL,
    @CustomerNotes         NVARCHAR(MAX) = NULL,
    @DiscountPercentage    DECIMAL(5,2)  = 0.0,
    @DiscountAmount        DECIMAL(18,2) = 0.0,
    @DiscountType          NVARCHAR(20)  = 'Percentage',
    @BillingPeriodMonths   INT           = NULL,
    @SecurityDepositMonths INT           = NULL,
    @AdvanceRentMonths     INT           = NULL,
    @SupportChargesId      TINYINT       = NULL,
    @Capacity              INT           = NULL,
    @OverrideDuration      DECIMAL(18,2) = NULL,
    @OverrideSubtotal      DECIMAL(18,2) = NULL,
    @OverrideUnitPrice     DECIMAL(18,2) = NULL,
    @CustomerCode          NVARCHAR(50)  = NULL,
    @ShiftType             NVARCHAR(20)  = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @NormalizedShift NVARCHAR(20) = CASE 
            WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
            WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
            WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
            ELSE LOWER(@ShiftType)
        END;

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Bookings bk WITH (UPDLOCK, HOLDLOCK)
            WHERE bk.SpaceId = @SpaceId
              AND bk.IsDeleted = 0
              AND bk.BookingStatusId IN (5, 33)
              AND @StartOn < bk.EndOn
              AND @EndOn > bk.StartOn
              AND (
                  @NormalizedShift = '24_7'
                  OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                  OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
              )
        )
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'Space is already booked for the selected date range and shift.' AS ErrorMessage;
            RETURN;
        END

        IF @UserId IS NULL
        BEGIN
            IF @UserEmail IS NOT NULL AND @UserEmail <> ''
            BEGIN
                SELECT TOP 1 @UserId = Id FROM dbo.WN_Users WHERE Email = @UserEmail;
            END

            IF @UserId IS NULL AND @CustomerEmail IS NOT NULL AND @CustomerEmail <> ''
            BEGIN
                SELECT TOP 1 @UserId = Id FROM dbo.WN_Users WHERE Email = @CustomerEmail;
                IF @UserId IS NULL
                BEGIN
                    SELECT TOP 1 @UserId = UserId FROM dbo.WN_Customers WHERE Email = @CustomerEmail;
                END
            END
        END

        IF @CustomerCode IS NULL AND @UserId IS NOT NULL
        BEGIN
            SELECT TOP 1 @CustomerCode = Code FROM dbo.WN_Customers WHERE UserId = @UserId;
        END

        DECLARE @CategoryCode NVARCHAR(50) = NULL;
        DECLARE @LocationId INT = NULL;
        DECLARE @BillingPeriodCode NVARCHAR(20) = 'Monthly';
        DECLARE @SeatPrice DECIMAL(18,2) = 0.0;
        DECLARE @SecurityDeposit DECIMAL(18,2) = 0.0;
        DECLARE @AccountReceivableId INT = NULL;
        DECLARE @RentAccountId INT = NULL;
        DECLARE @ServicesIncomeId INT = NULL;
        DECLARE @SalesTaxId INT = NULL;
        DECLARE @SecurityReceivedId INT = NULL;
        DECLARE @ResolvedPricingId INT = @PricingId;

        -- 1. Try resolving accounts from WN_Spaces & WN_SpaceTypes
        SELECT TOP 1 
            @CategoryCode        = sc.Code,
            @LocationId          = s.LocationId,
            @BillingPeriodCode   = ISNULL(bp.Code, 'Monthly'),
            @SeatPrice           = ISNULL(s.Price, 0.0),
            @AccountReceivableId = COALESCE(s.AccountReceivableId, st.AccountReceivableId),
            @RentAccountId       = COALESCE(s.RentAccountId, st.RentAccountId),
            @ServicesIncomeId    = COALESCE(s.ServicesIncomeId, st.ServicesIncomeId),
            @SalesTaxId          = COALESCE(s.SalesTaxId, st.SalesTaxId),
            @SecurityReceivedId  = COALESCE(s.SecurityReceivedId, st.SecurityReceivedId, s.SecurityRecivedId)
        FROM dbo.WN_Spaces s WITH (NOLOCK)
        JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
        LEFT JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id = s.BillingPeriodId
        WHERE s.Id = @SpaceId;

        -- 2. Try resolving accounts from WN_SpaceConfig
        IF (@RentAccountId IS NULL OR @SecurityReceivedId IS NULL OR @AccountReceivableId IS NULL OR @SalesTaxId IS NULL OR @ServicesIncomeId IS NULL)
        BEGIN
            SELECT TOP 1
                @AccountReceivableId = COALESCE(@AccountReceivableId, cfg.AccountReceivableId),
                @RentAccountId       = COALESCE(@RentAccountId, cfg.RentAccountId),
                @ServicesIncomeId    = COALESCE(@ServicesIncomeId, cfg.ServicesIncomeId),
                @SalesTaxId          = COALESCE(@SalesTaxId, cfg.SalesTaxId),
                @SecurityReceivedId  = COALESCE(@SecurityReceivedId, cfg.SecurityReceivedId)
            FROM dbo.WN_SpaceConfig cfg WITH (NOLOCK)
            WHERE cfg.SpaceTypeId = (SELECT SpaceTypeId FROM dbo.WN_Spaces WHERE Id = @SpaceId)
              AND (cfg.LocationId = @LocationId OR cfg.LocationId IS NULL)
            ORDER BY CASE WHEN cfg.LocationId = @LocationId THEN 0 ELSE 1 END;
        END

        -- 3. Fallback to WN_ChargeTypeAccountMapping
        IF @RentAccountId IS NULL
            SELECT TOP 1 @RentAccountId = RentAccountId 
            FROM dbo.WN_ChargeTypeAccountMapping WITH (NOLOCK) 
            WHERE ChargeTypeId = 1 AND (EffectiveTo IS NULL OR EffectiveTo >= CAST(GETDATE() AS DATE));

        IF @SecurityReceivedId IS NULL
            SELECT TOP 1 @SecurityReceivedId = SecurityReceivedId 
            FROM dbo.WN_ChargeTypeAccountMapping WITH (NOLOCK) 
            WHERE ChargeTypeId = 2 AND (EffectiveTo IS NULL OR EffectiveTo >= CAST(GETDATE() AS DATE));

        IF @SalesTaxId IS NULL
            SELECT TOP 1 @SalesTaxId = SalesTaxId 
            FROM dbo.WN_ChargeTypeAccountMapping WITH (NOLOCK) 
            WHERE ChargeTypeId = 3 AND (EffectiveTo IS NULL OR EffectiveTo >= CAST(GETDATE() AS DATE));

        IF @ServicesIncomeId IS NULL
            SELECT TOP 1 @ServicesIncomeId = ServicesIncomeId 
            FROM dbo.WN_ChargeTypeAccountMapping WITH (NOLOCK) 
            WHERE ChargeTypeId = 4 AND (EffectiveTo IS NULL OR EffectiveTo >= CAST(GETDATE() AS DATE));

        IF @AccountReceivableId IS NULL
            SELECT TOP 1 @AccountReceivableId = AccountReceivableId 
            FROM dbo.WN_ChargeTypeAccountMapping WITH (NOLOCK) 
            WHERE ChargeTypeId = 1 AND (EffectiveTo IS NULL OR EffectiveTo >= CAST(GETDATE() AS DATE));

        -- Duration and pricing calculations
        DECLARE @Duration DECIMAL(18,2) = 1.0;
        DECLARE @RentAmount DECIMAL(18,2) = 0.0;
        DECLARE @AdvMonths INT = 1;
        DECLARE @SecMonths INT = 0;
        DECLARE @BillPeriodMonths INT = 1;

        IF @BillingPeriodCode = 'Daily'
        BEGIN
            SET @Duration = CAST(DATEDIFF(day, @StartOn, @EndOn) AS DECIMAL(18,2));
            IF @Duration <= 0 SET @Duration = 1.0;
            SET @RentAmount = @SeatPrice * @Duration * ISNULL(@Capacity, 1);
            SET @AdvMonths = 0;
            SET @SecMonths = 0;
            SET @BillPeriodMonths = 0;
        END
        ELSE IF @BillingPeriodCode = 'Hourly'
        BEGIN
            SET @Duration = CAST(DATEDIFF(hour, @StartOn, @EndOn) AS DECIMAL(18,2));
            IF @Duration <= 0 SET @Duration = 1.0;
            SET @RentAmount = @SeatPrice * @Duration * ISNULL(@Capacity, 1);
            SET @AdvMonths = 0;
            SET @SecMonths = 0;
            SET @BillPeriodMonths = 0;
        END
        ELSE
        BEGIN
            IF @BillingPeriodMonths IS NOT NULL AND @BillingPeriodMonths > 0
                SET @Duration = CAST(@BillingPeriodMonths AS DECIMAL(18,2));
            ELSE
                SET @Duration = CAST(DATEDIFF(month, @StartOn, @EndOn) AS DECIMAL(18,2));

            IF @Duration <= 0 SET @Duration = 1.0;

            IF @CategoryCode IN ('PrivateOffice', 'Private')
            BEGIN
                SET @RentAmount = @SeatPrice * ISNULL(@Capacity, 1) * ISNULL(@AdvanceRentMonths, 1);
            END
            ELSE
            BEGIN
                SET @RentAmount = @SeatPrice * ISNULL(@AdvanceRentMonths, 1);
            END

            SET @AdvMonths = ISNULL(@AdvanceRentMonths, 1);
            SET @SecMonths = ISNULL(@SecurityDepositMonths, 0);
            SET @BillPeriodMonths = ISNULL(@BillingPeriodMonths, 1);
        END

        IF @OverrideDuration IS NOT NULL AND @OverrideDuration > 0
            SET @Duration = @OverrideDuration;
        IF @OverrideSubtotal IS NOT NULL AND @OverrideSubtotal > 0
            SET @RentAmount = @OverrideSubtotal;

        DECLARE @MonthlyRent DECIMAL(18,2) = 0.0;
        IF @CategoryCode IN ('PrivateOffice', 'Private')
            SET @MonthlyRent = @SeatPrice * ISNULL(@Capacity, 1);
        ELSE
            SET @MonthlyRent = @SeatPrice;

        IF (@MonthlyRent <= 0 OR @MonthlyRent IS NULL) AND @OverrideSubtotal IS NOT NULL AND @OverrideSubtotal > 0 AND ISNULL(@AdvMonths, 1) > 0
        BEGIN
            SET @MonthlyRent = ROUND(@OverrideSubtotal / ISNULL(@AdvMonths, 1), 2);
        END

        DECLARE @MonthlyDepositRate DECIMAL(18,2) = 0.0;
        IF @SecurityDeposit IS NOT NULL AND @SecurityDeposit > 0
            SET @MonthlyDepositRate = @SecurityDeposit;
        ELSE IF @CategoryCode IN ('PrivateOffice', 'Private', 'SharedSpace', 'Shared')
            SET @MonthlyDepositRate = @MonthlyRent;

        IF @SecMonths > 0
            SET @SecurityDeposit = @MonthlyDepositRate * @SecMonths;
        ELSE
            SET @SecurityDeposit = 0.00;

        DECLARE @DiscountAmt DECIMAL(18,2) = ISNULL(@DiscountAmount, 0);
        IF @DiscountAmt <= 0 AND ISNULL(@DiscountPercentage, 0) > 0
            SET @DiscountAmt = ROUND(@RentAmount * (@DiscountPercentage / 100.0), 2);
        ELSE IF @DiscountAmt > 0 AND ISNULL(@DiscountPercentage, 0) <= 0 AND @RentAmount > 0
            SET @DiscountPercentage = ROUND((@DiscountAmt / @RentAmount) * 100.0, 2);

        DECLARE @DiscountOnRent DECIMAL(18,2) = 0.00;
        DECLARE @DiscountOnDeposit DECIMAL(18,2) = 0.00;

        IF @DiscountAmt > 0
        BEGIN
            IF @DiscountAmt <= @RentAmount
            BEGIN
                SET @DiscountOnRent = @DiscountAmt;
                SET @DiscountOnDeposit = 0.00;
            END
            ELSE
            BEGIN
                SET @DiscountOnRent = @RentAmount;
                SET @DiscountOnDeposit = @DiscountAmt - @RentAmount;
                IF @DiscountOnDeposit > @SecurityDeposit
                    SET @DiscountOnDeposit = @SecurityDeposit;
            END
        END

        DECLARE @DiscountedRent DECIMAL(18,2) = @RentAmount - @DiscountOnRent;
        DECLARE @DiscountedDeposit DECIMAL(18,2) = @SecurityDeposit - @DiscountOnDeposit;
        DECLARE @TotalDiscountAmount DECIMAL(18,2) = @DiscountOnRent + @DiscountOnDeposit;

        -- Tax & Support Charges Calculation
        DECLARE @SupportChargeAmount DECIMAL(18,2) = 0.00;
        DECLARE @TaxAmount DECIMAL(18,2) = 0.00;
        DECLARE @AppliedChargePercentage DECIMAL(5,2) = 0.00;
        DECLARE @AppliedTaxPercentage DECIMAL(5,2) = 16.00;

        IF @SupportChargesId IS NOT NULL AND @CategoryCode IN ('PrivateOffice', 'Private')
        BEGIN
            DECLARE @ChargePercentage DECIMAL(5,2);
            DECLARE @TaxApplicable BIT = 1;
            DECLARE @ProvinceId INT = NULL;

            SELECT TOP 1 @ProvinceId = loc.ProvinceId
            FROM dbo.WN_Locations loc WITH (NOLOCK)
            WHERE loc.Id = @LocationId;

            DECLARE @BookingDateAnchor DATE = CAST(SYSUTCDATETIME() AS DATE);

            SELECT TOP 1 @ChargePercentage = ChargePercentage
            FROM dbo.WN_ChargeTypeRate WITH (NOLOCK)
            WHERE ChargeTypeId = @SupportChargesId
              AND StartDate <= @BookingDateAnchor
              AND (EndDate IS NULL OR EndDate > @BookingDateAnchor)
            ORDER BY StartDate DESC;

            IF @ChargePercentage IS NULL SET @ChargePercentage = 10.00;

            SET @AppliedChargePercentage = @ChargePercentage;
            SET @SupportChargeAmount = ROUND(@RentAmount * (@ChargePercentage / 100.0), 2);

            IF @ProvinceId IS NOT NULL
            BEGIN
                SELECT TOP 1 @TaxApplicable = ISNULL(TaxApplicable, 1)
                FROM dbo.WN_ProvinceChargeTypeTax WITH (NOLOCK)
                WHERE ProvinceId = @ProvinceId AND ChargeTypeId = @SupportChargesId;

                IF @TaxApplicable = 1
                BEGIN
                    SELECT TOP 1 @AppliedTaxPercentage = ISNULL(TaxPercentage, 16.00)
                    FROM dbo.WN_Tax WITH (NOLOCK)
                    WHERE ProvinceId = @ProvinceId
                      AND StartDate <= @BookingDateAnchor
                      AND (EndDate IS NULL OR EndDate > @BookingDateAnchor)
                    ORDER BY StartDate DESC;
                END
            END

            IF @TaxApplicable = 1
            BEGIN
                SET @TaxAmount = ROUND(@SupportChargeAmount * (@AppliedTaxPercentage / 100.0), 2);
            END
            ELSE
            BEGIN
                SET @TaxAmount = 0.00;
            END
        END

        DECLARE @TotalAmount DECIMAL(18,2) = @DiscountedRent + @DiscountedDeposit + @SupportChargeAmount + @TaxAmount;
        DECLARE @ChallanNum NVARCHAR(50) = 'WN-' + CONVERT(NVARCHAR(8), SYSUTCDATETIME(), 112) + '-' + RIGHT(CAST(ABS(CHECKSUM(NEWID())) AS NVARCHAR(10)), 4);
        DECLARE @ChallanValidUntil DATETIME2 = DATEADD(day, 7, SYSUTCDATETIME());
        DECLARE @CreatedByGuid UNIQUEIDENTIFIER = NULL;
        IF @CreatedById IS NOT NULL
            SELECT @CreatedByGuid = IdGUID FROM dbo.WN_Users WHERE Id = @CreatedById;

        -- Insert Booking with all 5 Account Mapping columns
        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, CustomerCode,
            RentAccountId, SecurityRecivedId, SecurityReceivedId, AccountReceivableId, ServicesIncomeId, SalesTaxId,
            Notes, RejectReason, CreatedOn, UpdatedOn, TransactionDate,
            ChallanNumber, ValidityDate, UserId, SpaceId, PricingId,
            StartOn, EndOn, BookingStatusId, CancelReason, IsDeleted, CreatedById, UpdatedById,
            DiscountPercentage, DiscountAmount, DiscountType, SubtotalAmount, TotalAmount,
            MonthlyRent, BillingPeriodMonths, AdvanceRentMonths, SecurityDepositMonths, SecurityDepositRequired, SecurityDepositPaid,
            ShiftType
        )
        VALUES (
            NEWID(), SYSUTCDATETIME(), @CustomerCode,
            @RentAccountId, @SecurityReceivedId, @SecurityReceivedId, @AccountReceivableId, @ServicesIncomeId, @SalesTaxId,
            @Notes, NULL, SYSUTCDATETIME(), NULL, SYSUTCDATETIME(),
            @ChallanNum, @ChallanValidUntil, @UserId, @SpaceId, @ResolvedPricingId,
            @StartOn, @EndOn, 1, NULL, 0, @CreatedById, NULL,
            @DiscountPercentage, @TotalDiscountAmount, ISNULL(@DiscountType, 'Percentage'), @RentAmount, @TotalAmount,
            @MonthlyRent, @BillPeriodMonths, @AdvMonths, @SecMonths, @SecurityDeposit, 0.00,
            @NormalizedShift
        );

        DECLARE @BookingId INT = SCOPE_IDENTITY();
        DECLARE @BookingGuid UNIQUEIDENTIFIER;
        SELECT @BookingGuid = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;

        DECLARE @SpaceName NVARCHAR(255), @SpaceCode NVARCHAR(50), @SpaceCategory NVARCHAR(50);
        SELECT TOP 1 @SpaceName = s.Name, @SpaceCode = s.Code, @SpaceCategory = sc.Label
        FROM dbo.WN_Spaces s WITH (NOLOCK)
        JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
        WHERE s.Id = @SpaceId;

        DECLARE @CustomerFullName NVARCHAR(255) = RTRIM(LTRIM(ISNULL(@CustomerFirstName, '') + ' ' + ISNULL(@CustomerLastName, '')));
        IF @CustomerFullName = '' SET @CustomerFullName = ISNULL(@CustomerEmail, 'Customer');

        EXEC dbo.WN_BookingDetails_Insert
            @BookingGuid             = @BookingGuid,
            @CustomerCode            = @CustomerCode,
            @CustomerName            = @CustomerFullName,
            @CustomerEmail           = @CustomerEmail,
            @SpaceName               = @SpaceName,
            @SpaceCode               = @SpaceCode,
            @SpaceCategory           = @SpaceCategory,
            @StartDateTime           = @StartOn,
            @EndDateTime             = @EndOn,
            @RentAmount              = @RentAmount,
            @SecurityDeposit         = @SecurityDeposit,
            @RentAccountId           = @RentAccountId,
            @DepositAccountId        = @SecurityReceivedId,
            @Notes                   = @Notes,
            @SupportChargesId        = @SupportChargesId,
            @AppliedChargePercentage = @AppliedChargePercentage,
            @AppliedTaxPercentage    = @AppliedTaxPercentage,
            @SupportChargeAmount     = @SupportChargeAmount,
            @TaxAmount               = @TaxAmount,
            @AccountReceivableId     = @AccountReceivableId,
            @ServicesIncomeId        = @ServicesIncomeId,
            @SalesTaxId              = @SalesTaxId,
            @SecurityReceivedId      = @SecurityReceivedId;

        INSERT INTO dbo.WN_Challans
            (BookingId, ChallanNumber, ValidUntil, StatusId, CreatedById)
        VALUES
            (@BookingId, @ChallanNum, @ChallanValidUntil, 1, @CreatedById);

        DECLARE @UnitPrice DECIMAL(18,2);
        IF @OverrideUnitPrice IS NOT NULL AND @OverrideUnitPrice > 0
            SET @UnitPrice = @OverrideUnitPrice;
        ELSE IF @CategoryCode IN ('PrivateOffice', 'Private')
            SET @UnitPrice = @SeatPrice * ISNULL(@Capacity, 1);
        ELSE
            SET @UnitPrice = @SeatPrice;

        DECLARE @EffectiveRentQuantity DECIMAL(18,2) = CASE 
            WHEN @BillingPeriodCode = 'Monthly' AND ISNULL(@AdvMonths, 0) > 0 THEN CAST(@AdvMonths AS DECIMAL(18,2))
            WHEN ISNULL(@Duration, 0) > 0 THEN @Duration
            ELSE 1.0
        END;
        IF @EffectiveRentQuantity <= 0 SET @EffectiveRentQuantity = 1.0;

        DECLARE @EffectiveSecMonths DECIMAL(18,2) = CASE WHEN ISNULL(@SecMonths, 0) > 0 THEN CAST(@SecMonths AS DECIMAL(18,2)) ELSE 1.0 END;
        DECLARE @EffectiveDepositUnitPrice DECIMAL(18,2) = CASE WHEN ISNULL(@SecMonths, 0) > 0 THEN @MonthlyDepositRate ELSE @SecurityDeposit END;

        INSERT INTO dbo.WN_BookingLines
            (BookingId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, IsDeleted, CreatedOn, CreatedById,
             AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, SecurityReceivedId)
        VALUES
            (@BookingId, 1, 'Room Rent', @EffectiveRentQuantity, @UnitPrice, @DiscountOnRent, 0.0000, @RentAccountId, 0, SYSUTCDATETIME(), @CreatedById,
             @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @SecurityReceivedId);

        IF @SecurityDeposit > 0
        BEGIN
            INSERT INTO dbo.WN_BookingLines
                (BookingId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, IsDeleted, CreatedOn, CreatedById,
                 AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, SecurityReceivedId)
            VALUES
                (@BookingId, 2, 'Security Deposit (Refundable)', @EffectiveSecMonths, @EffectiveDepositUnitPrice, @DiscountOnDeposit, 0.0000, @SecurityReceivedId, 0, SYSUTCDATETIME(), @CreatedById,
                 @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @SecurityReceivedId);

            -- Insert Security Deposit record with CustomerId (CustomerCode)
            INSERT INTO dbo.WN_SecurityDeposits
                (BookingId, UserId, Amount, AccountId, StatusId, Notes, UpdatedById, SecurityReceivedId,
                 AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, CustomerId, RefId, RefNo)
            VALUES
                (@BookingId, @UserId, @SecurityDeposit, @SecurityReceivedId, 1, 'Initial security deposit for booking ' + ISNULL(@ChallanNum, CAST(@BookingId AS NVARCHAR(20))), @CreatedById, @SecurityReceivedId,
                 @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @CustomerCode, NULL, NULL);
        END

        IF @SupportChargesId IS NOT NULL AND @SupportChargeAmount > 0
        BEGIN
            INSERT INTO dbo.WN_BookingLines
                (BookingId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, IsDeleted, CreatedOn, CreatedById,
                 AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, SecurityReceivedId)
            VALUES
                (@BookingId, @SupportChargesId, 'Support Services Portion', 1, @SupportChargeAmount, 0.00, @AppliedTaxPercentage / 100.0, @ServicesIncomeId, 0, SYSUTCDATETIME(), @CreatedById,
                 @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @SecurityReceivedId);
        END

        COMMIT TRANSACTION;

        SELECT 
            @BookingId AS BookingId,
            @BookingGuid AS BookingPublicId,
            @ChallanNum AS ChallanNumber,
            @ChallanValidUntil AS ChallanValidUntil,
            NULL AS ErrorMessage,
            @RentAmount AS SubtotalAmount,
            @TotalDiscountAmount AS DiscountAmount,
            @TaxAmount AS TaxAmount,
            @SecurityDeposit AS SecurityDeposit,
            @TotalAmount AS TotalAmount,
            @MonthlyRent AS MonthlyRent,
            @BillPeriodMonths AS BillingPeriodMonths,
            @NormalizedShift AS ShiftType;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT NULL AS BookingId, NULL AS BookingPublicId,
               NULL AS ChallanNumber, NULL AS ChallanValidUntil,
               ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_InsertFreeMeetingRoom]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_InsertFreeMeetingRoom]
    @UserId         INT,
    @SpaceId        INT,
    @StartOn        DATETIME2,
    @DurationHours  INT,
    @Notes          NVARCHAR(MAX) = NULL,
    @CreatedById    INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        IF @DurationHours NOT IN (1, 2, 3)
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, 'Duration must be exactly 1, 2, or 3 hours.' AS ErrorMessage;
            RETURN;
        END

        DECLARE @EndOn DATETIME2 = DATEADD(HOUR, @DurationHours, @StartOn);
        DECLARE @DurationMinutes INT = @DurationHours * 60;
        DECLARE @UsageDate DATE = CAST(@StartOn AS DATE);

        DECLARE @SpaceGuid UNIQUEIDENTIFIER;
        DECLARE @SpaceCode NVARCHAR(50);
        DECLARE @SpaceName NVARCHAR(255);
        DECLARE @SpaceCategoryId TINYINT;

        SELECT 
            @SpaceGuid       = s.IdGUID,
            @SpaceCode       = s.Code,
            @SpaceName       = s.Name,
            @SpaceCategoryId = st.CategoryId
        FROM dbo.WN_Spaces s WITH (NOLOCK)
        LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        WHERE s.Id = @SpaceId AND s.IsActive = 1;

        IF @SpaceGuid IS NULL OR @SpaceCategoryId <> 2
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, 'Selected space is not an active meeting room.' AS ErrorMessage;
            RETURN;
        END

        DECLARE @PrivateBookingId INT = NULL;
        SELECT TOP 1 @PrivateBookingId = b.Id
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        WHERE b.UserId = @UserId
          AND b.BookingStatusId = 33
          AND b.IsDeleted = 0
          AND st.CategoryId = 1
          AND @UsageDate >= CAST(b.StartOn AS DATE)
          AND @UsageDate <= CAST(b.EndOn AS DATE)
        ORDER BY b.StartOn ASC;

        IF @PrivateBookingId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, 'No active private-space booking covers the requested date.' AS ErrorMessage;
            RETURN;
        END

        DECLARE @EntitlementId INT = NULL;
        DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@UsageDate), MONTH(@UsageDate), 1);
        DECLARE @AllocatedMinutes INT = 360;

        SELECT @EntitlementId = Id, @AllocatedMinutes = AllocatedMinutes
        FROM dbo.WN_BookingMeetingRoomEntitlements WITH (UPDLOCK, HOLDLOCK)
        WHERE BookingId = @PrivateBookingId AND MonthStartDate = @MonthStart AND Status = 'Active';

        IF @EntitlementId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, 'No active meeting room entitlement found for this month.' AS ErrorMessage;
            RETURN;
        END

        DECLARE @UsedMinutes INT = 0;
        SELECT @UsedMinutes = ISNULL(SUM(UsedMinutes), 0)
        FROM dbo.WN_MeetingRoomUsageLogs WITH (UPDLOCK, HOLDLOCK)
        WHERE EntitlementId = @EntitlementId AND Status <> 'Cancelled';

        IF (@UsedMinutes + @DurationMinutes) > @AllocatedMinutes
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, 'Insufficient remaining free meeting-room hours for this month.' AS ErrorMessage;
            RETURN;
        END

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Bookings WITH (UPDLOCK, HOLDLOCK)
            WHERE SpaceId         = @SpaceId
              AND IsDeleted       = 0
              AND BookingStatusId IN (5, 33)
              AND @StartOn        < EndOn
              AND @EndOn          > StartOn
        )
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, 'Selected meeting room is not available for the requested period (overlapping booking).' AS ErrorMessage;
            RETURN;
        END

        DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);
        DECLARE @SeqNum INT;
        DECLARE @ChallanNum NVARCHAR(50);

        UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
        SET LastNumber = LastNumber + 1
        WHERE CounterDate = @Today;

        IF @@ROWCOUNT = 0
            INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
            VALUES (@Today, 1);

        SELECT @SeqNum = LastNumber
        FROM dbo.WN_ChallanCounter WITH (NOLOCK)
        WHERE CounterDate = @Today;

        SET @ChallanNum = 'WN-FREE-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                        + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);

        DECLARE @UserGuid UNIQUEIDENTIFIER;
        SELECT @UserGuid = IdGUID FROM dbo.WN_Users WHERE Id = @UserId;
        
        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, CustomerCode,
            RentAccountId, SecurityRecivedId, Notes, RejectReason,
            CreatedOn, UpdatedOn, TransactionDate,
            ChallanNumber, ValidityDate, UserId, SpaceId, PricingId,
            StartOn, EndOn, BookingStatusId, CancelReason, IsDeleted, CreatedById, UpdatedById,
            IsFree, EntitlementId, TotalAmount, ShiftType
        )
        VALUES (
            NEWID(), SYSUTCDATETIME(), NULL,
            NULL, NULL, @Notes, NULL,
            SYSUTCDATETIME(), NULL, SYSUTCDATETIME(),
            @ChallanNum, @Today, @UserId, @SpaceId, 0,
            @StartOn, @EndOn, 33,
            NULL, 0, @CreatedById, NULL,
            1, @EntitlementId, 0.00, '24_7'
        );

        DECLARE @BookingId INT = SCOPE_IDENTITY();
        DECLARE @BookingGuid UNIQUEIDENTIFIER;
        SELECT @BookingGuid = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;

        INSERT INTO dbo.WN_MeetingRoomUsageLogs (
            UserId, PrivateSpaceBookingId, EntitlementId, MeetingRoomBookingId, MeetingRoomSpaceId,
            UsageDate, StartDateTime, EndDateTime, AllocatedMinutes, UsedMinutes, DurationHours,
            Status, CreatedDate, CreatedById
        )
        VALUES (
            @UserId, @PrivateBookingId, @EntitlementId, @BookingId, @SpaceId,
            @UsageDate, @StartOn, @EndOn, @AllocatedMinutes, @DurationMinutes, @DurationHours,
            'Confirmed', SYSUTCDATETIME(), @CreatedById
        );

        COMMIT TRANSACTION;

        SELECT 
            @BookingId AS BookingId,
            @BookingGuid AS BookingPublicId,
            @ChallanNum AS ChallanNumber,
            @Today AS ChallanValidUntil,
            NULL AS ErrorMessage;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT NULL AS BookingId, NULL AS BookingPublicId, NULL AS ChallanNumber, NULL AS ChallanValidUntil, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_InsertSmart]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 9. PROCEDURE: dbo.WN_Bookings_InsertSmart
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_InsertSmart]
    @UserEmail         NVARCHAR(256),
    @CategoryCode      NVARCHAR(30),
    @StartOn           DATETIME2,
    @EndOn             DATETIME2,
    @Capacity          INT           = NULL,
    @Notes             NVARCHAR(MAX) = NULL,
    @CreatedById       INT           = NULL,
    @CustomerEmail     NVARCHAR(255) = NULL,
    @CustomerFirstName NVARCHAR(100) = NULL,
    @CustomerLastName  NVARCHAR(100) = NULL,
    @CustomerPhone     NVARCHAR(50)  = NULL,
    @CustomerCnic      NVARCHAR(50)  = NULL,
    @CustomerAddress   NVARCHAR(500) = NULL,
    @CustomerCityId    INT           = NULL,
    @CustomerNotes     NVARCHAR(1000) = NULL,
    @ShiftType         NVARCHAR(20)  = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @NormalizedShift NVARCHAR(20) = CASE 
            WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
            WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
            WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
            ELSE LOWER(@ShiftType)
        END;

        DECLARE @UserId    INT;
        DECLARE @SpaceId   INT;
        DECLARE @PricingId INT;

        SELECT @UserId = Id FROM dbo.WN_Users WITH (NOLOCK)
        WHERE Email = @UserEmail AND IsActive = 1;

        IF @UserId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'User not found or inactive.' AS ErrorMessage;
            RETURN;
        END

        SELECT TOP 1 @SpaceId = s.Id
        FROM dbo.WN_Spaces          s  WITH (UPDLOCK)
        JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
        WHERE s.IsActive  = 1
          AND sc.Code     = @CategoryCode
          AND (@Capacity  IS NULL OR s.Capacity >= @Capacity)
          AND NOT EXISTS (
              SELECT 1 FROM dbo.WN_Bookings bk
              WHERE bk.SpaceId         = s.Id
                AND bk.IsDeleted       = 0
                AND bk.BookingStatusId IN (5, 33)
                AND @StartOn           < bk.EndOn
                AND @EndOn             > bk.StartOn
                AND (
                    @NormalizedShift = '24_7'
                    OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                    OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
                )
          )
        ORDER BY s.Capacity ASC, TRY_CAST(s.Code AS INT), s.Code;

        IF @SpaceId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'No available space for the requested period.' AS ErrorMessage;
            RETURN;
        END

        DECLARE @BillingPeriodCode NVARCHAR(20);
        IF @CategoryCode = 'PrivateOffice' OR @CategoryCode = 'SharedSpace'
            SET @BillingPeriodCode = 'Monthly';
        ELSE
            SET @BillingPeriodCode = 'Hourly';

        SELECT TOP 1 @PricingId = sp.Id
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId       = @SpaceId
          AND sp.IsActive      = 1
          AND bp.Code          = @BillingPeriodCode
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC;

        IF @PricingId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS BookingId, NULL AS BookingPublicId,
                   NULL AS ChallanNumber, NULL AS ChallanValidUntil,
                   'No active ' + @BillingPeriodCode + ' pricing found for selected space.' AS ErrorMessage;
            RETURN;
        END

        COMMIT TRANSACTION;

        EXEC dbo.WN_Bookings_Insert
            @UserId            = @UserId,
            @SpaceId           = @SpaceId,
            @PricingId         = @PricingId,
            @StartOn           = @StartOn,
            @EndOn             = @EndOn,
            @Notes             = @Notes,
            @CreatedById       = @CreatedById,
            @UserEmail         = @UserEmail,
            @CustomerEmail     = @CustomerEmail,
            @CustomerFirstName = @CustomerFirstName,
            @CustomerLastName  = @CustomerLastName,
            @CustomerPhone     = @CustomerPhone,
            @CustomerCnic      = @CustomerCnic,
            @CustomerAddress   = @CustomerAddress,
            @CustomerCityId    = @CustomerCityId,
            @CustomerNotes     = @CustomerNotes,
            @ShiftType         = @NormalizedShift;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT NULL AS BookingId, NULL AS BookingPublicId,
               NULL AS ChallanNumber, NULL AS ChallanValidUntil,
               ERROR_MESSAGE() AS ErrorMessage;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Reassign]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_Reassign]
    @Id           INT,
    @NewSpaceId   INT,
    @NewPricingId INT,
    @UserEmail    NVARCHAR(256) = NULL,
    @UpdatedById  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @StartOn DATETIME2, @EndOn DATETIME2;
        SELECT @StartOn = StartOn, @EndOn = EndOn
        FROM dbo.WN_Bookings WHERE Id = @Id AND IsDeleted = 0;

        IF @StartOn IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Booking not found.', 16, 1); RETURN;
        END

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Bookings WITH (UPDLOCK)
            WHERE SpaceId         = @NewSpaceId
              AND IsDeleted       = 0
              AND BookingStatusId IN (1, 2)
              AND Id             <> @Id
              AND @StartOn        < EndOn
              AND @EndOn          > StartOn
        )
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('New space is not available for the booking period.', 16, 1); RETURN;
        END

        UPDATE dbo.WN_Bookings SET
            SpaceId     = @NewSpaceId,
            PricingId   = @NewPricingId,
            UpdatedOn   = SYSUTCDATETIME(),
            UpdatedById = @UpdatedById
        WHERE Id = @Id;

        COMMIT TRANSACTION;
        SELECT @@ROWCOUNT AS AffectedRows;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_Update]
    @Id          INT,
    @StartOn     DATETIME2     = NULL,
    @EndOn       DATETIME2     = NULL,
    @Notes       NVARCHAR(MAX) = NULL,
    @UpdatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Bookings SET
        StartOn     = ISNULL(@StartOn, StartOn),
        EndOn       = ISNULL(@EndOn,   EndOn),
        Notes       = ISNULL(@Notes,   Notes),
        UpdatedOn   = SYSUTCDATETIME(),
        UpdatedById = @UpdatedById
    WHERE Id = @Id AND IsDeleted = 0;
    SELECT @@ROWCOUNT AS AffectedRows;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Bookings_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Bookings_UpdateStatus]
    @Id              INT,
    @BookingStatusId TINYINT,
    @UpdatedById     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Bookings SET
        BookingStatusId = @BookingStatusId,
        UpdatedOn       = SYSUTCDATETIME(),
        UpdatedById     = @UpdatedById
    WHERE Id = @Id AND IsDeleted = 0;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_BookTour_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 20. WN_BookTour_Insert --
CREATE OR ALTER PROCEDURE [dbo].[WN_BookTour_Insert]
    @Name        NVARCHAR(255),
    @Email       NVARCHAR(256),
    @Message     NVARCHAR(MAX),
    @PhoneNumber NVARCHAR(20),
    @UserId      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NewGUID  UNIQUEIDENTIFIER = NEWID();
    DECLARE @UserGUID UNIQUEIDENTIFIER = NULL;

    IF @UserId IS NOT NULL
        SELECT @UserGUID = IdGUID FROM dbo.WN_Users WITH (NOLOCK) WHERE Id = @UserId;

    INSERT INTO dbo.WN_BookTour (IdGUID, Name, Email, Message, PhoneNumber, CreatedOn, CreatedBy)
    VALUES (@NewGUID, @Name, @Email, @Message, @PhoneNumber, GETDATE(), @UserGUID);

    SELECT SCOPE_IDENTITY() AS NewId, @NewGUID AS NewIdGuid;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Branches_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Branches_GetList]
    @CompanyId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Id, 
           b.Id AS PublicId, 
           b.[Description] AS Name, 
           b.Code, 
           b.CompanyId,
           co.CompanyName AS CompanyName, 
           b.CityId, 
           ci.Description AS CityName, 
           CASE WHEN b.Status = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsActive
    FROM dbo.Branches  b  WITH (NOLOCK)
    JOIN dbo.Company co WITH (NOLOCK) ON co.Id = b.CompanyId
    LEFT JOIN dbo.City ci WITH (NOLOCK) ON ci.Id = b.CityId
    WHERE b.Status = 1
      AND (@CompanyId IS NULL OR b.CompanyId = @CompanyId)
    ORDER BY b.[Description] ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challan_ExtendValidity]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Challan_ExtendValidity]
    @BookingId      INT,
    @NewExpiryDate  DATE,
    @UpdatedBy      NVARCHAR(200),
    @Remarks        NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldExpiryDate DATE;
    SELECT @OldExpiryDate = ValidityDate FROM dbo.WN_Bookings WHERE Id = @BookingId;

    IF @OldExpiryDate IS NULL
    BEGIN
        RAISERROR('Booking not found.', 16, 1);
        RETURN;
    END

    IF @NewExpiryDate <= @OldExpiryDate
    BEGIN
        RAISERROR('New expiry date must be after the current expiry date.', 16, 1);
        RETURN;
    END

    UPDATE dbo.WN_Bookings
    SET ValidityDate = @NewExpiryDate,
        UpdatedOn    = GETUTCDATE()
    WHERE Id = @BookingId;

    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'WN_Payments' AND COLUMN_NAME = 'ExpiresOn'
    )
    BEGIN
        UPDATE dbo.WN_Payments
        SET ExpiresOn = @NewExpiryDate
        WHERE BookingIdInt = @BookingId;
    END

    IF OBJECT_ID('dbo.WN_ChallanValidityLog', 'U') IS NOT NULL
    BEGIN
        INSERT INTO dbo.WN_ChallanValidityLog
            (BookingId, OldExpiryDate, NewExpiryDate, UpdatedBy, UpdatedAt, Remarks)
        VALUES
            (@BookingId, @OldExpiryDate, @NewExpiryDate, @UpdatedBy, GETUTCDATE(), @Remarks);
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challan_Search]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Challan_Search]
    @Query NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        b.Id                                            AS bookingId,
        CAST(b.IdGUID AS NVARCHAR(36))                  AS bookingGuid,
        b.ChallanNumber                                 AS challanNumber,
        b.ValidityDate                                  AS currentExpiryDate,
        b.BookingStatusId                               AS bookingStatus,
        p.TransactionRef                                AS voucherNumber,
        p.StatusId                                      AS voucherStatus,
        u.Name                                          AS customerName,
        s.Name                                          AS spaceName,
        b.StartOn                                       AS startDateTime,
        b.EndOn                                         AS endDateTime,
        b.TotalAmount                                   AS totalAmount
    FROM dbo.WN_Bookings b
    LEFT JOIN dbo.WN_Users u    ON u.Id = b.UserId
    LEFT JOIN dbo.WN_Spaces s   ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Payments p ON p.BookingIdInt = b.Id
    WHERE
        CAST(b.Id AS NVARCHAR(20)) = @Query
        OR b.ChallanNumber         = @Query
        OR p.TransactionRef        = @Query
        OR b.ChallanNumber LIKE '%' + @Query + '%'
    ORDER BY b.Id DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_ExtendValidity]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Challans_ExtendValidity]
    @ChallanId     INT,
    @NewValidUntil DATE,
    @UpdatedById   INT,
    @Remarks       NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OldValidUntil DATE;
    SELECT @OldValidUntil = ValidUntil FROM dbo.WN_Challans WHERE Id = @ChallanId;

    IF @OldValidUntil IS NULL
    BEGIN RAISERROR('Challan not found.', 16, 1); RETURN; END
    IF @NewValidUntil <= @OldValidUntil
    BEGIN RAISERROR('New validity date must be after current validity date.', 16, 1); RETURN; END

    UPDATE dbo.WN_Challans SET ValidUntil = @NewValidUntil WHERE Id = @ChallanId;

    INSERT INTO dbo.WN_ChallanAuditLog
        (ChallanId, OldValidUntil, NewValidUntil, UpdatedById, Remarks)
    VALUES
        (@ChallanId, @OldValidUntil, @NewValidUntil, @UpdatedById, @Remarks);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_GetByBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 9: CHALLANS
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_Challans_GetByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.Id, c.PublicId, c.BookingId, c.ChallanNumber,
        c.IssuedOn, c.ValidUntil, c.StatusId, c.Notes, c.CreatedOn
    FROM dbo.WN_Challans c WITH (NOLOCK)
    WHERE c.BookingId = @BookingId
    ORDER BY c.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_GetFullByBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Challans_GetFullByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.Id              AS ChallanId,
        c.PublicId        AS ChallanPublicId,
        c.ChallanNumber,
        c.IssuedOn,
        c.ValidUntil,
        c.StatusId        AS ChallanStatusId,
        c.Notes           AS ChallanNotes,
        v.BookingId,
        v.BookingPublicId,
        v.StartOn,
        v.EndOn,
        v.ContractStartDate,
        v.ContractEndDate,
        v.BookingStatusCode,
        v.BookingStatusLabel,
        v.UserName        AS CustomerName,
        v.UserEmail       AS CustomerEmail,
        v.SpaceCode,
        v.SpaceNumber,
        v.SpaceName,
        v.SpaceCapacity,
        v.SpaceTypeName,
        v.LocationName,
        v.BranchName,
        v.CompanyName,
        v.BillingPeriodCode,
        v.BillingPeriodLabel,
        v.BillingPeriod,
        v.BillingPeriodMonths,
        v.NumberOfMonths,
        v.ContractDuration,
        v.MonthlyRent,
        v.CurrentCycleAmount,
        v.TotalContractAmount,
        v.TotalPaidAmount,
        v.BalanceLeft,
        v.NextBillDueDate,
        v.NextBillingDate,
        v.SeatPrice,
        v.RoomPrice,
        v.SecurityDeposit,
        v.BookedOn
    FROM dbo.WN_Challans          c  WITH (NOLOCK)
    JOIN dbo.WN_vw_BookingSummary v  ON v.BookingId = c.BookingId
    WHERE c.BookingId = @BookingId
    ORDER BY c.CreatedOn DESC;

    SELECT
        bl.Id             AS LineId,
        bl.BookingId,
        ct.Code           AS ChargeTypeCode,
        ct.Label          AS ChargeTypeLabel,
        bl.Description,
        bl.Quantity,
        bl.UnitPrice,
        bl.DiscountAmount,
        bl.TaxRate,
        bl.TaxAmount,
        bl.LineTotal,
        bl.AccountId,
        a.Description     AS AccountName
    FROM dbo.WN_BookingLines bl WITH (NOLOCK)
    JOIN dbo.WN_ChargeTypes  ct WITH (NOLOCK) ON ct.Id = bl.ChargeTypeId
    LEFT JOIN dbo.AccountsCOA a WITH (NOLOCK) ON a.Id  = bl.AccountId
    WHERE bl.BookingId = @BookingId
      AND bl.IsDeleted = 0
    ORDER BY bl.Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Challans_Search]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Challans_Search]
    @Query NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        c.Id, c.PublicId, c.ChallanNumber,
        c.IssuedOn, c.ValidUntil, c.StatusId,
        v.BookingId, v.BookingPublicId,
        v.UserName, v.UserEmail,
        v.SpaceName, v.SpaceCode,
        v.StartOn, v.EndOn,
        v.RoomPrice, v.BillingPeriodCode,
        v.BookingStatusCode
    FROM dbo.WN_Challans          c  WITH (NOLOCK)
    JOIN dbo.WN_vw_BookingSummary v  ON v.BookingId = c.BookingId
    WHERE c.ChallanNumber = @Query
       OR CAST(c.BookingId AS NVARCHAR(20)) = @Query
       OR c.ChallanNumber LIKE '%' + @Query + '%'
    ORDER BY c.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Cities_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 2. WN_Cities_GetList
CREATE OR ALTER PROCEDURE [dbo].[WN_Cities_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ci.Id, CAST(NULL AS UNIQUEIDENTIFIER) AS PublicId, ci.Description AS Name, ci.CountryId,
           co.Name AS CountryName
    FROM dbo.City         ci WITH (NOLOCK)
    LEFT JOIN dbo.WN_Countries co WITH (NOLOCK) ON co.Id = ci.CountryId
    WHERE ci.Status = 1
    ORDER BY ci.Description ASC;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Companies_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Companies_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, 
           Id AS PublicId, 
           CompanyName AS Name, 
           CompanyName AS LegalName, 
           Email, 
           Contact AS Phone, 
           CASE WHEN Status = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsActive
    FROM dbo.Company WITH (NOLOCK)
    WHERE Status = 1
    ORDER BY CompanyName ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Contacts_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.WN_Contacts WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 10: CONTACTS
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_Contacts_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        c.Id, c.PublicId, c.ContactType,
        c.UserId, c.Name, c.Email, c.PhoneNumber,
        c.Message, c.StatusId, c.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Contacts c WITH (NOLOCK)
    WHERE (@Search IS NULL OR @Search = ''
           OR c.Name  LIKE '%' + @Search + '%'
           OR c.Email LIKE '%' + @Search + '%')
    ORDER BY c.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_GetRecent]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Contacts_GetRecent]
    @Top INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top)
        Id, PublicId, ContactType, Name, Email,
        PhoneNumber, Message, StatusId, CreatedOn
    FROM dbo.WN_Contacts WITH (NOLOCK)
    ORDER BY CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Contacts_Insert]
    @ContactType NVARCHAR(20)  = 'Contact',
    @UserId      INT           = NULL,
    @Name        NVARCHAR(200),
    @Email       NVARCHAR(256),
    @PhoneNumber NVARCHAR(50)  = NULL,
    @Message     NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Contacts
        (ContactType, UserId, Name, Email, PhoneNumber, Message, StatusId)
    VALUES
        (@ContactType, @UserId, @Name, @Email, @PhoneNumber, @Message, 1);
    SELECT Id, PublicId FROM dbo.WN_Contacts WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Contacts_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Contacts_UpdateStatus]
    @Id          INT,
    @StatusId    TINYINT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Contacts SET
        StatusId    = @StatusId,
        UpdatedOn   = SYSUTCDATETIME(),
        UpdatedById = @UpdatedById
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_CreateAdvanceInvoice]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 3. REFACTORED: dbo.WN_CreateAdvanceInvoice
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_CreateAdvanceInvoice]
    @BookingId              INT,
    @AdvanceRentMonths      INT,
    @SecurityDepositMonths  INT,
    @DiscountPercentage     DECIMAL(5,2)    = 0.00,
    @DiscountAmount         DECIMAL(18,2)   = 0.00,
    @TaxPercentage          DECIMAL(5,2)    = 0.00,
    @TaxAmount              DECIMAL(18,2)   = 0.00,
    @Notes                  NVARCHAR(MAX)   = NULL,
    @CreatedByAdminId       NVARCHAR(100)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY

        DECLARE @CustomerId INT, @MonthlyRent DECIMAL(18,2), @BookingStart DATETIME, @BookingEnd DATETIME;
        SELECT @CustomerId = UserId, @MonthlyRent = RoomPrice, @BookingStart = StartOn, @BookingEnd = EndOn
        FROM dbo.WN_vw_BookingSummary WHERE BookingId = @BookingId;

        IF @BookingId IS NULL OR @CustomerId IS NULL
        BEGIN
            RAISERROR('Invalid booking specified.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END

        -- Calculate amounts
        DECLARE @AdvanceRentTotal DECIMAL(18,2) = @AdvanceRentMonths * @MonthlyRent;
        DECLARE @SecurityDepositTotal DECIMAL(18,2) = @SecurityDepositMonths * @MonthlyRent;
        DECLARE @Subtotal DECIMAL(18,2) = @AdvanceRentTotal;
        DECLARE @CalcDiscount DECIMAL(18,2) = 0.00;
        IF ISNULL(@DiscountPercentage, 0) > 0
            SET @CalcDiscount = ROUND(@AdvanceRentTotal * (@DiscountPercentage / 100.0), 2);
        ELSE IF ISNULL(@DiscountAmount, 0) > 0
            SET @CalcDiscount = CASE WHEN @DiscountAmount > @AdvanceRentTotal THEN @AdvanceRentTotal ELSE @DiscountAmount END;

        DECLARE @DiscountedRent DECIMAL(18,2) = @AdvanceRentTotal - @CalcDiscount;
        DECLARE @SupportCharge DECIMAL(18,2) = ROUND(@DiscountedRent * 0.10, 2);
        DECLARE @CalcTax DECIMAL(18,2) = CASE WHEN @TaxAmount > 0 THEN @TaxAmount ELSE ROUND(@SupportCharge * (CASE WHEN ISNULL(@TaxPercentage, 0) > 0 THEN @TaxPercentage ELSE 16.00 END / 100.0), 2) END;
        DECLARE @TotalPayable DECIMAL(18,2) = @DiscountedRent + @SecurityDepositTotal + @CalcTax;

        -- Resolve sequence
        DECLARE @InvoiceNumber NVARCHAR(50);
        IF EXISTS (SELECT * FROM sys.sequences WHERE name = 'Seq_InvoiceNumber')
        BEGIN
            SET @InvoiceNumber = 'INV-ADV-' + CAST(NEXT VALUE FOR dbo.Seq_InvoiceNumber AS NVARCHAR(20));
        END
        ELSE
        BEGIN
            SET @InvoiceNumber = 'INV-ADV-' + CONVERT(NVARCHAR(8), GETDATE(), 112) + '-' + CAST(CAST(RAND()*100000 AS INT) AS NVARCHAR(10));
        END

        -- Load Account mappings
        DECLARE @RentAccountId    INT = NULL;
        DECLARE @DepositAccountId INT = NULL;

        SELECT TOP 1 
            @RentAccountId    = sp.RentAccountId,
            @DepositAccountId = sp.DepositAccountId
        FROM dbo.WN_SpacePricing sp
        JOIN dbo.WN_Bookings     b  ON b.PricingId = sp.Id
        WHERE b.Id = @BookingId;

        IF @RentAccountId IS NULL SET @RentAccountId = 2852;
        IF @DepositAccountId IS NULL AND @SecurityDepositTotal > 0 SET @DepositAccountId = 76;

        -- Call Centralized SP to insert invoice header
        DECLARE @InvoiceId INT;
        EXEC dbo.WN_Invoices_Insert
            @UserId                 = @CustomerId,
            @BookingId              = @BookingId,
            @InvoiceNumber          = @InvoiceNumber,
            @InvoiceTypeId          = 2, -- 2: Advance Invoice
            @AdvanceRentMonths      = @AdvanceRentMonths,
            @SecurityDepositMonths  = @SecurityDepositMonths,
            @SecurityDepositAmount  = @SecurityDepositTotal,
            @SubTotal               = @Subtotal,
            @DiscountTotal          = @CalcDiscount,
            @TaxTotal               = @CalcTax,
            @GrandTotal             = @TotalPayable,
            @StatusId               = NULL, -- OrderStatus 'Un Paid' (resolved in WN_Invoices_Insert)
            @Notes                  = @Notes,
            @IssuedOn               = NULL, -- Defaults to today
            @DueOn                  = NULL, -- Defaults to today + 7 days
            @SyncCustomerVendor     = 1,
            @InvoiceId              = @InvoiceId OUTPUT,
            @GeneratedInvoiceNumber = @InvoiceNumber OUTPUT;

        -- Create Line Items in WN_InvoiceLines
        DECLARE @PerMonthDisc DECIMAL(18,2) = CASE WHEN @AdvanceRentMonths > 0 THEN ROUND(@CalcDiscount / @AdvanceRentMonths, 2) ELSE 0 END;
        DECLARE @i INT = 1;
        WHILE @i <= @AdvanceRentMonths
        BEGIN
            DECLARE @MonthStart DATETIME = DATEADD(month, @i - 1, @BookingStart);
            DECLARE @MonthLabel NVARCHAR(50) = DATENAME(month, @MonthStart) + ' ' + CAST(YEAR(@MonthStart) AS NVARCHAR(4));
            DECLARE @MonthLineDisc DECIMAL(18,2) = CASE WHEN @i = @AdvanceRentMonths THEN @CalcDiscount - (@PerMonthDisc * (@AdvanceRentMonths - 1)) ELSE @PerMonthDisc END;

            INSERT INTO dbo.WN_InvoiceLines (InvoiceId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, SortOrder)
            VALUES (@InvoiceId, 1, 'Advance Rent - ' + @MonthLabel, 1, @MonthlyRent, @MonthLineDisc, 0, @RentAccountId, @i);

            -- Insert into Prepaid Periods tracking as Pending
            INSERT INTO dbo.WN_InvoicePrepaidPeriods (InvoiceId, BookingId, PeriodStartDate, PeriodEndDate, MonthName, MonthlyRentAmount, Status)
            VALUES (@InvoiceId, @BookingId, @MonthStart, DATEADD(day, -1, DATEADD(month, 1, @MonthStart)), @MonthLabel, @MonthlyRent, 'Pending');

            SET @i = @i + 1;
        END

        IF @SecurityDepositMonths > 0
        BEGIN
            INSERT INTO dbo.WN_InvoiceLines (InvoiceId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, SortOrder)
            VALUES (@InvoiceId, 2, 'Security Deposit - ' + CAST(@SecurityDepositMonths AS NVARCHAR(5)) + ' Month(s)', @SecurityDepositMonths, @MonthlyRent, 0, 0, @DepositAccountId, @AdvanceRentMonths + 1);
        END

        COMMIT TRANSACTION;

        SELECT @InvoiceId AS InvoiceId, @InvoiceNumber AS InvoiceNumber, @TotalPayable AS TotalAmount;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_CreateBookingWithAutoAssignment]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ------------------------------------------------------------
-- 4.7  WN_CreateBookingWithAutoAssignment
-- Removed reading s.CompanyId/s.BranchId into local vars.
-- Removed CompanyId/BranchId from WN_Bookings INSERT.
-- ------------------------------------------------------------
CREATE OR ALTER PROCEDURE [dbo].[WN_CreateBookingWithAutoAssignment]
    @Email             NVARCHAR(255),
    @SpaceType         NVARCHAR(100),
    @StartDateTime     DATETIME,
    @EndDateTime       DATETIME,
    @Notes             NVARCHAR(MAX)    = '',
    @TotalAmount       DECIMAL(10,2)    = 0,
    @PaymentMethod     NVARCHAR(50)     = NULL,
    @PaymentRef        NVARCHAR(100)    = NULL,
    @BookingId         INT              OUTPUT,
    @BookingGuid       UNIQUEIDENTIFIER OUTPUT,
    @AssignedSpaceId   INT              OUTPUT,
    @AssignedSpaceName NVARCHAR(255)    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @UserId      INT;
        DECLARE @SpaceId     INT;
        DECLARE @SpaceTypeId INT;

        SELECT @UserId = Id
        FROM dbo.WN_Users WITH (NOLOCK)
        WHERE Email = @Email AND (IsActive = 1 OR Status = 1);

        IF @UserId IS NULL
        BEGIN
            RAISERROR('User not found or inactive', 16, 1); RETURN;
        END

        SELECT @SpaceTypeId = Id
        FROM dbo.WN_SpaceTypes WITH (NOLOCK)
        WHERE (Name = @SpaceType OR Description = @SpaceType) AND (IsActive = 1 OR Status = 1);

        IF @SpaceTypeId IS NULL
        BEGIN
            RAISERROR('Invalid space type', 16, 1); RETURN;
        END

        SELECT TOP 1
            @SpaceId           = Id,
            @AssignedSpaceName = Name
        FROM (
            SELECT
                s.Id, s.Name, s.Code,
                CASE
                    WHEN @SpaceType LIKE '%Private%' AND s.Code LIKE '30%' THEN 1
                    WHEN @SpaceType LIKE '%Shared%'  AND s.Code LIKE '31%' THEN 1
                    WHEN @SpaceType LIKE '%Meeting%' AND s.Code LIKE '32%' THEN 1
                    ELSE 2
                END AS Priority,
                CASE
                    WHEN ISNUMERIC(SUBSTRING(s.Code, 3, LEN(s.Code)-2)) = 1
                    THEN CAST(SUBSTRING(s.Code, 3, LEN(s.Code)-2) AS INT)
                    ELSE 999
                END AS CodeNumber
            FROM dbo.WN_Spaces s WITH (UPDLOCK)
            WHERE s.SpaceTypeId = @SpaceTypeId
              AND s.Status = 1
              AND s.Id NOT IN (
                    SELECT DISTINCT b.SpaceId
                    FROM dbo.WN_Bookings b WITH (NOLOCK)
                    WHERE b.BookingStatusId IN (1, 4)
                      AND (
                            (@StartDateTime >= b.StartOn AND @StartDateTime <  b.EndOn) OR
                            (@EndDateTime   >  b.StartOn AND @EndDateTime   <= b.EndOn) OR
                            (@StartDateTime <= b.StartOn AND @EndDateTime   >= b.EndOn)
                          )
                )
        ) AS AvailableSpaces
        ORDER BY Priority ASC, CodeNumber ASC;

        IF @SpaceId IS NULL
        BEGIN
            RAISERROR('No available spaces for the requested time period', 16, 1); RETURN;
        END

        SET @BookingGuid = NEWID();

        INSERT INTO dbo.WN_Bookings (
            IdGUID, BookingDate, UserId, SpaceId,
            StartOn, EndOn, Notes, TotalAmount,
            BookingStatusId, CreatedOn, CreatedById
        ) VALUES (
            @BookingGuid, GETDATE(), @UserId, @SpaceId,
            @StartDateTime, @EndDateTime, @Notes, @TotalAmount,
            1, GETDATE(), @UserId
        );

        SET @BookingId       = SCOPE_IDENTITY();
        SET @AssignedSpaceId = @SpaceId;

        IF @PaymentMethod IS NOT NULL AND @TotalAmount > 0
        BEGIN
            INSERT INTO dbo.WN_Payments (
                IdGUID, UserIdInt, BookingIdInt, Amount, CurrencyCode,
                TransactionRef, StatusId, CreatedOn
            ) VALUES (
                NEWID(), @UserId, @BookingId, @TotalAmount, N'PKR',
                @PaymentRef, 1, GETDATE()
            );
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        DECLARE @ErrorMessage  NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT            = ERROR_SEVERITY();
        DECLARE @ErrorState    INT            = ERROR_STATE();
        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_CreateCustomInvoice]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 2. Centralized dbo.WN_CreateCustomInvoice
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_CreateCustomInvoice]
    @UserId                 INT,
    @IssuedOn               DATE,
    @DueOn                  DATE,
    @CurrencyCode           NVARCHAR(10)        = 'PKR',
    @Notes                  NVARCHAR(MAX)       = NULL,
    @SubTotal               DECIMAL(18,4),
    @DiscountTotal          DECIMAL(18,4),
    @TaxTotal               DECIMAL(18,4),
    @BookingId              INT                 = NULL,
    @GrandTotal             DECIMAL(18,4),
    @AdvanceRentMonths      INT                 = NULL,
    @SecurityDepositMonths  INT                 = NULL,
    @SecurityDepositAmount  DECIMAL(18,2)       = 0.00,
    @BillingPeriodStart     DATETIME            = NULL,
    @BillingPeriodEnd       DATETIME            = NULL,
    @AccountReceivableId    INT                 = NULL,
    @RentAccountId          INT                 = NULL,
    @ServicesIncomeId       INT                 = NULL,
    @SalesTaxId             INT                 = NULL,
    @SecurityReceivedId     INT                 = NULL,
    @AccountsCoaId          INT                 = NULL,
    @RoomRentExclTax        DECIMAL(18,4)       = 0.0000,
    @ServiceCharges         DECIMAL(18,4)       = 0.0000,
    @TaxOnServiceCharges    DECIMAL(18,4)       = 0.0000,
    @InvoiceId              INT OUTPUT,
    @InvoiceNumber          NVARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        EXEC dbo.WN_Invoices_Insert
            @UserId                 = @UserId,
            @BookingId              = @BookingId,
            @IssuedOn               = @IssuedOn,
            @DueOn                  = @DueOn,
            @CurrencyCode           = @CurrencyCode,
            @Notes                  = @Notes,
            @SubTotal               = @SubTotal,
            @DiscountTotal          = @DiscountTotal,
            @TaxTotal               = @TaxTotal,
            @GrandTotal             = @GrandTotal,
            @InvoiceTypeId          = 1,
            @AdvanceRentMonths      = @AdvanceRentMonths,
            @SecurityDepositMonths  = @SecurityDepositMonths,
            @SecurityDepositAmount  = @SecurityDepositAmount,
            @BillingPeriodStart     = @BillingPeriodStart,
            @BillingPeriodEnd       = @BillingPeriodEnd,
            @AccountReceivableId    = @AccountReceivableId,
            @RentAccountId          = @RentAccountId,
            @ServicesIncomeId       = @ServicesIncomeId,
            @SalesTaxId             = @SalesTaxId,
            @SecurityReceivedId     = @SecurityReceivedId,
            @AccountsCoaId          = @AccountsCoaId,
            @RoomRentExclTax        = @RoomRentExclTax,
            @ServiceCharges         = @ServiceCharges,
            @TaxOnServiceCharges    = @TaxOnServiceCharges,
            @SyncCustomerVendor     = 1,
            @InvoiceId              = @InvoiceId OUTPUT,
            @GeneratedInvoiceNumber = @InvoiceNumber OUTPUT;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_CreateSurchargeInvoice]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 4. REFACTORED: dbo.WN_CreateSurchargeInvoice
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_CreateSurchargeInvoice]
    @BookingDetailId INT,
    @PersonId INT,
    @CustomerId INT,
    @SurchargeAmount DECIMAL(18,2),
    @ExcessSeatCount INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- Fetch Booking info
        DECLARE @BookingId INT, @UserId INT, @BookingGuid UNIQUEIDENTIFIER, @CustomerCode NVARCHAR(50);
        SELECT TOP 1
            @BookingGuid = bd.BookingGuid,
            @CustomerCode = bd.CustomerCode,
            @BookingId = b.Id,
            @UserId = b.UserId
        FROM [dbo].[WN_BookingDetails] bd WITH (NOLOCK)
        LEFT JOIN [dbo].[WN_Bookings] b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
        WHERE bd.Id = @BookingDetailId;

        -- Fallback for UserId if not directly linked via booking
        IF @UserId IS NULL
        BEGIN
            SELECT TOP 1 @UserId = UserId FROM [dbo].[WN_Customers] WITH (NOLOCK) WHERE Id = @CustomerId;
        END

        -- Fetch Attendant info
        DECLARE @AssignedFrom DATETIME, @AssignedTo DATETIME, @SurchargeApplied DECIMAL(18,2);
        SELECT TOP 1
            @AssignedFrom = AssignedFrom,
            @AssignedTo = AssignedTo,
            @SurchargeApplied = SurchargeApplied
        FROM [dbo].[WN_BookingAttendants] WITH (NOLOCK)
        WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId;

        -- Determine cycle boundaries
        DECLARE @CycleEnd DATETIME = DATEADD(month, 1, @AssignedFrom);
        DECLARE @SurchargeMonths INT = 1;
        IF @AssignedTo IS NOT NULL AND @AssignedTo > @AssignedFrom
        BEGIN
            SET @SurchargeMonths = CEILING(DATEDIFF(day, @AssignedFrom, @AssignedTo) / 30.0);
            IF @SurchargeMonths < 1 SET @SurchargeMonths = 1;
            SET @CycleEnd = @AssignedTo;
        END

        -- Calculate Financials
        DECLARE @SubTotal DECIMAL(18,2) = @SurchargeAmount * @SurchargeMonths;
        DECLARE @AppliedChargePercentage DECIMAL(5,2) = 10.00;
        DECLARE @AppliedTaxPercentage DECIMAL(5,2) = 16.00;

        SELECT TOP 1
            @AppliedChargePercentage = ISNULL(AppliedChargePercentage, 10.00),
            @AppliedTaxPercentage = ISNULL(AppliedTaxPercentage, 16.00)
        FROM [dbo].[WN_BookingDetails] WITH (NOLOCK)
        WHERE Id = @BookingDetailId;

        DECLARE @SupportCharge DECIMAL(18,2) = ROUND(@SubTotal * (@AppliedChargePercentage / 100.0), 2);
        DECLARE @TaxAmount DECIMAL(18,2) = ROUND(@SupportCharge * (@AppliedTaxPercentage / 100.0), 2);
        DECLARE @GrandTotal DECIMAL(18,2) = @SubTotal + @TaxAmount;

        -- Fetch Person info
        DECLARE @PersonName NVARCHAR(200), @PersonIdNumber NVARCHAR(50);
        SELECT TOP 1
            @PersonName = Name,
            @PersonIdNumber = IdNumber
        FROM [dbo].[WN_Persons] WITH (NOLOCK)
        WHERE PersonId = @PersonId;

        -- Generate Invoice Number
        DECLARE @NextId INT;
        SELECT @NextId = ISNULL(MAX(Id), 0) + 1 FROM [dbo].[WN_Invoices];
        DECLARE @InvoiceNumber NVARCHAR(50) = 'INV-SUR-' + CAST(YEAR(GETDATE()) AS VARCHAR(4)) + '-' + RIGHT('00000' + CAST(@NextId AS VARCHAR(10)), 5);
        DECLARE @Notes NVARCHAR(MAX) = 'Attendant Capacity Overage Surcharge (Current Cycle: ' + CAST(@SurchargeMonths AS VARCHAR(10)) + ' month(s)) - ' + ISNULL(@PersonName, '') + ' (' + ISNULL(@PersonIdNumber, '') + ')';

        -- Call Centralized SP to insert invoice header
        DECLARE @InvoiceId INT;
        EXEC dbo.WN_Invoices_Insert
            @UserId                 = @UserId,
            @BookingId              = NULL,
            @InvoiceNumber          = @InvoiceNumber,
            @IssuedOn               = NULL,
            @DueOn                  = NULL,
            @SubTotal               = @SubTotal,
            @DiscountTotal          = 0.00,
            @TaxTotal               = @TaxAmount,
            @GrandTotal             = @GrandTotal,
            @PaidTotal              = 0.00,
            @CurrencyCode           = 'PKR',
            @StatusId               = NULL, -- OrderStatus 'Un Paid' (resolved in WN_Invoices_Insert)
            @Notes                  = @Notes,
            @InvoiceTypeId          = 1, -- Custom/Surcharge
            @SecurityDepositAmount  = 0.00,
            @BillingPeriodStart     = @AssignedFrom,
            @BillingPeriodEnd       = @CycleEnd,
            @SyncCustomerVendor     = 1,
            @InvoiceId              = @InvoiceId OUTPUT,
            @GeneratedInvoiceNumber = @InvoiceNumber OUTPUT;

        -- Insert line item into WN_InvoiceLines
        INSERT INTO [dbo].[WN_InvoiceLines] (
            InvoiceId,
            ChargeTypeId,
            Description,
            Quantity,
            UnitPrice,
            DiscountAmount,
            TaxRate,
            SortOrder
        )
        VALUES (
            @InvoiceId,
            1, -- Rent / Surcharge Charge Type
            'Capacity Overage Surcharge (' + CAST(@ExcessSeatCount AS VARCHAR(10)) + ' excess seat(s)) - ' + ISNULL(@PersonName, 'Attendant'),
            @ExcessSeatCount,
            @SurchargeAmount * @SurchargeMonths / CASE WHEN @ExcessSeatCount > 0 THEN @ExcessSeatCount ELSE 1 END,
            0.00,
            CASE WHEN @SubTotal > 0 THEN CAST(ROUND(@TaxAmount / @SubTotal, 4) AS DECIMAL(6,4)) ELSE 0 END,
            1
        );

        COMMIT TRANSACTION;

        SELECT 
            @InvoiceId AS InvoiceId,
            @InvoiceNumber AS InvoiceNumber,
            @GrandTotal AS GrandTotal,
            @SubTotal AS SubTotal,
            @TaxAmount AS TaxAmount,
            'Success' AS Status;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_Delete]
    @IdGUID UNIQUEIDENTIFIER = NULL,
    @Id     INT              = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    UPDATE dbo.WN_Customers
    SET IsActive = 0, UpdatedAt = GETDATE()
    WHERE (@IdGUID IS NOT NULL AND IdGUID = @IdGUID)
       OR (@Id IS NOT NULL AND Id = @Id);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetByEmail]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetByEmail]
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, IdGUID, Code, FirstName, LastName, Company, Email, PhoneNumber, CnicOrPassport, Address, CityId, IsActive, CreatedAt, Notes, UserId
    FROM dbo.WN_Customers WITH (NOLOCK)
    WHERE Email = @Email;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetByGuid]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetByGuid]
    @IdGUID UNIQUEIDENTIFIER = NULL,
    @Id     INT              = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName, c.Company,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Description AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.City ci ON ci.Id = c.CityId
    WHERE (@IdGUID IS NOT NULL AND c.IdGUID = @IdGUID)
       OR (@Id IS NOT NULL AND c.Id = @Id);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 5. WN_Customers_GetById
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetById]
    @IdGUID UNIQUEIDENTIFIER = NULL,
    @Id     INT              = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName, c.Company,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Description AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.City ci ON ci.Id = c.CityId
    WHERE (@IdGUID IS NOT NULL AND c.IdGUID = @IdGUID)
       OR (@Id IS NOT NULL AND c.Id = @Id);
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetByUserId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, IdGUID, Code, FirstName, LastName, Company, Email, PhoneNumber, CnicOrPassport, Address, CityId, IsActive, CreatedAt, Notes, UserId
    FROM dbo.WN_Customers WITH (NOLOCK)
    WHERE UserId = @UserId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_GetList]
    @Search NVARCHAR(255) = NULL,
    @Page   INT = 1,
    @Limit  INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName, c.Company,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, NULL AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    WHERE (@Search IS NULL OR
           c.FirstName    LIKE '%' + @Search + '%' OR
           c.LastName     LIKE '%' + @Search + '%' OR
           c.Company      LIKE '%' + @Search + '%' OR
           c.Email        LIKE '%' + @Search + '%' OR
           c.Code         LIKE '%' + @Search + '%' OR
           c.PhoneNumber  LIKE '%' + @Search + '%')
    ORDER BY c.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 7. WN_Customers_Insert
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_Insert]
    @FirstName      NVARCHAR(100),
    @LastName       NVARCHAR(100)  = NULL,
    @Email          NVARCHAR(255),
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CityId         INT            = NULL,
    @Notes          NVARCHAR(1000) = NULL,
    @CreatedBy      NVARCHAR(255)  = NULL,
    @UserId         INT            = NULL,
    @Company        NVARCHAR(255)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;

    INSERT INTO dbo.WN_Customers
        (FirstName, LastName, Company, Email, PhoneNumber, CnicOrPassport, Address, CityId, Notes, CreatedBy, UserId)
    VALUES
        (@FirstName, @LastName, @Company, @Email, @PhoneNumber, @CnicOrPassport, @Address, @CityId, @Notes, @CreatedBy, @UserId);

    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName, c.Company,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Description AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.City ci ON ci.Id = c.CityId
    WHERE c.Id = SCOPE_IDENTITY();
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Search]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 8. WN_Customers_Search
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_Search]
    @Query NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 20
           c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName, c.Company,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Description AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.City ci ON ci.Id = c.CityId
    WHERE c.IsActive = 1 AND (
           c.FirstName   LIKE '%' + @Query + '%' OR
           c.LastName    LIKE '%' + @Query + '%' OR
           c.Company     LIKE '%' + @Query + '%' OR
           c.Email       LIKE '%' + @Query + '%' OR
           c.Code        LIKE '%' + @Query + '%' OR
           c.PhoneNumber LIKE '%' + @Query + '%')
    ORDER BY c.FirstName;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Customers_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 9. WN_Customers_Update
CREATE OR ALTER PROCEDURE [dbo].[WN_Customers_Update]
    @IdGUID         UNIQUEIDENTIFIER = NULL,
    @Id             INT              = NULL,
    @FirstName      NVARCHAR(100)  = NULL,
    @LastName       NVARCHAR(100)  = NULL,
    @Email          NVARCHAR(255)  = NULL,
    @PhoneNumber    NVARCHAR(50)   = NULL,
    @CnicOrPassport NVARCHAR(50)   = NULL,
    @Address        NVARCHAR(500)  = NULL,
    @CityId         INT            = NULL,
    @Notes          NVARCHAR(1000) = NULL,
    @IsActive       BIT            = NULL,
    @Company        NVARCHAR(255)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;

    UPDATE dbo.WN_Customers SET
        FirstName      = ISNULL(@FirstName,      FirstName),
        LastName       = ISNULL(@LastName,        LastName),
        Company        = ISNULL(@Company,         Company),
        Email          = ISNULL(@Email,           Email),
        PhoneNumber    = ISNULL(@PhoneNumber,     PhoneNumber),
        CnicOrPassport = ISNULL(@CnicOrPassport,  CnicOrPassport),
        Address        = ISNULL(@Address,         Address),
        CityId         = ISNULL(@CityId,          CityId),
        Notes          = ISNULL(@Notes,           Notes),
        IsActive       = ISNULL(@IsActive,        IsActive),
        UpdatedAt      = GETDATE()
    WHERE (@IdGUID IS NOT NULL AND IdGUID = @IdGUID)
       OR (@Id IS NOT NULL AND Id = @Id);

    SELECT c.Id, c.IdGUID, c.Code, c.FirstName, c.LastName, c.Company,
           LTRIM(RTRIM(ISNULL(c.FirstName,'') + ' ' + ISNULL(c.LastName,''))) AS FullName,
           c.Email, c.PhoneNumber, c.CnicOrPassport, c.Address,
           c.CityId, ci.Description AS CityName, c.IsActive, c.Notes, c.CreatedAt
    FROM dbo.WN_Customers c
    LEFT JOIN dbo.City ci ON ci.Id = c.CityId
    WHERE (@IdGUID IS NOT NULL AND c.IdGUID = @IdGUID)
       OR (@Id IS NOT NULL AND c.Id = @Id);
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_CustomersVendors_SyncOnInvoice]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Enhanced Helper Procedure: WN_CustomersVendors_SyncOnInvoice
CREATE OR ALTER PROCEDURE [dbo].[WN_CustomersVendors_SyncOnInvoice]
    @CustomerId  INT = NULL,
    @UserId      INT = NULL,
    @BookingId   INT = NULL,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TargetEmail NVARCHAR(255) = NULL;

    IF @UserId IS NOT NULL AND @UserId > 0
    BEGIN
        SELECT TOP 1 @TargetEmail = Email FROM dbo.WN_Users WITH (NOLOCK) WHERE Id = @UserId;
    END

    -- Resolve Customer details from WN_Customers with multi-attribute fallback
    DECLARE @CustUserId INT = NULL;
    DECLARE @CustomerCode NVARCHAR(50) = NULL;
    DECLARE @FirstName NVARCHAR(100) = NULL;
    DECLARE @LastName NVARCHAR(100) = NULL;
    DECLARE @Company NVARCHAR(255) = NULL;
    DECLARE @Email NVARCHAR(255) = NULL;
    DECLARE @PhoneNumber NVARCHAR(50) = NULL;
    DECLARE @CnicOrPassport NVARCHAR(50) = NULL;
    DECLARE @Address NVARCHAR(500) = NULL;
    DECLARE @CustCityId INT = NULL;

    SELECT TOP 1
        @CustUserId     = c.UserId,
        @CustomerCode   = c.Code,
        @FirstName      = c.FirstName,
        @LastName       = c.LastName,
        @Company        = c.Company,
        @Email          = c.Email,
        @PhoneNumber    = c.PhoneNumber,
        @CnicOrPassport = c.CnicOrPassport,
        @Address        = c.Address,
        @CustCityId     = c.CityId
    FROM dbo.WN_Customers c WITH (NOLOCK)
    WHERE c.Id = @CustomerId 
       OR (c.UserId IS NOT NULL AND c.UserId = @UserId)
       OR (@TargetEmail IS NOT NULL AND c.Email = @TargetEmail)
       OR (c.Code IS NOT NULL AND c.Code = (SELECT TOP 1 CustomerCode FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @BookingId));

    IF @CustomerId IS NULL AND @CustUserId IS NOT NULL
        SET @CustomerId = @CustUserId;

    -- Fallback missing fields from WN_Users
    IF @UserId IS NOT NULL AND @UserId > 0
    BEGIN
        SELECT TOP 1
            @Email          = ISNULL(NULLIF(@Email, ''), u.Email),
            @FirstName      = CASE WHEN @FirstName IS NULL OR @FirstName = '' THEN u.Name ELSE @FirstName END,
            @Address        = ISNULL(NULLIF(@Address, ''), u.Address),
            @CnicOrPassport = ISNULL(NULLIF(@CnicOrPassport, ''), u.CnicOrPassport),
            @CustCityId     = ISNULL(@CustCityId, u.CityId)
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.Id = @UserId;
    END

    -- Fallback missing fields from WN_BookingDetails & WN_Bookings
    IF @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1
            @CustomerCode   = ISNULL(NULLIF(@CustomerCode, ''), bd.CustomerCode),
            @Email          = ISNULL(NULLIF(@Email, ''), bd.CustomerEmail),
            @FirstName      = CASE WHEN @FirstName IS NULL OR @FirstName = '' THEN bd.CustomerName ELSE @FirstName END
        FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
        JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
        WHERE b.Id = @BookingId;

        IF @CustomerCode IS NULL OR @CustomerCode = ''
        BEGIN
            SELECT TOP 1 @CustomerCode = CustomerCode
            FROM dbo.WN_Bookings WITH (NOLOCK)
            WHERE Id = @BookingId;
        END
    END

    -- Fallback Company Name from dbo.Company if WN_Customers.Company is missing
    IF (@Company IS NULL OR LTRIM(RTRIM(@Company)) = '') AND @UserId IS NOT NULL
    BEGIN
        SELECT TOP 1 @Company = comp.CompanyName
        FROM dbo.WN_Users u WITH (NOLOCK)
        JOIN dbo.Company comp WITH (NOLOCK) ON comp.Id = u.CompanyId
        WHERE u.Id = @UserId AND comp.CompanyName NOT LIKE '%WorkNest%';
    END

    -- Exit if no email or customer code available
    IF (@Email IS NULL OR LTRIM(RTRIM(@Email)) = '') AND (@CustomerCode IS NULL OR LTRIM(RTRIM(@CustomerCode)) = '')
        RETURN;

    -- Duplicate Check: If matching customer already exists in dbo.CustomersVendors, skip insertion
    IF EXISTS (
        SELECT 1 
        FROM dbo.CustomersVendors WITH (UPDLOCK, HOLDLOCK)
        WHERE (Email IS NOT NULL AND Email <> '' AND LOWER(RTRIM(LTRIM(Email))) = LOWER(RTRIM(LTRIM(@Email))))
           OR (Code IS NOT NULL AND Code <> '' AND Code = @CustomerCode)
    )
    BEGIN
        RETURN;
    END

    -- Resolve Name Rule: When WN_Customers Company is not NULL/empty, Name MUST be the Company name
    DECLARE @TargetName NVARCHAR(250) = NULL;
    IF @Company IS NOT NULL AND LTRIM(RTRIM(@Company)) <> ''
        SET @TargetName = LTRIM(RTRIM(@Company));
    ELSE
        SET @TargetName = LTRIM(RTRIM(ISNULL(@FirstName, '') + ' ' + ISNULL(@LastName, '')));

    IF @TargetName IS NULL OR @TargetName = ''
        SET @TargetName = 'Valued Customer';

    -- Parse CNIC vs Passport
    DECLARE @CleanCnicPass NVARCHAR(50) = REPLACE(REPLACE(REPLACE(ISNULL(@CnicOrPassport, ''), '-', ''), ' ', ''), '/', '');
    DECLARE @CNIC NVARCHAR(50) = NULL;
    DECLARE @Passport NVARCHAR(50) = NULL;

    IF @CleanCnicPass <> ''
    BEGIN
        IF LEN(@CleanCnicPass) = 13 AND ISNUMERIC(@CleanCnicPass) = 1
            SET @CNIC = @CnicOrPassport;
        ELSE
            SET @Passport = @CnicOrPassport;
    END

    -- Normalize Phone to Pakistani international format (+923xxxxxxxxx)
    -- Handles raw, +9203..., 00923..., 923..., 03..., 3... formats cleanly
    DECLARE @Phone NVARCHAR(50) = NULL;
    IF @PhoneNumber IS NOT NULL AND LTRIM(RTRIM(@PhoneNumber)) <> ''
    BEGIN
        DECLARE @CleanPhone NVARCHAR(50) = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@PhoneNumber)), ' ', ''), '-', ''), '(', ''), ')', ''), '+', '');
        IF @CleanPhone LIKE '9203%'
            SET @Phone = '+92' + SUBSTRING(@CleanPhone, 4, LEN(@CleanPhone) - 3);
        ELSE IF @CleanPhone LIKE '00923%'
            SET @Phone = '+' + RIGHT(@CleanPhone, LEN(@CleanPhone) - 2);
        ELSE IF @CleanPhone LIKE '923%'
            SET @Phone = '+' + @CleanPhone;
        ELSE IF @CleanPhone LIKE '03%'
            SET @Phone = '+92' + SUBSTRING(@CleanPhone, 2, LEN(@CleanPhone) - 1);
        ELSE IF @CleanPhone LIKE '3%' AND LEN(@CleanPhone) = 10
            SET @Phone = '+92' + @CleanPhone;
        ELSE
            SET @Phone = LTRIM(RTRIM(@PhoneNumber));
    END

    -- Location / Company / Branch / Province / City Resolution
    DECLARE @CompanyId INT = NULL;
    DECLARE @BranchId INT = NULL;
    DECLARE @ProvinceId INT = NULL;
    DECLARE @LocationCityId INT = NULL;

    IF @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1
            @CompanyId  = l.CompanyId,
            @BranchId   = l.BranchId,
            @ProvinceId = l.ProvinceId,
            @LocationCityId = l.CityId
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
        WHERE b.Id = @BookingId;
    END

    IF @CompanyId IS NULL
        SET @CompanyId = 486;

    DECLARE @ResolvedCityId INT = ISNULL(@CustCityId, @LocationCityId);
    DECLARE @CountryId INT = NULL;

    IF @ResolvedCityId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CountryId = CountryId
        FROM dbo.City WITH (NOLOCK)
        WHERE Id = @ResolvedCityId AND CountryId IS NOT NULL;

        IF @CountryId IS NULL
            SET @CountryId = 168; -- Pakistan SAC400 CountryId default
    END
    ELSE
    BEGIN
        SET @CountryId = 168;
    END

    -- GUID / HashGUID generation
    DECLARE @ShortGUID VARCHAR(10);

SET @ShortGUID = CONVERT(VARCHAR(50), NEWID());

SELECT @ShortGUID;

    DECLARE @HashGUID VARBINARY(50) = CAST(NEWID() AS VARBINARY(50));

    -- Insert single record into dbo.CustomersVendors
    INSERT INTO dbo.CustomersVendors (
        Name, Code, Type, AddressLine1,
        CNIC, Passport, CityId, CountryId, Phone, Email,
        CompanyId, CreatedById, CreatedTime, ShortGUID, HashGUID,
        BranchId, ProvinceId
    )
    VALUES (
        @TargetName, @CustomerCode, 1, @Address,
        @CNIC, @Passport, @ResolvedCityId, @CountryId, @Phone, @Email,
        @CompanyId, @CreatedById, SYSUTCDATETIME(), @ShortGUID, @HashGUID,
        @BranchId, @ProvinceId
    );
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Dashboard_GetSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 17: DASHBOARD
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_Dashboard_GetSummary]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalSpaces      INT;
    DECLARE @OccupiedNow      INT;
    DECLARE @AvailableNow     INT;
    DECLARE @TotalUsers       INT;
    DECLARE @ActiveMembers    INT;
    DECLARE @RevenueThisMonth DECIMAL(18,4);
    DECLARE @BookingsToday    INT;

    SELECT @TotalSpaces = COUNT(*) FROM dbo.WN_Spaces WHERE IsActive = 1;

    SELECT @OccupiedNow = COUNT(DISTINCT SpaceId)
    FROM dbo.WN_Bookings
    WHERE IsDeleted       = 0
      AND BookingStatusId IN (1, 2)
      AND StartOn         <= SYSUTCDATETIME()
      AND EndOn           >= SYSUTCDATETIME();

    SET @AvailableNow = @TotalSpaces - ISNULL(@OccupiedNow, 0);

    SELECT @TotalUsers = COUNT(*) FROM dbo.WN_Users WHERE IsActive = 1;

    -- Active members: check new table first, fall back to old
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StatusId')
    BEGIN
        DECLARE @sqlM NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM dbo.WN_Memberships WHERE StatusId = 1 AND StartOn <= SYSUTCDATETIME() AND (EndOn IS NULL OR EndOn >= SYSUTCDATETIME());';
        EXEC sp_executesql @sqlM, N'@cnt INT OUTPUT', @ActiveMembers OUTPUT;
    END
    ELSE
        SELECT @ActiveMembers = COUNT(*)
        FROM dbo.WN_MemberShips_Temp
        WHERE Status   = 'Active'
          AND IsActive = 1;

    SELECT @RevenueThisMonth = ISNULL(SUM(Amount), 0)
    FROM dbo.WN_Payments
    WHERE StatusId    = 2
      AND YEAR(PaidOn)  = YEAR(SYSUTCDATETIME())
      AND MONTH(PaidOn) = MONTH(SYSUTCDATETIME());

    SELECT @BookingsToday = COUNT(*)
    FROM dbo.WN_Bookings
    WHERE IsDeleted = 0
      AND CAST(CreatedOn AS DATE) = CAST(SYSUTCDATETIME() AS DATE);

    -- Summary row
    SELECT
        @TotalSpaces      AS TotalSpaces,
        @OccupiedNow      AS OccupiedNow,
        @AvailableNow     AS AvailableNow,
        @TotalUsers       AS TotalUsers,
        @ActiveMembers    AS ActiveMembers,
        @RevenueThisMonth AS RevenueThisMonth,
        @BookingsToday    AS BookingsToday;

    -- Booking counts by status
    SELECT
        bs.Code  AS StatusCode,
        bs.Label AS StatusLabel,
        COUNT(b.Id) AS BookingCount
    FROM dbo.WN_BookingStatuses bs
    LEFT JOIN dbo.WN_Bookings   b ON b.BookingStatusId = bs.Id AND b.IsDeleted = 0
    GROUP BY bs.Id, bs.Code, bs.Label
    ORDER BY bs.Id;

    -- Recent 5 bookings
    SELECT TOP 5
        v.BookingId, v.BookingPublicId,
        v.UserName, v.UserEmail,
        v.SpaceCode, v.SpaceName,
        v.StartOn, v.EndOn,
        v.BookingStatusCode, v.BookingStatusLabel,
        v.RoomPrice, v.BookedOn
    FROM dbo.WN_vw_BookingSummary v
    ORDER BY v.BookedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_DeviceTokens_Upsert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 12. Stored Procedure: dbo.WN_DeviceTokens_Upsert
CREATE OR ALTER PROCEDURE [dbo].[WN_DeviceTokens_Upsert]
    @UserId   INT,
    @Token    NVARCHAR(500),
    @Platform NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.WN_DeviceToken WHERE UserId = @UserId AND Token = @Token)
    BEGIN
        UPDATE dbo.WN_DeviceToken
        SET Platform = @Platform,
            IsActive = 1
        WHERE UserId = @UserId AND Token = @Token;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_DeviceToken (UserId, Token, Platform, CreatedAt, IsActive)
        VALUES (@UserId, @Token, @Platform, SYSUTCDATETIME(), 1);
    END

    SELECT 1 AS Success;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_EmailOtps_GetActive]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_EmailOtps_GetActive]
    @Email   NVARCHAR(256),
    @Purpose NVARCHAR(60)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 Id, Email, Purpose, CodeHash, CreatedOn, ExpiresOn, IdentityToken
    FROM dbo.WN_EmailOtps WITH (NOLOCK)
    WHERE Email     = @Email
      AND Purpose   = @Purpose
      AND UsedOn    IS NULL
      AND ExpiresOn > SYSUTCDATETIME()
    ORDER BY CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_EmailOtps_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_EmailOtps_Insert]
    @Email         NVARCHAR(256),
    @Purpose       NVARCHAR(60),
    @CodeHash      NVARCHAR(500),
    @ExpiresOn     DATETIME2,
    @IdentityToken NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    -- Invalidate any existing unused OTPs for same email+purpose
    UPDATE dbo.WN_EmailOtps SET UsedOn = SYSUTCDATETIME()
    WHERE Email   = @Email
      AND Purpose = @Purpose
      AND UsedOn  IS NULL;

    INSERT INTO dbo.WN_EmailOtps (Email, Purpose, CodeHash, ExpiresOn, IdentityToken)
    VALUES (@Email, @Purpose, @CodeHash, @ExpiresOn, @IdentityToken);

    SELECT Id FROM dbo.WN_EmailOtps WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_EmailOtps_MarkUsed]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_EmailOtps_MarkUsed]
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_EmailOtps SET UsedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Floors_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Floors_GetList]
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT f.Id, f.LocationId, l.Name AS LocationName,
           f.Name, f.FloorNumber, f.IsActive
    FROM dbo.WN_Floors    f  WITH (NOLOCK)
    JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = f.LocationId
    WHERE f.IsActive = 1
      AND (@LocationId IS NULL OR f.LocationId = @LocationId)
    ORDER BY f.LocationId, f.FloorNumber;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Floors_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Floors_Insert]
    @LocationId  INT,
    @Name        NVARCHAR(100),
    @FloorNumber SMALLINT = 0,
    @CreatedById INT      = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Floors (LocationId, Name, FloorNumber, CreatedById)
    VALUES (@LocationId, @Name, @FloorNumber, @CreatedById);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_GalleryImages_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_GalleryImages SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_GetAll]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_GalleryImages_GetAll]
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        g.Id, g.IdGUID AS PublicId, g.LocationId, l.Name AS LocationName,
        g.SpaceId, s.Name AS SpaceName,
        g.Title, g.Description, g.ImageUrl, g.SortOrder, g.IsActive
    FROM dbo.WN_GalleryImages g  WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = g.LocationId
    LEFT JOIN dbo.WN_Spaces    s WITH (NOLOCK) ON s.Id = g.SpaceId
    WHERE g.IsActive = 1
      AND (@LocationId IS NULL OR g.LocationId = @LocationId)
    ORDER BY g.SortOrder ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_GalleryImages_GetList]
    @Page       INT = 1,
    @Limit      INT = 20,
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        g.Id, g.IdGUID AS PublicId, g.IdGUID, g.LocationId, l.Name AS LocationName,
        g.SpaceId, s.Name AS SpaceName,
        g.Title, g.Description, g.ImageUrl, g.SortOrder, g.IsActive,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_GalleryImages g  WITH (NOLOCK)
    LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = g.LocationId
    LEFT JOIN dbo.WN_Spaces    s WITH (NOLOCK) ON s.Id = g.SpaceId
    WHERE g.IsActive = 1
      AND (@LocationId IS NULL OR g.LocationId = @LocationId)
    ORDER BY g.SortOrder ASC, g.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_GalleryImages_Insert]
    @LocationId  INT           = NULL,
    @SpaceId     INT           = NULL,
    @Title       NVARCHAR(150) = NULL,
    @Description NVARCHAR(1000)= NULL,
    @ImageUrl    NVARCHAR(500),
    @SortOrder   INT           = 0,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_GalleryImages
        (LocationId, SpaceId, Title, Description, ImageUrl, SortOrder, IsActive, CreatedById)
    VALUES
        (@LocationId, @SpaceId, @Title, @Description, @ImageUrl, @SortOrder, 1, @CreatedById);
    SELECT Id, IdGUID FROM dbo.WN_GalleryImages WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GalleryImages_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_GalleryImages_Update]
    @Id          INT,
    @Title       NVARCHAR(150)  = NULL,
    @Description NVARCHAR(1000) = NULL,
    @ImageUrl    NVARCHAR(500)  = NULL,
    @SortOrder   INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_GalleryImages SET
        Title       = ISNULL(@Title,       Title),
        Description = ISNULL(@Description, Description),
        ImageUrl    = ISNULL(@ImageUrl,    ImageUrl),
        SortOrder   = ISNULL(@SortOrder,   SortOrder)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GetAvailableSpaces]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3.16 WN_GetAvailableSpaces (legacy â€” returns NULL for deprecated columns)
CREATE OR ALTER PROCEDURE [dbo].[WN_GetAvailableSpaces] 
    @SpaceType NVARCHAR(100), 
    @StartDateTime DATETIME, 
    @EndDateTime DATETIME 
AS 
BEGIN
    SET NOCOUNT ON;
    DECLARE @STId INT;
    SELECT @STId = Id FROM dbo.WN_SpaceTypes WITH(NOLOCK) WHERE (Name = @SpaceType OR Description = @SpaceType) AND (IsActive = 1 OR Status = 1);
    IF @STId IS NULL BEGIN RAISERROR('Invalid space type', 16, 1); RETURN; END

    SELECT s.Id, s.IdGUID, s.Name, s.Code, NULL AS PricePerDay, NULL AS PricePerHour,
        st.Description AS SpaceType, l.Name AS LocationName,
        CASE WHEN @SpaceType LIKE '%Private%' AND s.Code LIKE '30%' THEN 1 WHEN @SpaceType LIKE '%Shared%' AND s.Code LIKE '31%' THEN 1 WHEN @SpaceType LIKE '%Meeting%' AND s.Code LIKE '32%' THEN 1 ELSE 2 END AS Priority,
        CASE WHEN ISNUMERIC(SUBSTRING(s.Code,3,LEN(s.Code)-2))=1 THEN CAST(SUBSTRING(s.Code,3,LEN(s.Code)-2) AS INT) ELSE 999 END AS CodeNumber
    FROM dbo.WN_Spaces s WITH(NOLOCK) 
    INNER JOIN dbo.WN_SpaceTypes st WITH(NOLOCK) ON s.SpaceTypeId = st.Id 
    INNER JOIN dbo.WN_Locations l WITH(NOLOCK) ON s.LocationId = l.Id
    WHERE s.SpaceTypeId = @STId AND s.Status = 1 AND s.Id NOT IN (
        SELECT DISTINCT b.SpaceId FROM dbo.WN_Bookings b WITH(NOLOCK) 
        WHERE b.BookingStatusId IN (1,4) 
          AND ((@StartDateTime >= b.StartOn AND @StartDateTime < b.EndOn) 
            OR (@EndDateTime > b.StartOn AND @EndDateTime <= b.EndOn) 
            OR (@StartDateTime <= b.StartOn AND @EndDateTime >= b.EndOn))
    )
    ORDER BY Priority ASC, CodeNumber ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GetBookingBillingSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Procedure: dbo.WN_GetBookingBillingSummary
CREATE OR ALTER PROCEDURE [dbo].[WN_GetBookingBillingSummary]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        b.Id AS BookingId,
        ISNULL(b.SubtotalAmount, 420000.00) AS TotalContractRent,
        ISNULL((SELECT SUM(GrandTotal) FROM dbo.WN_Invoices WHERE BookingId = b.Id AND InvoiceTypeId IN (1,2)), 0.00) AS RentInvoiced,
        ISNULL((SELECT SUM(PaidTotal) FROM dbo.WN_Invoices WHERE BookingId = b.Id AND InvoiceTypeId IN (1,2)), 0.00) AS RentPaid,
        ISNULL((SELECT SUM(PaidTotal) FROM dbo.WN_Invoices WHERE BookingId = b.Id AND InvoiceTypeId = 2), 0.00) AS AdvanceRentPaid,
        ISNULL(b.SecurityDepositRequired, 70000.00) AS SecurityDepositRequired,
        ISNULL((SELECT SUM(SecurityDepositAmount) FROM dbo.WN_Invoices WHERE BookingId = b.Id), 0.00) AS SecurityDepositInvoiced,
        ISNULL(b.SecurityDepositPaid, 0.00) AS SecurityDepositPaid,
        (ISNULL(b.SecurityDepositRequired, 70000.00) - ISNULL(b.SecurityDepositPaid, 0.00)) AS SecurityDepositOutstanding
    FROM dbo.WN_Bookings b
    WHERE b.Id = @BookingId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GetCustomerSTInvoiceByPublicId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 8. PROCEDURE: dbo.WN_GetCustomerSTInvoiceByPublicId
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_GetCustomerSTInvoiceByPublicId]
    @PublicId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        st.Id AS STInvoiceId,
        st.PublicId AS STPublicId,
        st.CustomerInvoiceId,
        st.STInvoiceNumber,
        st.TariffHeading,
        st.TariffLabel,
        st.RoomRentDescription,
        st.RoomRentAmount,
        st.RoomRentTaxRate,
        st.RoomRentTaxAmount,
        st.ServiceChargeDescription,
        COALESCE(
            NULLIF(st.ServiceChargeAmount, 0),
            NULLIF(bd.SupportChargeAmount, 0),
            CASE WHEN i.TaxTotal > 0 THEN ROUND(i.TaxTotal / (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2) ELSE 0 END
        ) AS ServiceChargeAmount,
        ISNULL(NULLIF(st.ServiceChargeTaxRate, 0), ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00)) AS ServiceChargeTaxRate,
        COALESCE(
            NULLIF(st.ServiceChargeTaxAmount, 0),
            NULLIF(i.TaxTotal, 0),
            NULLIF(bd.TaxAmount, 0),
            ROUND(ISNULL(bd.SupportChargeAmount, 0) * (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2)
        ) AS ServiceChargeTaxAmount,
        st.SecurityDepositDescription,
        st.SecurityDepositAmount,
        st.SecurityDepositTaxRate,
        st.SecurityDepositTaxAmount,
        COALESCE(
            NULLIF(st.ServiceChargeAmount, 0),
            NULLIF(bd.SupportChargeAmount, 0),
            CASE WHEN i.TaxTotal > 0 THEN ROUND(i.TaxTotal / (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2) ELSE 0 END
        ) AS SubTotal,
        COALESCE(
            NULLIF(st.ServiceChargeTaxAmount, 0),
            NULLIF(i.TaxTotal, 0),
            NULLIF(bd.TaxAmount, 0),
            ROUND(ISNULL(bd.SupportChargeAmount, 0) * (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2)
        ) AS TaxTotal,
        (
            COALESCE(
                NULLIF(st.ServiceChargeAmount, 0),
                NULLIF(bd.SupportChargeAmount, 0),
                CASE WHEN i.TaxTotal > 0 THEN ROUND(i.TaxTotal / (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2) ELSE 0 END
            ) +
            COALESCE(
                NULLIF(st.ServiceChargeTaxAmount, 0),
                NULLIF(i.TaxTotal, 0),
                NULLIF(bd.TaxAmount, 0),
                ROUND(ISNULL(bd.SupportChargeAmount, 0) * (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2)
            )
        ) AS GrandTotal,
        st.CreatedOn,
        i.InvoiceNumber AS ParentInvoiceNumber,
        i.IssuedOn,
        i.DueOn,
        ISNULL(i.CurrencyCode, 'PKR') AS CurrencyCode,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), NULLIF(LTRIM(RTRIM(bd.CustomerName)), ''), u.Name, '') AS CustomerName,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Address)), ''), NULLIF(LTRIM(RTRIM(u.Address)), ''), '') AS CustomerAddress,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Code)), ''), NULLIF(LTRIM(RTRIM(b.CustomerCode)), ''), NULLIF(LTRIM(RTRIM(bd.CustomerCode)), ''), '') AS CustomerCode,
        COALESCE(NULLIF(LTRIM(RTRIM(c.CnicOrPassport)), ''), NULLIF(LTRIM(RTRIM(c.NTN)), ''), NULLIF(LTRIM(RTRIM(u.CnicOrPassport)), ''), '') AS SntnNtnNic,
        COALESCE(loc.Name, br.[Description], comp.CompanyName, '') AS CenterName,
        COALESCE(comp.CompanyName, br.[Description], loc.Name, '') AS VendorLegalName,
        COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(comp.AddressLine1, '') + ' ' + ISNULL(comp.AddressLine2, ''))), ''), loc.Address, '') AS VendorAddress,
        COALESCE(comp.Contact, '') AS VendorPhone,
        COALESCE(comp.Fax, '') AS VendorFax,
        COALESCE(comp.NTN, '') AS VendorNtn
    FROM dbo.WN_CustomerSTInvoice st WITH (NOLOCK)
    JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = st.CustomerInvoiceId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON (
        (b.CustomerCode IS NOT NULL AND b.CustomerCode <> '' AND c.Code = b.CustomerCode)
        OR (bd.CustomerCode IS NOT NULL AND bd.CustomerCode <> '' AND c.Code = bd.CustomerCode)
        OR (i.UserId IS NOT NULL AND i.UserId > 0 AND c.UserId = i.UserId)
        OR (u.Email IS NOT NULL AND u.Email <> '' AND c.Email = u.Email)
    ) AND (c.IsActive = 1 OR c.IsActive IS NULL)
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = s.LocationId
    LEFT JOIN dbo.Branches br WITH (NOLOCK) ON br.Id = loc.BranchId
    LEFT JOIN dbo.Company comp WITH (NOLOCK) ON comp.Id = COALESCE(loc.CompanyId, br.CompanyId, u.CompanyId)
    WHERE st.PublicId = @PublicId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_GetInvoiceDetailsById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_GetInvoiceDetailsById]
    @InvoiceId INT
AS
BEGIN
    EXEC dbo.WN_GetInvoiceDetailsById @InvoiceId = @InvoiceId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_GetInvoiceEmailData]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 6. WN_sp_GetInvoiceEmailData
CREATE OR ALTER PROCEDURE [dbo].[WN_GetInvoiceEmailData]
    @InvoiceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        i.Id,
        i.InvoiceNumber,
        i.IssuedOn,
        i.DueOn,
        ISNULL(i.GrandTotal, 0) AS GrandTotal,
        ISNULL(i.SubTotal, 0) AS SubTotal,
        ISNULL(i.TaxTotal, 0) AS TaxTotal,
        ISNULL(i.DiscountTotal, 0) AS DiscountTotal,
        ISNULL(NULLIF(u.Email, ''), ISNULL(NULLIF(c.Email, ''), '')) AS TargetEmail,
        ISNULL(NULLIF(c.Company, ''), ISNULL(NULLIF(u.Name, ''), 'Valued Customer')) AS CustomerName,
        ISNULL(s.Name, 'WorkNest Workspace') AS SpaceName
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    WHERE i.Id = @InvoiceId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_GetInvoicesList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 3. Invoices List Stored Procedure
CREATE OR ALTER PROCEDURE [dbo].[WN_GetInvoicesList]
    @Page        INT           = 1,
    @Limit       INT           = 10,
    @Search      NVARCHAR(200) = NULL,
    @TypeId      INT           = NULL,
    @LocationId  INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Page < 1 SET @Page = 1;
    IF @Limit < 1 SET @Limit = 10;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    -- Total count
    SELECT COUNT(1) AS TotalCount
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    WHERE (@LocationId IS NULL OR s.LocationId = @LocationId OR u.LocationId = @LocationId)
      AND (@TypeId IS NULL OR @TypeId <= 0 OR i.InvoiceTypeId = @TypeId)
      AND (@Search IS NULL OR @Search = '' 
           OR i.InvoiceNumber LIKE '%' + @Search + '%' 
           OR u.Email LIKE '%' + @Search + '%' 
           OR c.FirstName LIKE '%' + @Search + '%'
           OR c.LastName LIKE '%' + @Search + '%');

    -- Paginated Items
    SELECT 
        i.Id,
        i.PublicId,
        i.InvoiceNumber,
        i.UserId,
        i.BookingId,
        i.IssuedOn,
        i.DueOn,
        COALESCE(i.BillingPeriodStart, b.StartOn) AS BillingPeriodStart,
        COALESCE(i.BillingPeriodEnd, b.EndOn) AS BillingPeriodEnd,
        ISNULL(i.SubTotal, 0) AS SubTotal,
        ISNULL(i.DiscountTotal, 0) AS DiscountTotal,
        ISNULL(i.TaxTotal, 0) AS TaxTotal,
        ISNULL(i.GrandTotal, 0) AS GrandTotal,
        ISNULL(i.PaidTotal, 0) AS PaidTotal,
        ISNULL(i.GrandTotal - i.PaidTotal, 0) AS BalanceDue,
        i.CurrencyCode,
        i.StatusId,
        i.InvoiceTypeId,
        i.Notes,
        i.CreatedOn,
        ISNULL(s.LocationId, u.LocationId) AS LocationId,
        loc.Name AS LocationName,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
        u.Email AS CustomerEmail,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), '-') AS CompanyName,
        s.Name AS SpaceName,
        s.Code AS SpaceCode,
        ISNULL(b.MonthlyRent, 0) AS MonthlyRent,
        ISNULL(b.BillingPeriodMonths, 1) AS BillingPeriodMonths,
        COALESCE(
            -- existing invoices keep their old values
            CASE i.StatusId WHEN 1 THEN 'Unpaid' WHEN 2 THEN 'Paid' WHEN 3 THEN 'Partial' WHEN 4 THEN 'Overdue' WHEN 5 THEN 'Cancelled' END,
            -- new invoices: label from dbo.OrderStatus
            (SELECT CASE LTRIM(RTRIM(os.Description)) WHEN 'Un Paid' THEN 'Unpaid' WHEN 'Challan Expire' THEN 'Overdue' ELSE LTRIM(RTRIM(os.Description)) END
               FROM dbo.OrderStatus os WITH (NOLOCK) WHERE os.Id = i.StatusId),
            'Unknown') AS StatusLabel
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = ISNULL(s.LocationId, u.LocationId)
    WHERE (@LocationId IS NULL OR s.LocationId = @LocationId OR u.LocationId = @LocationId)
      AND (@TypeId IS NULL OR @TypeId <= 0 OR i.InvoiceTypeId = @TypeId)
      AND (@Search IS NULL OR @Search = '' 
           OR i.InvoiceNumber LIKE '%' + @Search + '%' 
           OR u.Email LIKE '%' + @Search + '%' 
           OR c.FirstName LIKE '%' + @Search + '%'
           OR c.LastName LIKE '%' + @Search + '%')
    ORDER BY i.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_GetRemainingBillingPeriods]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- =================================================================================
-- STORED PROCEDURES
-- =================================================================================

-- Procedure: dbo.WN_GetRemainingBillingPeriods
CREATE OR ALTER PROCEDURE [dbo].[WN_GetRemainingBillingPeriods]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @StartOn DATETIME, @EndOn DATETIME, @MonthlyRent DECIMAL(18,2);
    SELECT @StartOn = StartOn, @EndOn = EndOn, @MonthlyRent = RoomPrice
    FROM dbo.WN_vw_BookingSummary WHERE BookingId = @BookingId;

    -- Return unbilled months excluding already prepaid or invoiced months
    SELECT 
        p.PeriodStartDate,
        p.PeriodEndDate,
        p.MonthName,
        @MonthlyRent AS MonthlyRent,
        ISNULL(p.Status, 'Unbilled') AS Status
    FROM dbo.WN_InvoicePrepaidPeriods p
    WHERE p.BookingId = @BookingId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_GetStatementInvoicePdfData]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_GetStatementInvoicePdfData]
    @InvoiceId INT
AS
BEGIN
    SELECT TOP 1
        i.Id,
        i.InvoiceNumber,
        i.UserId,
        i.BookingId,
        i.IssuedOn,
        i.DueOn,
        COALESCE(i.BillingPeriodStart, b.StartOn, i.IssuedOn) AS BillingPeriodStart,
        COALESCE(i.BillingPeriodEnd, b.EndOn, i.DueOn) AS BillingPeriodEnd,
        ISNULL(i.GrandTotal, 0) AS GrandTotal,
        ISNULL(i.PaidTotal, 0) AS PaidTotal,
        ISNULL(i.SubTotal, 0) AS SubTotal,
        ISNULL(i.DiscountTotal, 0) AS DiscountTotal,
        ISNULL(i.TaxTotal, 0) AS TaxTotal,
        ISNULL(i.CurrencyCode, 'PKR') AS CurrencyCode,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), NULLIF(LTRIM(RTRIM(bd.CustomerName)), ''), u.Name, '') AS AccountName,
        COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), NULLIF(LTRIM(RTRIM(bd.CustomerName)), ''), u.Name, '') AS AttnName,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Address)), ''), NULLIF(LTRIM(RTRIM(u.Address)), ''), '') AS BillingAddress,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Code)), ''), NULLIF(LTRIM(RTRIM(b.CustomerCode)), ''), NULLIF(LTRIM(RTRIM(bd.CustomerCode)), ''), '') AS AccountNumber,
        COALESCE(NULLIF(LTRIM(RTRIM(c.CnicOrPassport)), ''), NULLIF(LTRIM(RTRIM(c.NTN)), ''), NULLIF(LTRIM(RTRIM(u.CnicOrPassport)), ''), '') AS SntnNtnNic,
        COALESCE(loc.Name, br.[Description], comp.CompanyName, '') AS CenterName,
        COALESCE(comp.CompanyName, br.[Description], loc.Name, '') AS VendorLegalName,
        COALESCE(NULLIF(LTRIM(RTRIM(ISNULL(comp.AddressLine1, '') + ' ' + ISNULL(comp.AddressLine2, ''))), ''), loc.Address, '') AS VendorAddress,
        COALESCE(comp.Contact, '') AS VendorPhone,
        COALESCE(comp.Fax, '') AS VendorFax,
        COALESCE(comp.NTN, '') AS VendorNtn,
        ISNULL(NULLIF(bd.AppliedChargePercentage, 0), 10.00) AS AppliedChargePercentage,
        ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) AS AppliedTaxPercentage,
        COALESCE(
            NULLIF(st.ServiceChargeAmount, 0),
            NULLIF(bd.SupportChargeAmount, 0),
            CASE WHEN ISNULL(i.TaxTotal, 0) > 0 THEN ROUND(i.TaxTotal / (ISNULL(NULLIF(bd.AppliedTaxPercentage, 0), 16.00) / 100.0), 2) ELSE 0 END
        ) AS SupportChargeAmount,
        COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(i.SecurityDepositAmount, 0), ISNULL(bd.SecurityDeposit, 0)) AS SecurityDepositAmount,
        st.PublicId AS STPublicId,
        st.STInvoiceNumber
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_CustomerSTInvoice st WITH (NOLOCK) ON st.CustomerInvoiceId = i.Id
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON (
        (b.CustomerCode IS NOT NULL AND b.CustomerCode <> '' AND c.Code = b.CustomerCode)
        OR (bd.CustomerCode IS NOT NULL AND bd.CustomerCode <> '' AND c.Code = bd.CustomerCode)
        OR (i.UserId IS NOT NULL AND i.UserId > 0 AND c.UserId = i.UserId)
        OR (u.Email IS NOT NULL AND u.Email <> '' AND c.Email = u.Email)
    ) AND (c.IsActive = 1 OR c.IsActive IS NULL)
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = s.LocationId
    LEFT JOIN dbo.Branches br WITH (NOLOCK) ON br.Id = loc.BranchId
    LEFT JOIN dbo.Company comp WITH (NOLOCK) ON comp.Id = COALESCE(loc.CompanyId, br.CompanyId, u.CompanyId)
    WHERE i.Id = @InvoiceId;

    SELECT 
        l.ChargeTypeId,
        l.Description,
        ISNULL(l.Quantity, 1) AS Quantity,
        ISNULL(l.UnitPrice, 0) AS UnitPrice,
        (l.Quantity * l.UnitPrice - l.DiscountAmount) AS PriceExclVat,
        l.TaxAmount AS VatAmount,
        l.LineTotal AS TotalInclVat,
        l.TaxRate,
        ISNULL(ct.Label, 'Business Support Services') AS CategoryName
    FROM dbo.WN_InvoiceLines l WITH (NOLOCK)
    LEFT JOIN dbo.WN_ChargeTypes ct WITH (NOLOCK) ON ct.Id = l.ChargeTypeId
    WHERE l.InvoiceId = @InvoiceId
    ORDER BY l.SortOrder, l.Id;

    DECLARE @UserId INT;
    SELECT @UserId = UserId FROM dbo.WN_Invoices WHERE Id = @InvoiceId;

    SELECT 
        ISNULL(SUM(GrandTotal - PaidTotal), 0) AS PriorBalance,
        ISNULL(SUM(PaidTotal), 0) AS PaymentReceived
    FROM dbo.WN_Invoices WITH (NOLOCK)
    WHERE UserId = @UserId AND Id < @InvoiceId;

    SELECT TOP 1 Description AS BankName, ShortDesc AS BankAccountNumber
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE AccountNature = 'Bank' OR Description LIKE '%Bank%';
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Access_Extend]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Access_Extend]
  @employee_no NVARCHAR(MAX), @valid_end DATETIME2(0), @valid_begin DATETIME2(0) = NULL AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRAN;
    UPDATE dbo.WN_HIK_Cards SET Valid_end = @valid_end, Valid_begin = COALESCE(@valid_begin, Valid_begin), Status = 1 WHERE Employee_no = @employee_no;
    UPDATE dbo.WN_HIK_Visitors SET valid_end = @valid_end, valid_begin = COALESCE(@valid_begin, valid_begin), status = 'active' WHERE employee_no = @employee_no;
  COMMIT;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Activity_Recent]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Activity_Recent] @limit INT = 200 AS
BEGIN
  SET NOCOUNT ON;
  SELECT TOP (@limit)
         l.Id AS id, l.Employee_id AS employee_id, l.Device_id AS device_id,
         l.Action AS action, l.Ok AS ok, l.Detail AS detail, l.[Timestamp] AS ts,
         e.name AS employee_name, d.Device_Name AS device_name
  FROM dbo.WN_HIK_SyncLog l WITH (NOLOCK)
  LEFT JOIN dbo.WN_HIK_Employees e ON e.id = l.Employee_id
  LEFT JOIN dbo.WN_HIK_Devices d WITH (NOLOCK) ON d.Id = l.Device_id
  ORDER BY l.Id DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Booking_Attendees]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Booking lookups by external reference.
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Booking_Attendees]
  @ref NVARCHAR(64)
AS
BEGIN
  SET NOCOUNT ON;
  SELECT e.*, g.device_id, g.sync_state, d.name AS device_name
  FROM dbo.WN_HIK_Employees e WITH (NOLOCK)
  LEFT JOIN dbo.WN_HIK_AccessGrants g WITH (NOLOCK) ON g.employee_id = e.id
  LEFT JOIN dbo.WN_HIK_Devices d WITH (NOLOCK) ON d.id = g.device_id
  WHERE e.booking_ref = @ref;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Booking_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Booking_Delete] @ref NVARCHAR(64) AS
BEGIN
  SET NOCOUNT ON;
  DELETE FROM dbo.WN_HIK_Visitors WHERE booking_ref = @ref;  -- grants cascade via TR_WN_HIK_Visitors_GrantCascade
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Card_Register]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Card_Register]
  @employee_no NVARCHAR(MAX) = NULL, @name NVARCHAR(MAX), @card_no NVARCHAR(32),
  @valid_begin DATETIME2(0) = NULL, @valid_end DATETIME2(0) = NULL, @auto_delete BIT = 0,
  @created_by INT = NULL AS
BEGIN
  SET NOCOUNT ON;
  INSERT INTO dbo.WN_HIK_Cards (Employee_no, Name, Card_no, Valid_begin, Valid_end, Auto_delete, Created_by)
  VALUES (@employee_no, @name, @card_no, @valid_begin, @valid_end, @auto_delete, @created_by);
  SELECT SCOPE_IDENTITY() AS id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_Count]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_Count]
AS
BEGIN
  SET NOCOUNT ON;
  SELECT COUNT(*) AS n FROM dbo.WN_HIK_DashboardUsers WITH (NOLOCK);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_Delete]
  @username NVARCHAR(64)
AS
BEGIN
  SET NOCOUNT ON;
  DELETE FROM dbo.WN_HIK_DashboardUsers WHERE username = @username;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_Get]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_Get] @username NVARCHAR(64) AS
BEGIN
  SET NOCOUNT ON;
  SELECT Id AS id, Username AS username, Name AS name, Password_hash AS password_hash,
         Role AS role, Status AS status
  FROM dbo.WN_HIK_DashboardUsers WITH (NOLOCK) WHERE Username = @username;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_List]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_List] AS
BEGIN
  SET NOCOUNT ON;
  SELECT u.Id AS id, u.Username AS username, u.Name AS name, u.Role AS role, u.Status AS status,
         u.Created_at AS created_at, u.Updated_at AS updated_at,
         cb.Username AS created_by_name, ub.Username AS updated_by_name
  FROM dbo.WN_HIK_DashboardUsers u WITH (NOLOCK)
  LEFT JOIN dbo.WN_HIK_DashboardUsers cb WITH (NOLOCK) ON cb.Id = u.Created_by
  LEFT JOIN dbo.WN_HIK_DashboardUsers ub WITH (NOLOCK) ON ub.Id = u.Updated_by
  ORDER BY u.Username;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_Rename]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_Rename]
  @old_username NVARCHAR(64), @new_username NVARCHAR(64)
AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.WN_HIK_DashboardUsers
     SET username = @new_username, updated_at = SYSDATETIME()
   WHERE username = @old_username;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_SetStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_SetStatus] @username NVARCHAR(64), @status INT, @actor_id INT = NULL AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.WN_HIK_DashboardUsers SET Status = @status, Updated_by = COALESCE(@actor_id, Updated_by), Updated_at = SYSDATETIME() WHERE Username = @username;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_DashUser_Upsert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_DashUser_Upsert]
  @username NVARCHAR(64), @password_hash NVARCHAR(256), @role NVARCHAR(16) = NULL,
  @name NVARCHAR(128) = NULL, @display_password NVARCHAR(256) = NULL, @actor_id INT = NULL AS
BEGIN
  SET NOCOUNT ON;
  MERGE dbo.WN_HIK_DashboardUsers WITH (HOLDLOCK) AS t USING (SELECT @username AS u) s ON t.Username = s.u
  WHEN MATCHED THEN UPDATE SET
    Password_hash = @password_hash,
    Display_password = COALESCE(@display_password, t.Display_password),
    Role = COALESCE(@role, t.Role),
    Name = COALESCE(@name, t.Name),
    Updated_by = COALESCE(@actor_id, t.Updated_by),
    Updated_at = SYSDATETIME()
  WHEN NOT MATCHED THEN INSERT (Username, Name, Password_hash, Display_password, Role, Created_by)
    VALUES (s.u, COALESCE(@name, s.u), @password_hash, @display_password, COALESCE(@role, 'user'), @actor_id);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Device_SetOnline]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Device_SetOnline]
  @device_id INT, @online BIT, @model NVARCHAR(64) = NULL, @serial NVARCHAR(64) = NULL AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.WN_HIK_Devices
     SET Online = @online,
         Last_seen = CASE WHEN @online = 1 THEN SYSDATETIME() ELSE Last_seen END,
         Model = COALESCE(@model, Model),
         Serial = COALESCE(@serial, Serial)
   WHERE Id = @device_id;
  -- convenience mirror so the cache table reads standalone; Devices.Online
  -- remains the authority
  UPDATE dbo.WN_HIK_DevCache SET Status = CASE WHEN @online = 1 THEN 1 ELSE 0 END WHERE Device_id = @device_id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Device_UpsertByHost]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Device_UpsertByHost]
  @name NVARCHAR(100), @host NVARCHAR(64), @port INT = 80, @use_https BIT = 0,
  @username NVARCHAR(64), @password NVARCHAR(128),
  @location NVARCHAR(128) = NULL, @grp NVARCHAR(64) = NULL AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  BEGIN TRAN;
    DECLARE @gid INT = NULL;
    IF @grp IS NOT NULL AND LTRIM(RTRIM(@grp)) <> ''
    BEGIN
      SELECT @gid = Id FROM dbo.WN_HIK_Groups WITH (UPDLOCK, HOLDLOCK) WHERE Name = @grp;
      IF @gid IS NULL BEGIN INSERT INTO dbo.WN_HIK_Groups (Name) VALUES (@grp); SET @gid = SCOPE_IDENTITY(); END
    END
    IF EXISTS (SELECT 1 FROM dbo.WN_HIK_Devices WITH (UPDLOCK, HOLDLOCK) WHERE Host = @host AND Port = @port)
      UPDATE dbo.WN_HIK_Devices
         SET Device_Name = @name, Use_https = @use_https,
             Username = @username, Password = @password,
             Location = @location, Group_id = @gid
       WHERE Host = @host AND Port = @port;
    ELSE
      INSERT INTO dbo.WN_HIK_Devices (Device_Name, Host, Port, Use_https, Username, Password, Location, Group_id)
      VALUES (@name, @host, @port, @use_https, @username, @password, @location, @gid);
  COMMIT;
  SELECT Id AS id FROM dbo.WN_HIK_Devices WITH (NOLOCK) WHERE Host = @host AND Port = @port;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Employee_NextNumber]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Employee_NextNumber]
  @floor INT = 1000, @ceiling INT = 2147483647
AS
BEGIN
  SET NOCOUNT ON;
  SELECT CASE WHEN MAX(n) IS NULL OR MAX(n) < @floor - 1 THEN @floor ELSE MAX(n) + 1 END AS next_no
  FROM (SELECT TRY_CAST(employee_no AS INT) AS n FROM dbo.WN_HIK_Employees WITH (NOLOCK)) t
  WHERE n IS NOT NULL AND n >= @floor AND n < @ceiling;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Expiry_Run]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Expiry_Run] @now DATETIME2(0) AS
BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  DECLARE @expired TABLE (id INT, employee_no NVARCHAR(MAX), auto_delete BIT);
  BEGIN TRAN;
    UPDATE dbo.WN_HIK_Cards SET Status = 0
    OUTPUT inserted.Id, inserted.Employee_no, inserted.Auto_delete INTO @expired
     WHERE Valid_end IS NOT NULL AND Valid_end <= @now AND Status = 1;
    UPDATE dbo.WN_HIK_Visitors SET status = 'expired'
    OUTPUT inserted.id, inserted.employee_no, inserted.auto_delete INTO @expired
     WHERE valid_end IS NOT NULL AND valid_end <= @now AND status = 'active';
  COMMIT;
  SELECT * FROM @expired;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Grant_Ensure]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Grant_Ensure] @employee_id INT, @device_id INT AS
BEGIN
  SET NOCOUNT ON;
  MERGE dbo.WN_HIK_AccessGrants WITH (HOLDLOCK) AS t
  USING (SELECT @employee_id AS e, @device_id AS d) s ON t.employee_id = s.e AND t.device_id = s.d
  WHEN NOT MATCHED THEN INSERT (employee_id, device_id, sync_state) VALUES (s.e, s.d, 'pending');
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Grant_MarkRemoving]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Mark all of a person's grants for device-side removal.
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Grant_MarkRemoving]
  @employee_id INT
AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.WN_HIK_AccessGrants SET sync_state = 'removing' WHERE employee_id = @employee_id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Grant_PendingEmployees]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- People with work still queued for the sync engine.
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Grant_PendingEmployees]
AS
BEGIN
  SET NOCOUNT ON;
  SELECT DISTINCT employee_id
  FROM dbo.WN_HIK_AccessGrants WITH (NOLOCK)
  WHERE sync_state IN ('pending','error','removing');
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Grant_SetState]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Update one grant after a push attempt.
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Grant_SetState]
  @grant_id INT, @state NVARCHAR(16), @error NVARCHAR(MAX) = NULL
AS
BEGIN
  SET NOCOUNT ON;
  UPDATE dbo.WN_HIK_AccessGrants
     SET sync_state = @state, last_error = @error, synced_at = SYSDATETIME()
   WHERE id = @grant_id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Log_Write]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Log_Write]
  @employee_id INT = NULL, @device_id INT = NULL,
  @action NVARCHAR(64), @ok BIT, @detail NVARCHAR(MAX) = NULL AS
BEGIN
  SET NOCOUNT ON;
  INSERT INTO dbo.WN_HIK_SyncLog (Employee_id, Device_id, Action, Ok, Detail)
  VALUES (@employee_id, @device_id, @action, @ok, @detail);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Settings_Get]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Get / set a settings value (e.g. the external booking API key).
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Settings_Get]
  @key NVARCHAR(64)
AS
BEGIN
  SET NOCOUNT ON;
  SELECT value FROM dbo.WN_HIK_Settings WITH (NOLOCK) WHERE [key] = @key;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Settings_Set]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Settings_Set] @key NVARCHAR(64), @value NVARCHAR(256) AS
BEGIN
  SET NOCOUNT ON;
  MERGE dbo.WN_HIK_Settings WITH (HOLDLOCK) AS t USING (SELECT @key AS k) s ON t.[key] = s.k
  WHEN MATCHED THEN UPDATE SET value = @value
  WHEN NOT MATCHED THEN INSERT ([key], value) VALUES (s.k, @value);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Stats_Get]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Dashboard counters.
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Stats_Get]
AS
BEGIN
  SET NOCOUNT ON;
  SELECT
    (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK))                           AS devices,
    (SELECT COUNT(*) FROM dbo.WN_HIK_Devices WITH (NOLOCK) WHERE online = 1)          AS devicesOnline,
    (SELECT COUNT(*) FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE status = 'active') AS active,
    (SELECT COUNT(*) FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE status = 'expired') AS expired,
    (SELECT COUNT(*) FROM dbo.WN_HIK_Employees WITH (NOLOCK) WHERE kind = 'card')     AS cards,
    (SELECT COUNT(*) FROM dbo.WN_HIK_AccessGrants WITH (NOLOCK)
      WHERE sync_state IN ('pending','error','removing'))                      AS pendingSync;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_Visitor_Create]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_HIK_Visitor_Create]
  @employee_no NVARCHAR(32), @name NVARCHAR(128), @card_no NVARCHAR(32) = NULL,
  @valid_begin DATETIME2(0), @valid_end DATETIME2(0), @booking_ref NVARCHAR(64) = NULL,
  @created_by INT = NULL AS
BEGIN
  SET NOCOUNT ON;
  INSERT INTO dbo.WN_HIK_Visitors (Employee_no, Name, Card_no, Valid_begin, Valid_end, Auto_delete, Booking_ref, Created_by)
  VALUES (@employee_no, @name, @card_no, @valid_begin, @valid_end, 1, @booking_ref, @created_by);
  SELECT SCOPE_IDENTITY() AS id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_IntegrationLog_GetPending]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_IntegrationLog_GetPending]
    @System     NVARCHAR(50) = NULL,
    @EntityType NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 100
        Id, EntityType, EntityId, System, EventType,
        ExternalRef, RequestBody, StatusId, CreatedOn
    FROM dbo.WN_IntegrationLog WITH (NOLOCK)
    WHERE StatusId IN (1, 3)
      AND (@System     IS NULL OR System     = @System)
      AND (@EntityType IS NULL OR EntityType = @EntityType)
    ORDER BY CreatedOn ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_IntegrationLog_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 18: INTEGRATION LOG
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_IntegrationLog_Insert]
    @EntityType   NVARCHAR(50),
    @EntityId     INT,
    @System       NVARCHAR(50),
    @EventType    NVARCHAR(50),
    @ExternalRef  NVARCHAR(200) = NULL,
    @RequestBody  NVARCHAR(MAX) = NULL,
    @ResponseBody NVARCHAR(MAX) = NULL,
    @PaidAmount   DECIMAL(18,4) = NULL,
    @StatusId     TINYINT       = 1
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_IntegrationLog
        (EntityType, EntityId, System, EventType,
         ExternalRef, RequestBody, ResponseBody,
         PaidAmount, StatusId)
    VALUES
        (@EntityType, @EntityId, @System, @EventType,
         @ExternalRef, @RequestBody, @ResponseBody,
         @PaidAmount, @StatusId);
    SELECT SCOPE_IDENTITY() AS Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_IntegrationLog_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_IntegrationLog_UpdateStatus]
    @Id            INT,
    @StatusId      TINYINT,
    @ResponseBody  NVARCHAR(MAX) = NULL,
    @PaidAmount    DECIMAL(18,4) = NULL,
    @ProcessedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_IntegrationLog SET
        StatusId      = @StatusId,
        ResponseBody  = ISNULL(@ResponseBody, ResponseBody),
        PaidAmount    = ISNULL(@PaidAmount,   PaidAmount),
        ProcessedById = @ProcessedById,
        ProcessedOn   = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoice_CheckExistingBillingPeriod]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Invoice_CheckExistingBillingPeriod]
    @BookingId INT,
    @BillingPeriodStart DATETIME,
    @BillingPeriodEnd DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 
        FROM dbo.WN_Invoices WITH (NOLOCK)
        WHERE BookingId = @BookingId
          AND BillingPeriodStart = @BillingPeriodStart
          AND BillingPeriodEnd = @BillingPeriodEnd
          AND StatusId NOT IN (5, ISNULL((SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Cancelled' ORDER BY Id), 5)) -- was != 3 (Partial) by mistake; void = old 5 / 'Cancelled'
    )
        SELECT 1 AS AlreadyExists;
    ELSE
        SELECT 0 AS AlreadyExists;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoice_CreateRecurring]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 5. REFACTORED: dbo.WN_Invoice_CreateRecurring
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Invoice_CreateRecurring]
    @BookingId INT,
    @BillingPeriodStart DATETIME,
    @BillingPeriodEnd DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @UserId INT;
        DECLARE @MonthlyRent DECIMAL(18,2);
        DECLARE @DiscountAmt DECIMAL(18,2);
        DECLARE @BillingPeriodMonths INT;
        DECLARE @SpaceId INT;
        DECLARE @BookingGuid UNIQUEIDENTIFIER;
        DECLARE @RentAccountId INT;

        SELECT TOP 1 
            @UserId = b.UserId,
            @MonthlyRent = ISNULL(b.MonthlyBasePrice, b.SubtotalAmount),
            @DiscountAmt = ISNULL(b.DiscountAmount, 0.00),
            @BillingPeriodMonths = ISNULL(b.BillingPeriodMonths, 1),
            @SpaceId = b.SpaceId,
            @BookingGuid = b.IdGUID,
            @RentAccountId = ISNULL(b.RentAccountId, 2852)
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        WHERE b.Id = @BookingId AND b.BookingStatusId = 5; -- Active

        IF @UserId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SELECT NULL AS InvoiceId, NULL AS InvoiceNumber, 0 AS TotalAmount;
            RETURN;
        END

        IF @BillingPeriodMonths <= 0 SET @BillingPeriodMonths = 1;

        DECLARE @RentAmount DECIMAL(18,2) = @MonthlyRent * @BillingPeriodMonths;
        DECLARE @DiscountedRent DECIMAL(18,2) = @RentAmount - @DiscountAmt;
        IF @DiscountedRent < 0 SET @DiscountedRent = 0;

        -- Attendant Surcharges for this cycle
        DECLARE @AttendantSurchargeSubtotal DECIMAL(18,2) = 0.00;
        SELECT @AttendantSurchargeSubtotal = ISNULL(SUM(ba.SurchargeApplied * @BillingPeriodMonths), 0.00)
        FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
        JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = ba.BookingDetailId
        WHERE (bd.BookingGuid = @BookingGuid OR bd.CustomerCode IN (SELECT Code FROM dbo.WN_Customers WHERE UserId = @UserId))
          AND ba.IsOverCapacity = 1
          AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(@BillingPeriodStart AS DATE))
          AND ba.AssignedFrom <= CAST(@BillingPeriodEnd AS DATE);

        DECLARE @TotalBaseAmount DECIMAL(18,2) = @DiscountedRent + @AttendantSurchargeSubtotal;

        -- Determine Taxes and Support Charges
        DECLARE @AppliedChargePercentage DECIMAL(5,2) = 10.00;
        DECLARE @AppliedTaxPercentage DECIMAL(5,2) = 16.00;
        DECLARE @ProvinceId INT = NULL;

        SELECT TOP 1 
            @AppliedChargePercentage = ISNULL(bd.AppliedChargePercentage, 10.00),
            @AppliedTaxPercentage = ISNULL(bd.AppliedTaxPercentage, 16.00)
        FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
        WHERE bd.BookingGuid = @BookingGuid;

        IF @AppliedChargePercentage IS NULL OR @AppliedChargePercentage <= 0
        BEGIN
            SELECT TOP 1 @AppliedChargePercentage = ISNULL(SupportChargesPercentage, 10.00)
            FROM dbo.WN_SupportCharges WITH (NOLOCK)
            WHERE IsActive = 1;
        END

        IF @AppliedTaxPercentage IS NULL OR @AppliedTaxPercentage <= 0
        BEGIN
            SELECT TOP 1 @ProvinceId = l.ProvinceId
            FROM dbo.WN_Spaces s WITH (NOLOCK)
            JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
            WHERE s.Id = @SpaceId;

            IF @ProvinceId IS NOT NULL
            BEGIN
                SELECT TOP 1 @AppliedTaxPercentage = ISNULL(TaxPercentage, 16.00)
                FROM dbo.WN_Tax WITH (NOLOCK)
                WHERE ProvinceId = @ProvinceId
                  AND StartDate <= SYSUTCDATETIME()
                  AND (EndDate IS NULL OR EndDate > SYSUTCDATETIME())
                ORDER BY StartDate DESC;
            END
        END

        DECLARE @SupportChargeAmount DECIMAL(18,2) = ROUND(@TotalBaseAmount * (@AppliedChargePercentage / 100.0), 2);
        DECLARE @TaxAmount DECIMAL(18,2) = ROUND(@SupportChargeAmount * (@AppliedTaxPercentage / 100.0), 2);
        DECLARE @GrandTotal DECIMAL(18,2) = @TotalBaseAmount + @TaxAmount;

        DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);
        DECLARE @DueDate DATE = EOMONTH(@Today);
        DECLARE @InvNum NVARCHAR(50) = 'INV-REC-' + CAST(@BookingId AS NVARCHAR(10)) + '-' + CONVERT(NVARCHAR(8), @BillingPeriodStart, 112);
        DECLARE @SubTotalAmount DECIMAL(18,2) = @RentAmount + @AttendantSurchargeSubtotal;
        DECLARE @InvoiceNotes NVARCHAR(MAX) = 'Recurring Invoice for Period ' + CONVERT(NVARCHAR(10), @BillingPeriodStart, 120) + ' to ' + CONVERT(NVARCHAR(10), @BillingPeriodEnd, 120);

        -- Call Centralized SP to insert invoice header
        DECLARE @InvoiceId INT;
        EXEC dbo.WN_Invoices_Insert
            @UserId                 = @UserId,
            @BookingId              = @BookingId,
            @InvoiceNumber          = @InvNum,
            @IssuedOn               = @Today,
            @DueOn                  = @DueDate,
            @SubTotal               = @SubTotalAmount,
            @DiscountTotal          = @DiscountAmt,
            @TaxTotal               = @TaxAmount,
            @GrandTotal             = @GrandTotal,
            @PaidTotal              = 0.00,
            @CurrencyCode           = 'PKR',
            @StatusId               = NULL, -- OrderStatus 'Un Paid' (resolved in WN_Invoices_Insert)
            @Notes                  = @InvoiceNotes,
            @InvoiceTypeId          = 2, -- Recurring/Advance
            @AdvanceRentMonths      = @BillingPeriodMonths,
            @SecurityDepositMonths  = 0,
            @SecurityDepositAmount  = 0.00,
            @AccountsCoaId          = @RentAccountId,
            @BillingPeriodMonths    = @BillingPeriodMonths,
            @BillingPeriodStart     = @BillingPeriodStart,
            @BillingPeriodEnd       = @BillingPeriodEnd,
            @SyncCustomerVendor     = 1,
            @InvoiceId              = @InvoiceId OUTPUT,
            @GeneratedInvoiceNumber = @InvNum OUTPUT;

        DECLARE @LineTaxRate DECIMAL(6,4) = CASE WHEN @TotalBaseAmount > 0 THEN CAST(ROUND(@TaxAmount / @TotalBaseAmount, 4) AS DECIMAL(6,4)) ELSE 0 END;

        -- Line Item 1: Recurring Room Rent
        INSERT INTO dbo.WN_InvoiceLines (
            InvoiceId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, SortOrder
        )
        VALUES (
            @InvoiceId, 1, 'Recurring Room Rent', @BillingPeriodMonths, @MonthlyRent, @DiscountAmt, @LineTaxRate, @RentAccountId, 1
        );

        -- Line Items for Active Attendant Surcharges
        INSERT INTO dbo.WN_InvoiceLines (
            InvoiceId, ChargeTypeId, Description, Quantity, UnitPrice, DiscountAmount, TaxRate, AccountId, SortOrder
        )
        SELECT 
            @InvoiceId,
            1,
            'Capacity Overage Surcharge for Attendant: ' + p.Name + ' (' + ISNULL(p.IdNumber, '') + ') in ' + ISNULL(bd.SpaceName, 'Workspace'),
            ISNULL(ba.ExcessSeatCount, 1),
            ba.SurchargeApplied * @BillingPeriodMonths / CASE WHEN ISNULL(ba.ExcessSeatCount, 1) > 0 THEN ba.ExcessSeatCount ELSE 1 END,
            0,
            @LineTaxRate,
            @RentAccountId,
            2
        FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
        JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = ba.BookingDetailId
        JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = ba.PersonId
        WHERE (bd.BookingGuid = @BookingGuid OR bd.CustomerCode IN (SELECT Code FROM dbo.WN_Customers WHERE UserId = @UserId))
          AND ba.IsOverCapacity = 1
          AND (ba.AssignedTo IS NULL OR ba.AssignedTo >= CAST(@BillingPeriodStart AS DATE))
          AND ba.AssignedFrom <= CAST(@BillingPeriodEnd AS DATE);

        -- Automatically enqueue invoice delivery for background email dispatch
        DECLARE @TargetCustomerEmail NVARCHAR(255);
        SELECT TOP 1 @TargetCustomerEmail = Email FROM dbo.WN_Users WHERE Id = @UserId;

        IF @TargetCustomerEmail IS NOT NULL AND @TargetCustomerEmail <> ''
        BEGIN
            INSERT INTO dbo.WN_InvoiceDeliveryQueue (
                InvoiceId, BookingId, TargetEmail, Attempts, LastAttemptAt, NextRetryAt, Status, CreatedOn
            )
            VALUES (
                @InvoiceId, @BookingId, @TargetCustomerEmail, 0, NULL, SYSUTCDATETIME(), 'Pending', SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;

        SELECT @InvoiceId AS InvoiceId, @InvNum AS InvoiceNumber, @GrandTotal AS TotalAmount;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SELECT NULL AS InvoiceId, NULL AS InvoiceNumber, 0 AS TotalAmount;
    END CATCH;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoices_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 12: INVOICES
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_Invoices_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        i.Id, i.PublicId, i.InvoiceNumber,
        i.UserId, u.Name AS UserName, u.Email AS UserEmail,
        i.BookingId, i.MembershipId,
        i.IssuedOn, i.DueOn,
        i.SubTotal, i.DiscountTotal, i.TaxTotal,
        i.GrandTotal, i.PaidTotal, i.BalanceDue,
        i.CurrencyCode, i.StatusId, i.Notes, i.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    JOIN dbo.WN_Users    u WITH (NOLOCK) ON u.Id = i.UserId
    WHERE (@Search IS NULL OR @Search = ''
           OR u.Name          LIKE '%' + @Search + '%'
           OR u.Email         LIKE '%' + @Search + '%'
           OR i.InvoiceNumber LIKE '%' + @Search + '%')
    ORDER BY i.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoices_GetSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Invoices_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        i.Id, i.PublicId, i.InvoiceNumber,
        i.UserId, u.Name AS UserName, u.Email AS UserEmail,
        i.BookingId, i.MembershipId,
        i.IssuedOn, i.DueOn,
        i.SubTotal, i.DiscountTotal, i.TaxTotal,
        i.GrandTotal, i.PaidTotal, i.BalanceDue,
        i.CurrencyCode, i.StatusId, i.Notes, i.CreatedOn,
        (SELECT il.Id, ct.Code AS ChargeTypeCode, ct.Label AS ChargeTypeLabel,
                il.Description, il.Quantity, il.UnitPrice,
                il.DiscountAmount, il.TaxRate, il.TaxAmount, il.LineTotal
         FROM dbo.WN_InvoiceLines il
         JOIN dbo.WN_ChargeTypes  ct ON ct.Id = il.ChargeTypeId
         WHERE il.InvoiceId = i.Id
         ORDER BY il.SortOrder
         FOR JSON PATH) AS InvoiceLines
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    JOIN dbo.WN_Users    u WITH (NOLOCK) ON u.Id = i.UserId
    WHERE i.Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Invoices_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Invoices_Insert]
(
    -- Core & Existing Parameters (100% Backward Compatible)
    @UserId                 INT,
    @BookingId              INT                 = NULL,
    @MembershipId           INT                 = NULL,
    @InvoiceNumber          NVARCHAR(50)        = NULL,
    @PublicId               UNIQUEIDENTIFIER    = NULL,
    @IssuedOn               DATETIME            = NULL,
    @DueOn                  DATE                = NULL,
    @SubTotal               DECIMAL(18, 4)      = 0.0000,
    @DiscountTotal          DECIMAL(18, 4)      = 0.0000,
    @TaxTotal               DECIMAL(18, 4)      = 0.0000,
    @GrandTotal             DECIMAL(18, 4)      = NULL,
    @PaidTotal              DECIMAL(18, 4)      = 0.0000,
    @CurrencyCode           NVARCHAR(10)        = 'PKR',
    @StatusId               TINYINT             = NULL, -- NULL = OrderStatus 'Un Paid' (looked up below)
    @Notes                  NVARCHAR(MAX)       = NULL,
    @InvoiceTypeId          INT                 = 1, -- 1: Standard, 2: Advance, 3: Recurring, 4: Custom, 5: Surcharge
    @AdvanceRentMonths      INT                 = NULL,
    @SecurityDepositMonths  INT                 = NULL,
    @SecurityDepositAmount  DECIMAL(18, 2)      = 0.00,
    @AccountsCoaId          INT                 = NULL,
    @BillingPeriodMonths    INT                 = NULL,
    @BillingPeriodStart     DATETIME            = NULL,
    @BillingPeriodEnd       DATETIME            = NULL,
    @AccountReceivableId    INT                 = NULL,
    @RentAccountId          INT                 = NULL,
    @ServicesIncomeId       INT                 = NULL,
    @SalesTaxId             INT                 = NULL,
    @SecurityReceivedId     INT                 = NULL,
    @CreatedById            INT                 = NULL,
    @SyncCustomerVendor     BIT                 = 1,

    -- Optional GL Posting & Breakdown Parameters
    @RoomRentExclTax        DECIMAL(18, 4)      = NULL,
    @ServiceCharges         DECIMAL(18, 4)      = NULL,
    @TaxOnServiceCharges    DECIMAL(18, 4)      = NULL,
    @WHTRate                DECIMAL(9, 4)       = 0.0000,
    @WHTAmount              DECIMAL(18, 4)      = 0.0000,
    @LocationId             INT                 = NULL,
    @VoucherTypeId          TINYINT             = 1,    -- Default Voucher Type: 1 (JV)
    @CustomerVendorId       INT                 = NULL,
    @PostToLedger           BIT                 = 1,

    -- Output Parameters
    @InvoiceId              INT                 = NULL OUTPUT,
    @GeneratedInvoiceNumber NVARCHAR(50)        = NULL OUTPUT,
    @GeneratedPublicId      UNIQUEIDENTIFIER    = NULL OUTPUT,
    @VoucherId              INT                 = NULL OUTPUT
)
AS
BEGIN
    SET NOCOUNT ON;
    -- New invoices use dbo.OrderStatus IDs (looked up by description, not hard-coded).
    IF @StatusId IS NULL SET @StatusId = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Un Paid' ORDER BY Id);

    -- 1. Default PublicId
    IF @PublicId IS NULL
    BEGIN
        SET @PublicId = NEWID();
    END

    -- 2. Default IssuedOn (Exact to the second) & DueOn
    IF @IssuedOn IS NULL
    BEGIN
        SET @IssuedOn = GETDATE();
    END
    ELSE IF CAST(@IssuedOn AS TIME) = '00:00:00'
    BEGIN
        -- If only a date portion was passed, preserve the date and append current time down to the second
        SET @IssuedOn = DATEADD(SECOND, DATEDIFF(SECOND, CAST(CAST(GETDATE() AS DATE) AS DATETIME), GETDATE()), CAST(CAST(@IssuedOn AS DATE) AS DATETIME));
    END

    IF @DueOn IS NULL
    BEGIN
        SET @DueOn = CAST(DATEADD(DAY, 7, @IssuedOn) AS DATE);
    END

    -- 3. Resolve CustomerCode across all available sources early
    DECLARE @CustomerCode NVARCHAR(50) = NULL;

    -- (a) From WN_Bookings
    IF @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerCode = CustomerCode 
        FROM dbo.WN_Bookings WITH (NOLOCK) 
        WHERE Id = @BookingId AND CustomerCode IS NOT NULL AND LTRIM(RTRIM(CustomerCode)) <> '';
    END

    -- (b) From WN_BookingDetails
    IF (@CustomerCode IS NULL OR @CustomerCode = '') AND @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerCode = bd.CustomerCode
        FROM dbo.WN_BookingDetails bd WITH (NOLOCK)
        JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
        WHERE b.Id = @BookingId AND bd.CustomerCode IS NOT NULL AND LTRIM(RTRIM(bd.CustomerCode)) <> '';
    END

    -- (c) From WN_Customers via UserId
    IF (@CustomerCode IS NULL OR @CustomerCode = '') AND @UserId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerCode = Code 
        FROM dbo.WN_Customers WITH (NOLOCK) 
        WHERE UserId = @UserId AND Code IS NOT NULL AND LTRIM(RTRIM(Code)) <> '';
    END

    -- (d) From WN_Customers via Email matching WN_Users
    IF (@CustomerCode IS NULL OR @CustomerCode = '') AND @UserId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerCode = c.Code
        FROM dbo.WN_Customers c WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Email = c.Email
        WHERE u.Id = @UserId AND c.Code IS NOT NULL AND LTRIM(RTRIM(c.Code)) <> '';
    END

    -- (e) From CustomersVendors via CustomerVendorId
    IF (@CustomerCode IS NULL OR @CustomerCode = '') AND @CustomerVendorId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerCode = Code
        FROM dbo.CustomersVendors WITH (NOLOCK)
        WHERE Id = @CustomerVendorId AND Code IS NOT NULL AND LTRIM(RTRIM(Code)) <> '';
    END

    -- (f) From CustomersVendors via Email matching WN_Users
    IF (@CustomerCode IS NULL OR @CustomerCode = '') AND @UserId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerCode = cv.Code
        FROM dbo.CustomersVendors cv WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON cv.Email = u.Email
        WHERE u.Id = @UserId AND cv.Code IS NOT NULL AND LTRIM(RTRIM(cv.Code)) <> '';
    END

    -- 4. Fallback / resolution for booking-related metadata (SecurityDepositAmount, Months)
    IF @BookingId IS NOT NULL
    BEGIN
        IF @SecurityDepositMonths IS NULL
        BEGIN
            SELECT @SecurityDepositMonths = SecurityDepositMonths 
            FROM dbo.WN_Bookings WITH (NOLOCK) 
            WHERE Id = @BookingId;
        END

        IF @AdvanceRentMonths IS NULL
        BEGIN
            SELECT @AdvanceRentMonths = AdvanceRentMonths 
            FROM dbo.WN_Bookings WITH (NOLOCK) 
            WHERE Id = @BookingId;
        END

        -- Resolve total required security deposit from Booking or SecurityDeposits table
        DECLARE @BookingSecReq DECIMAL(18, 4) = 0.0000;
        SELECT @BookingSecReq = ISNULL(SecurityDepositRequired, 0.0000)
        FROM dbo.WN_Bookings WITH (NOLOCK)
        WHERE Id = @BookingId;

        IF @BookingSecReq = 0.0000
        BEGIN
            SELECT TOP 1 @BookingSecReq = ISNULL(Amount, 0.0000)
            FROM dbo.WN_SecurityDeposits WITH (NOLOCK)
            WHERE BookingId = @BookingId;
        END

        -- If @SecurityDepositAmount was omitted or is smaller than the full required deposit, adopt the full booking deposit
        IF (@SecurityDepositAmount IS NULL OR @SecurityDepositAmount = 0.00) AND @BookingSecReq > 0
        BEGIN
            SET @SecurityDepositAmount = @BookingSecReq;
        END
        ELSE IF @BookingSecReq > 0 AND @SecurityDepositAmount < @BookingSecReq
        BEGIN
            -- If caller passed single-seat/monthly rate (e.g. 70,000) instead of total deposit (e.g. 490,000)
            SET @SecurityDepositAmount = @BookingSecReq;
        END
    END

    -- 5. Default GrandTotal calculation if omitted
    IF @GrandTotal IS NULL
    BEGIN
        SET @GrandTotal = (@SubTotal - @DiscountTotal) + @TaxTotal + ISNULL(@SecurityDepositAmount, 0.0000);
    END

    -- 6. Default Currency
    IF @CurrencyCode IS NULL OR LTRIM(RTRIM(@CurrencyCode)) = ''
    BEGIN
        SET @CurrencyCode = 'PKR';
    END

    -- 7. Auto-generate InvoiceNumber if omitted
    IF @InvoiceNumber IS NULL OR LTRIM(RTRIM(@InvoiceNumber)) = ''
    BEGIN
        DECLARE @NextId INT;
        SELECT @NextId = ISNULL(MAX(Id), 0) + 1 FROM dbo.WN_Invoices;
        SET @InvoiceNumber = 'INV-' + CAST(YEAR(GETDATE()) AS VARCHAR(4)) + '-' + RIGHT('00000' + CAST(@NextId AS VARCHAR(10)), 5);
    END

    -- 8. Resolve CreatedById from Booking, User, or WN_Users table
    IF @CreatedById IS NULL AND @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CreatedById = CreatedById 
        FROM dbo.WN_Bookings WITH (NOLOCK) 
        WHERE Id = @BookingId AND CreatedById IS NOT NULL;
    END

    IF @CreatedById IS NULL AND @UserId IS NOT NULL
    BEGIN
        SET @CreatedById = @UserId;
    END

    IF @CreatedById IS NULL
    BEGIN
        SELECT TOP 1 @CreatedById = Id 
        FROM dbo.WN_Users WITH (NOLOCK) 
        ORDER BY Id ASC;
    END

    -- 9. Location Resolution (LocationId -> CompanyId & BranchId)
    IF @LocationId IS NULL AND @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1 @LocationId = s.LocationId
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        WHERE b.Id = @BookingId;
    END

    -- Fallback: lookup via User's latest booking if not provided
    IF @LocationId IS NULL AND @UserId IS NOT NULL
    BEGIN
        SELECT TOP 1 @LocationId = s.LocationId
        FROM dbo.WN_Bookings b WITH (NOLOCK)
        JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        WHERE b.UserId = @UserId
        ORDER BY b.Id DESC;
    END

    -- Resolve CompanyId and BranchId from WN_Locations
    DECLARE @CompanyId INT = NULL;
    DECLARE @BranchId  INT = NULL;

    IF @LocationId IS NOT NULL
    BEGIN
        SELECT TOP 1 
            @CompanyId = CompanyId, 
            @BranchId  = BranchId
        FROM dbo.WN_Locations WITH (NOLOCK)
        WHERE Id = @LocationId;
    END

    IF @PostToLedger = 1 AND (@LocationId IS NULL OR @CompanyId IS NULL OR @BranchId IS NULL)
    BEGIN
        RAISERROR('Unable to resolve LocationId, CompanyId, or BranchId for invoice ledger posting.', 16, 1);
        RETURN -1;
    END

    -- 10. Breakdown Derivation
    DECLARE @NetBase DECIMAL(18, 4) = 0.0000;
    DECLARE @SecAmt  DECIMAL(18, 4) = ISNULL(@SecurityDepositAmount, 0.0000);

    IF @GrandTotal IS NOT NULL AND @GrandTotal > 0
    BEGIN
        SET @NetBase = @GrandTotal - @SecAmt - @TaxTotal;
    END
    ELSE
    BEGIN
        SET @NetBase = (@SubTotal - @DiscountTotal);
        IF @NetBase < 0 SET @NetBase = 0.0000;
        SET @GrandTotal = @NetBase + @TaxTotal + @SecAmt;
    END

    IF @NetBase < 0 SET @NetBase = 0.0000;

    IF (@RoomRentExclTax IS NULL AND @ServiceCharges IS NULL AND @TaxOnServiceCharges IS NULL)
       OR (ISNULL(@RoomRentExclTax, 0) = 0 AND ISNULL(@ServiceCharges, 0) = 0 AND ISNULL(@TaxOnServiceCharges, 0) = 0 AND @GrandTotal > 0)
    BEGIN
        IF @TaxTotal > 0
        BEGIN
            -- Custom / Surcharge invoices: entire taxable base is treated as Service Fee
            IF @InvoiceTypeId IN (4, 5) OR @BookingId IS NULL
            BEGIN
                SET @TaxOnServiceCharges = @TaxTotal;
                SET @ServiceCharges      = @NetBase;
                SET @RoomRentExclTax     = 0.0000;
            END
            ELSE
            BEGIN
                -- Standard / Advance / Recurring booking invoices
                DECLARE @AppliedChargePct DECIMAL(5,2) = 10.00;
                IF @BookingId IS NOT NULL
                BEGIN
                    SELECT TOP 1 @AppliedChargePct = ISNULL(AppliedChargePercentage, 10.00)
                    FROM dbo.WN_BookingDetails WITH (NOLOCK)
                    WHERE BookingGuid = (SELECT TOP 1 IdGUID FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @BookingId);
                END

                SET @TaxOnServiceCharges = @TaxTotal;
                SET @ServiceCharges      = ROUND(@NetBase * (@AppliedChargePct / 100.0), 4);
                SET @RoomRentExclTax     = @NetBase - @ServiceCharges;
            END
        END
        ELSE
        BEGIN
            -- Zero tax invoices
            SET @RoomRentExclTax     = @NetBase;
            SET @ServiceCharges      = 0.0000;
            SET @TaxOnServiceCharges = 0.0000;
        END
    END
    ELSE
    BEGIN
        SET @RoomRentExclTax     = ISNULL(@RoomRentExclTax, 0.0000);
        SET @ServiceCharges      = ISNULL(@ServiceCharges, 0.0000);
        SET @TaxOnServiceCharges = ISNULL(@TaxOnServiceCharges, 0.0000);
    END

    -- 11. Balance & Fractional Rounding Alignment (Absorb sub-rupee rounding delta)
    DECLARE @CalculatedTotal DECIMAL(18, 4) = @RoomRentExclTax + @ServiceCharges + @TaxOnServiceCharges + @SecAmt;
    DECLARE @RoundingDiff    DECIMAL(18, 4) = @GrandTotal - @CalculatedTotal;

    IF ABS(@RoundingDiff) > 1.00
    BEGIN
        DECLARE @CalcTotalStr VARCHAR(30) = CAST(@CalculatedTotal AS VARCHAR(30));
        DECLARE @GrandTotalStr VARCHAR(30) = CAST(@GrandTotal AS VARCHAR(30));
        RAISERROR('Invoice breakdown amounts sum (%s) does not match GrandTotal (%s).', 16, 1, @CalcTotalStr, @GrandTotalStr);
        RETURN -1;
    END
    ELSE IF @RoundingDiff <> 0.0000
    BEGIN
        IF @RoomRentExclTax > 0
            SET @RoomRentExclTax = @RoomRentExclTax + @RoundingDiff;
        ELSE IF @ServiceCharges > 0
            SET @ServiceCharges = @ServiceCharges + @RoundingDiff;
        ELSE
            SET @RoomRentExclTax = @RoomRentExclTax + @RoundingDiff;
    END

    -- 12. Dynamic GL Accounts Lookup (Location-Aware by Date)
    DECLARE @ARAccountId            INT = NULL;
    DECLARE @RentAccountId_Loc      INT = NULL;
    DECLARE @SecurityReceivedId_Loc INT = NULL;
    DECLARE @SalesTaxId_Loc         INT = NULL;
    DECLARE @ServicesIncomeId_Loc   INT = NULL;

    IF @LocationId IS NOT NULL
    BEGIN
        SELECT 
            @ARAccountId            = MAX(CASE WHEN ChargeTypeId = 1 THEN AccountReceivableId END),
            @RentAccountId_Loc      = MAX(CASE WHEN ChargeTypeId = 1 THEN RentAccountId END),
            @SecurityReceivedId_Loc = MAX(CASE WHEN ChargeTypeId = 2 THEN SecurityReceivedId END),
            @SalesTaxId_Loc         = MAX(CASE WHEN ChargeTypeId = 3 THEN SalesTaxId END),
            @ServicesIncomeId_Loc   = MAX(CASE WHEN ChargeTypeId = 4 THEN ServicesIncomeId END)
        FROM dbo.WN_ChargeTypeAccountMapping WITH (NOLOCK)
        WHERE LocationId = @LocationId
          AND EffectiveFrom <= @IssuedOn
          AND (EffectiveTo IS NULL OR EffectiveTo >= @IssuedOn);
    END

    -- Validate required GL account mappings
    IF @PostToLedger = 1
    BEGIN
        DECLARE @IssuedOnStr VARCHAR(30) = CONVERT(VARCHAR(30), @IssuedOn, 120);

        IF @ARAccountId IS NULL
        BEGIN
            RAISERROR('No GL account mapping for LocationId %d, ChargeTypeId 1 on %s', 16, 1, @LocationId, 1, @IssuedOnStr);
            RETURN -1;
        END

        IF @RoomRentExclTax > 0 AND @RentAccountId_Loc IS NULL
        BEGIN
            RAISERROR('No GL account mapping for LocationId %d, ChargeTypeId 1 on %s', 16, 1, @LocationId, 1, @IssuedOnStr);
            RETURN -1;
        END

        IF @SecAmt > 0 AND @SecurityReceivedId_Loc IS NULL
        BEGIN
            RAISERROR('No GL account mapping for LocationId %d, ChargeTypeId 2 on %s', 16, 1, @LocationId, 2, @IssuedOnStr);
            RETURN -1;
        END

        IF @TaxOnServiceCharges > 0 AND @SalesTaxId_Loc IS NULL
        BEGIN
            RAISERROR('No GL account mapping for LocationId %d, ChargeTypeId 3 on %s', 16, 1, @LocationId, 3, @IssuedOnStr);
            RETURN -1;
        END

        IF @ServiceCharges > 0 AND @ServicesIncomeId_Loc IS NULL
        BEGIN
            RAISERROR('No GL account mapping for LocationId %d, ChargeTypeId 4 on %s', 16, 1, @LocationId, 4, @IssuedOnStr);
            RETURN -1;
        END
    END

    -- Map resolved accounts to invoice header columns
    SET @AccountReceivableId = @ARAccountId;
    SET @RentAccountId       = @RentAccountId_Loc;
    SET @SecurityReceivedId  = @SecurityReceivedId_Loc;
    SET @SalesTaxId          = @SalesTaxId_Loc;
    SET @ServicesIncomeId    = @ServicesIncomeId_Loc;
    IF @AccountsCoaId IS NULL SET @AccountsCoaId = @RentAccountId;

    -- 13. Atomic Transaction & Posting Pipeline
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Step A: Insert into dbo.WN_Invoices
        INSERT INTO dbo.WN_Invoices (
            PublicId,
            InvoiceNumber,
            UserId,
            BookingId,
            MembershipId,
            IssuedOn,
            DueOn,
            SubTotal,
            DiscountTotal,
            TaxTotal,
            GrandTotal,
            PaidTotal,
            CurrencyCode,
            StatusId,
            Notes,
            CreatedOn,
            CreatedById,
            InvoiceTypeId,
            AdvanceRentMonths,
            SecurityDepositMonths,
            SecurityDepositAmount,
            AccountsCoaId,
            BillingPeriodMonths,
            BillingPeriodStart,
            BillingPeriodEnd,
            AccountReceivableId,
            RentAccountId,
            ServicesIncomeId,
            SalesTaxId,
            SecurityReceivedId,
            RoomRentExclTax,
            ServiceCharges,
            TaxOnServiceCharges,
            WHTRate,
            WHTAmount
        )
        VALUES (
            @PublicId,
            @InvoiceNumber,
            @UserId,
            @BookingId,
            @MembershipId,
            @IssuedOn,
            @DueOn,
            @SubTotal,
            @DiscountTotal,
            @TaxTotal,
            @GrandTotal,
            @PaidTotal,
            @CurrencyCode,
            @StatusId,
            @Notes,
            SYSUTCDATETIME(),
            @CreatedById,
            @InvoiceTypeId,
            @AdvanceRentMonths,
            @SecurityDepositMonths,
            @SecurityDepositAmount,
            @AccountsCoaId,
            @BillingPeriodMonths,
            @BillingPeriodStart,
            @BillingPeriodEnd,
            @AccountReceivableId,
            @RentAccountId,
            @ServicesIncomeId,
            @SalesTaxId,
            @SecurityReceivedId,
            @RoomRentExclTax,
            @ServiceCharges,
            @TaxOnServiceCharges,
            ISNULL(@WHTRate, 0.0000),
            ISNULL(@WHTAmount, 0.0000)
        );

        SET @InvoiceId = SCOPE_IDENTITY();
        SET @GeneratedInvoiceNumber = @InvoiceNumber;
        SET @GeneratedPublicId = @PublicId;

        -- Step B: Update or Insert dbo.WN_SecurityDeposits (RefId = Invoice.Id, RefNo = Invoice.InvoiceNumber, CustomerId = CustomerCode)
        IF @BookingId IS NOT NULL
        BEGIN
            UPDATE dbo.WN_SecurityDeposits
            SET RefId      = @InvoiceId,
                RefNo      = @InvoiceNumber,
                CustomerId = COALESCE(CustomerId, @CustomerCode),
                Amount     = CASE 
                                WHEN (Amount IS NULL OR Amount = 0) AND @SecAmt > 0 THEN @SecAmt 
                                WHEN Amount > 0 AND @SecAmt > Amount THEN @SecAmt
                                ELSE Amount 
                             END
            WHERE BookingId = @BookingId;

            -- If no record existed yet for this booking and security deposit amount > 0, insert it
            IF @SecAmt > 0 AND NOT EXISTS (SELECT 1 FROM dbo.WN_SecurityDeposits WITH (NOLOCK) WHERE BookingId = @BookingId)
            BEGIN
                INSERT INTO dbo.WN_SecurityDeposits
                    (BookingId, UserId, Amount, AccountId, StatusId, Notes, UpdatedById, SecurityReceivedId,
                     AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, CustomerId, RefId, RefNo)
                VALUES
                    (@BookingId, @UserId, @SecAmt, @SecurityReceivedId, 1, 'Security deposit for invoice ' + @InvoiceNumber, @CreatedById, @SecurityReceivedId,
                     @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @CustomerCode, @InvoiceId, @InvoiceNumber);
            END
        END
        ELSE IF @SecAmt > 0
        BEGIN
            INSERT INTO dbo.WN_SecurityDeposits
                (BookingId, UserId, Amount, AccountId, StatusId, Notes, UpdatedById, SecurityReceivedId,
                 AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, CustomerId, RefId, RefNo)
            VALUES
                (NULL, @UserId, @SecAmt, @SecurityReceivedId, 1, 'Security deposit for invoice ' + @InvoiceNumber, @CreatedById, @SecurityReceivedId,
                 @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @CustomerCode, @InvoiceId, @InvoiceNumber);
        END

        -- Step C: Customer/Vendor Sync
        IF @SyncCustomerVendor = 1
        BEGIN
            IF OBJECT_ID('dbo.WN_CustomersVendors_SyncOnInvoice', 'P') IS NOT NULL
            BEGIN
                EXEC dbo.WN_CustomersVendors_SyncOnInvoice
                    @CustomerId  = NULL,
                    @UserId      = @UserId,
                    @BookingId   = @BookingId,
                    @CreatedById = @CreatedById;
            END
        END

        -- Step D: Accounting Postings (when @PostToLedger = 1 and @GrandTotal > 0)
        IF @PostToLedger = 1 AND @GrandTotal > 0
        BEGIN
            -- Derive CustomerVendorId if not supplied
            IF @CustomerVendorId IS NULL
            BEGIN
                IF @CustomerCode IS NOT NULL AND @CustomerCode <> ''
                BEGIN
                    SELECT TOP 1 @CustomerVendorId = Id 
                    FROM dbo.CustomersVendors WITH (NOLOCK) 
                    WHERE Code = @CustomerCode 
                    ORDER BY Id DESC;
                END

                IF @CustomerVendorId IS NULL AND @UserId IS NOT NULL
                BEGIN
                    SELECT TOP 1 @CustomerVendorId = cv.Id
                    FROM dbo.CustomersVendors cv WITH (NOLOCK)
                    JOIN dbo.WN_Users u WITH (NOLOCK) ON cv.Email = u.Email
                    WHERE u.Id = @UserId AND cv.Email IS NOT NULL AND cv.Email <> ''
                    ORDER BY cv.Id DESC;
                END
            END

            -- Derive Customer / Company Name for description suffix (on the right)
            DECLARE @CustomerDisplayName NVARCHAR(200) = NULL;

            IF @CustomerCode IS NOT NULL AND @CustomerCode <> ''
            BEGIN
                SELECT TOP 1 
                    @CustomerDisplayName = CASE 
                        WHEN c.Company IS NOT NULL AND LTRIM(RTRIM(c.Company)) <> '' THEN LTRIM(RTRIM(c.Company))
                        ELSE LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, '')))
                    END
                FROM dbo.WN_Customers c WITH (NOLOCK)
                WHERE c.Code = @CustomerCode;
            END

            IF (@CustomerDisplayName IS NULL OR @CustomerDisplayName = '') AND @BookingId IS NOT NULL
            BEGIN
                SELECT TOP 1 
                    @CustomerDisplayName = CASE 
                        WHEN c.Company IS NOT NULL AND LTRIM(RTRIM(c.Company)) <> '' THEN LTRIM(RTRIM(c.Company))
                        ELSE LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, '')))
                    END
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                JOIN dbo.WN_Customers c WITH (NOLOCK) ON (c.Code = b.CustomerCode OR (b.CustomerCode IS NULL AND c.UserId = b.UserId))
                WHERE b.Id = @BookingId;
            END

            IF (@CustomerDisplayName IS NULL OR @CustomerDisplayName = '') AND @UserId IS NOT NULL
            BEGIN
                SELECT TOP 1 
                    @CustomerDisplayName = CASE 
                        WHEN c.Company IS NOT NULL AND LTRIM(RTRIM(c.Company)) <> '' THEN LTRIM(RTRIM(c.Company))
                        ELSE LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, '')))
                    END
                FROM dbo.WN_Customers c WITH (NOLOCK)
                WHERE c.UserId = @UserId;
            END

            IF (@CustomerDisplayName IS NULL OR @CustomerDisplayName = '') AND @CustomerVendorId IS NOT NULL
            BEGIN
                SELECT TOP 1 
                    @CustomerDisplayName = LTRIM(RTRIM(Name))
                FROM dbo.CustomersVendors WITH (NOLOCK)
                WHERE Id = @CustomerVendorId;
            END

            IF (@CustomerDisplayName IS NULL OR @CustomerDisplayName = '') AND @UserId IS NOT NULL
            BEGIN
                SELECT TOP 1 
                    @CustomerDisplayName = LTRIM(RTRIM(Name))
                FROM dbo.WN_Users WITH (NOLOCK)
                WHERE Id = @UserId;
            END

            DECLARE @DescSuffix VARCHAR(200) = '';
            IF @CustomerDisplayName IS NOT NULL AND LTRIM(RTRIM(@CustomerDisplayName)) <> ''
            BEGIN
                SET @DescSuffix = ' - ' + LTRIM(RTRIM(@CustomerDisplayName));
            END

            -- 1. Create Voucher via dbo.Vouchers_Insert
            DECLARE @VoucherTable TABLE (Id INT, Code VARCHAR(50));
            DECLARE @VoucherShortGUID VARCHAR(50) = CONVERT(VARCHAR(50), NEWID());

            INSERT INTO @VoucherTable (Id, Code)
            EXEC dbo.Vouchers_Insert
                @TypeId              = @VoucherTypeId,
                @TransDate           = @IssuedOn,
                @TotalAmount         = @GrandTotal,
                @CompanyId           = @CompanyId,
                @CreatedById         = @CreatedById,
                @ShortGUID           = @VoucherShortGUID,
                @RefNo               = @InvoiceNumber,
                @RefId               = @InvoiceId,
                @BranchId            = @BranchId,
                @FundMangId          = NULL,
                @CreatedByBranchId   = @BranchId,
                @CreatedByCompanyId  = @CompanyId;

            SELECT TOP 1 @VoucherId = Id FROM @VoucherTable;

            IF @VoucherId IS NULL OR @VoucherId <= 0
            BEGIN
                RAISERROR('Failed to create Voucher record for invoice %s', 16, 1, @InvoiceNumber);
            END

            -- 2. Create Ledger Entries via dbo.Ledgers_Insert (Customer / Company Name on the right)
            DECLARE @DescAR VARCHAR(255)       = LEFT('Accounts Receivable - Invoice ' + @InvoiceNumber + @DescSuffix, 255);
            DECLARE @DescRent VARCHAR(255)     = LEFT('Rent - Invoice ' + @InvoiceNumber + @DescSuffix, 255);
            DECLARE @DescService VARCHAR(255)  = LEFT('Services Income - Invoice ' + @InvoiceNumber + @DescSuffix, 255);
            DECLARE @DescTax VARCHAR(255)      = LEFT('Sales Tax - Invoice ' + @InvoiceNumber + @DescSuffix, 255);
            DECLARE @DescSecurity VARCHAR(255) = LEFT('Security Received - Invoice ' + @InvoiceNumber + @DescSuffix, 255);

            -- (a) Accounts Receivable (DEBIT)
            EXEC dbo.Ledgers_Insert
                @VoucherId                   = @VoucherId,
                @AccountId                   = @ARAccountId,
                @Description                 = @DescAR,
                @ProjectId                   = NULL,
                @Debit                       = @GrandTotal,
                @Credit                      = 0.0000,
                @VehicleId                   = NULL,
                @ConstructionProjectActiveId = NULL;

            -- (b) Room Rent (CREDIT)
            IF @RoomRentExclTax > 0
            BEGIN
                EXEC dbo.Ledgers_Insert
                    @VoucherId                   = @VoucherId,
                    @AccountId                   = @RentAccountId_Loc,
                    @Description                 = @DescRent,
                    @ProjectId                   = NULL,
                    @Debit                       = 0.0000,
                    @Credit                      = @RoomRentExclTax,
                    @VehicleId                   = NULL,
                    @ConstructionProjectActiveId = NULL;
            END

            -- (c) Services Income (CREDIT)
            IF @ServiceCharges > 0
            BEGIN
                EXEC dbo.Ledgers_Insert
                    @VoucherId                   = @VoucherId,
                    @AccountId                   = @ServicesIncomeId_Loc,
                    @Description                 = @DescService,
                    @ProjectId                   = NULL,
                    @Debit                       = 0.0000,
                    @Credit                      = @ServiceCharges,
                    @VehicleId                   = NULL,
                    @ConstructionProjectActiveId = NULL;
            END

            -- (d) Sales Tax (CREDIT)
            IF @TaxOnServiceCharges > 0
            BEGIN
                EXEC dbo.Ledgers_Insert
                    @VoucherId                   = @VoucherId,
                    @AccountId                   = @SalesTaxId_Loc,
                    @Description                 = @DescTax,
                    @ProjectId                   = NULL,
                    @Debit                       = 0.0000,
                    @Credit                      = @TaxOnServiceCharges,
                    @VehicleId                   = NULL,
                    @ConstructionProjectActiveId = NULL;
            END

            -- (e) Security Deposit Received (CREDIT)
            IF @SecAmt > 0
            BEGIN
                EXEC dbo.Ledgers_Insert
                    @VoucherId                   = @VoucherId,
                    @AccountId                   = @SecurityReceivedId_Loc,
                    @Description                 = @DescSecurity,
                    @ProjectId                   = NULL,
                    @Debit                       = 0.0000,
                    @Credit                      = @SecAmt,
                    @VehicleId                   = NULL,
                    @ConstructionProjectActiveId = NULL;
            END

            -- 3. Create CustomerLedger Entry via dbo.CustomerLedger_Insert (AR DEBIT)
            DECLARE @CustLedgerShortGUID VARCHAR(50) = CONVERT(VARCHAR(50), NEWID());
            EXEC dbo.CustomerLedger_Insert
                @Description         = @DescAR,
                @Debit               = @GrandTotal,
                @Credit              = 0.0000,
                @LedgerDate          = @IssuedOn,
                @CustomerVendorId    = @CustomerVendorId,
                @AccountId           = @ARAccountId,
                @Status              = @StatusId,
                @CreatedById         = @CreatedById,
                @ShortGUID           = @CustLedgerShortGUID,
                @CompanyId           = @CompanyId,
                @RefNo               = @InvoiceNumber,
                @RefId               = @InvoiceId,
                @TypeId              = @VoucherTypeId,
                @CreatedByBranchId   = @BranchId,
                @CreatedByCompanyId  = @CompanyId,
                @BranchId            = @BranchId;
        END

        COMMIT TRANSACTION;

        -- Return output resultset for callers
        SELECT 
            @InvoiceId              AS InvoiceId,
            @GeneratedInvoiceNumber AS InvoiceNumber,
            @GeneratedPublicId      AS PublicId,
            @VoucherId              AS VoucherId;

        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
        RETURN -1;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_LeaseTemplates_GetActiveByName]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 5. Stored Procedure: WN_LeaseTemplates_GetActiveByName
CREATE OR ALTER PROCEDURE [dbo].[WN_LeaseTemplates_GetActiveByName]
    @Name NVARCHAR(100) = 'StandardLeaseAgreement'
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1 
        t.Id,
        t.Name,
        t.ContentHtml,
        t.IsActive,
        t.CreatedAt,
        t.CreatedBy,
        u.Email AS CreatedByName
    FROM dbo.WN_LeaseTemplates t WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = t.CreatedBy
    WHERE t.Name = @Name AND t.IsActive = 1
    ORDER BY t.Id DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_LeaseTemplates_Publish]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 6. Stored Procedure: WN_LeaseTemplates_Publish
CREATE OR ALTER PROCEDURE [dbo].[WN_LeaseTemplates_Publish]
    @Name NVARCHAR(100),
    @ContentHtml NVARCHAR(MAX),
    @CreatedBy INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    BEGIN TRY
        -- Deactivate existing active versions for this template name
        UPDATE dbo.WN_LeaseTemplates
        SET IsActive = 0
        WHERE Name = @Name AND IsActive = 1;

        -- Insert new active version
        INSERT INTO dbo.WN_LeaseTemplates (
            Name,
            ContentHtml,
            IsActive,
            CreatedAt,
            CreatedBy
        )
        OUTPUT 
            INSERTED.Id,
            INSERTED.Name,
            INSERTED.ContentHtml,
            INSERTED.IsActive,
            INSERTED.CreatedAt,
            INSERTED.CreatedBy
        VALUES (
            @Name,
            @ContentHtml,
            1,
            SYSUTCDATETIME(),
            @CreatedBy
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Locations SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_GetActive]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 3. WN_Locations_GetActive
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_GetActive]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id, l.IdGUID AS PublicId, l.Name, l.Address,
        l.CityId, ci.Description AS CityName,
        l.BranchId, br.[Description] AS BranchName,
        br.CompanyId, co.CompanyName AS CompanyName,
        l.OpeningTime, l.ClosingTime, l.IsActive
    FROM dbo.WN_Locations l  WITH (NOLOCK)
    JOIN dbo.Branches     br WITH (NOLOCK) ON br.Id = l.BranchId
    JOIN dbo.Company      co WITH (NOLOCK) ON co.Id = br.CompanyId
    JOIN dbo.City         ci WITH (NOLOCK) ON ci.Id = l.CityId
    WHERE l.IsActive = 1
    ORDER BY l.Name ASC;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_GetAll]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_GetAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        l.Id, l.IdGUID AS PublicId, l.Name, l.Address,
        l.CityId, ci.Description AS CityName,
        l.BranchId, br.[Description] AS BranchName,
        br.CompanyId, co.CompanyName AS CompanyName,
        l.OpeningTime, l.ClosingTime, l.IsActive
    FROM dbo.WN_Locations l  WITH (NOLOCK)
    JOIN dbo.Branches     br WITH (NOLOCK) ON br.Id = l.BranchId
    JOIN dbo.Company      co WITH (NOLOCK) ON co.Id = br.CompanyId
    JOIN dbo.City    ci WITH (NOLOCK) ON ci.Id = l.CityId
    WHERE l.IsActive = 1
    ORDER BY l.Name ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 4. WN_Locations_GetList
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_GetList]
    @Page      INT           = 1,
    @Limit     INT           = 50,
    @Search    NVARCHAR(255) = NULL,
    @BranchId  INT           = NULL,
    @CompanyId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        l.Id, l.IdGUID AS PublicId, l.Name, l.Address,
        l.CityId, ci.Description AS CityName,
        l.BranchId, br.[Description] AS BranchName,
        br.CompanyId, co.CompanyName AS CompanyName,
        l.OpeningTime, l.ClosingTime,
        l.Latitude, l.Longitude, l.IsActive,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Locations l  WITH (NOLOCK)
    JOIN dbo.Branches     br WITH (NOLOCK) ON br.Id = l.BranchId
    JOIN dbo.Company      co WITH (NOLOCK) ON co.Id = br.CompanyId
    JOIN dbo.City         ci WITH (NOLOCK) ON ci.Id = l.CityId
    WHERE l.IsActive = 1
      AND (@BranchId  IS NULL OR l.BranchId   = @BranchId)
      AND (@CompanyId IS NULL OR br.CompanyId = @CompanyId)
      AND (@Search    IS NULL OR @Search = ''
           OR l.Name    LIKE '%' + @Search + '%'
           OR l.Address LIKE '%' + @Search + '%')
    ORDER BY l.Name ASC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_Insert]
    @BranchId    INT,
    @Name        NVARCHAR(MAX),
    @Address     NVARCHAR(MAX) = NULL,
    @CityId      INT,
    @OpeningTime NVARCHAR(10)  = NULL,
    @ClosingTime NVARCHAR(10)  = NULL,
    @Latitude    DECIMAL(10,7) = NULL,
    @Longitude   DECIMAL(10,7) = NULL,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Locations (
        IdGUID, BranchId, Name, Address, CityId,
        OpeningTime, ClosingTime, Latitude, Longitude, IsActive, CreatedOn, CreatedById
    ) VALUES (
        NEWID(), @BranchId, @Name, @Address, @CityId,
        @OpeningTime, @ClosingTime, @Latitude, @Longitude, 1, GETUTCDATE(), @CreatedById
    );

    SELECT l.Id, l.IdGUID AS PublicId, l.IdGUID, l.Name, l.Address, l.CityId, l.OpeningTime, l.ClosingTime, l.IsActive, l.CreatedOn
    FROM dbo.WN_Locations l WITH (NOLOCK)
    WHERE l.Id = SCOPE_IDENTITY();
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Locations_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Locations_Update]
    @Id          INT,
    @Name        NVARCHAR(200) = NULL,
    @Address     NVARCHAR(500) = NULL,
    @CityId      INT           = NULL,
    @OpeningTime TIME(0)       = NULL,
    @ClosingTime TIME(0)       = NULL,
    @Latitude    DECIMAL(10,7) = NULL,
    @Longitude   DECIMAL(10,7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Locations SET
        Name        = ISNULL(@Name,        Name),
        Address     = ISNULL(@Address,     Address),
        CityId      = ISNULL(@CityId,      CityId),
        OpeningTime = ISNULL(@OpeningTime, OpeningTime),
        ClosingTime = ISNULL(@ClosingTime, ClosingTime),
        Latitude    = ISNULL(@Latitude,    Latitude),
        Longitude   = ISNULL(@Longitude,   Longitude)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlanFeatures_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlanFeatures_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlanFeatures SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlanFeatures_GetByPlan]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 3: MembershipPlanFeatures SPs
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlanFeatures_GetByPlan]
    @PlanId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, PublicId, PlanId, FeatureName, FeatureValue, SortOrder, IsActive
    FROM dbo.WN_MembershipPlanFeatures WITH (NOLOCK)
    WHERE PlanId = @PlanId AND IsActive = 1
    ORDER BY SortOrder;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlanFeatures_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlanFeatures_Insert]
    @PlanId       INT,
    @FeatureName  NVARCHAR(120),
    @FeatureValue NVARCHAR(120) = NULL,
    @SortOrder    SMALLINT      = 0
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_MembershipPlanFeatures (PlanId, FeatureName, FeatureValue, SortOrder, IsActive)
    VALUES (@PlanId, @FeatureName, @FeatureValue, @SortOrder, 1);
    SELECT Id, PublicId FROM dbo.WN_MembershipPlanFeatures WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlanFeatures_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlanFeatures_Update]
    @Id           INT,
    @FeatureName  NVARCHAR(120) = NULL,
    @FeatureValue NVARCHAR(120) = NULL,
    @SortOrder    SMALLINT      = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlanFeatures SET
        FeatureName  = ISNULL(@FeatureName,  FeatureName),
        FeatureValue = ISNULL(@FeatureValue, FeatureValue),
        SortOrder    = ISNULL(@SortOrder,    SortOrder)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlans_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlans SET IsActive = 0, UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_GetAll]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 14: MEMBERSHIP PLANS
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlans_GetAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        mp.Id, mp.PublicId, mp.Name, mp.Description,
        mp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        mp.Price, mp.IncludesHours, mp.CurrencyCode, mp.IsActive, mp.CreatedOn
    FROM dbo.WN_MembershipPlans mp WITH (NOLOCK)
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = mp.BillingPeriodId
    WHERE mp.IsActive = 1
    ORDER BY mp.Price ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlans_GetList]
    @Page  INT = 1,
    @Limit INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    SELECT
        mp.Id, mp.PublicId, mp.Name, mp.Description,
        mp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        mp.Price, mp.IncludesHours, mp.CurrencyCode, mp.IsActive, mp.CreatedOn,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_MembershipPlans mp WITH (NOLOCK)
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = mp.BillingPeriodId
    WHERE mp.IsActive = 1
    ORDER BY mp.Price ASC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_GetSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlans_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        mp.Id, mp.PublicId, mp.Name, mp.Description,
        mp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        mp.Price, mp.IncludesHours, mp.CurrencyCode, mp.IsActive, mp.CreatedOn,
        (SELECT f.Id, f.PublicId, f.FeatureName, f.FeatureValue, f.SortOrder
         FROM dbo.WN_MembershipPlanFeatures f
         WHERE f.PlanId = mp.Id AND f.IsActive = 1
         ORDER BY f.SortOrder
         FOR JSON PATH) AS Features
    FROM dbo.WN_MembershipPlans mp WITH (NOLOCK)
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = mp.BillingPeriodId
    WHERE mp.Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlans_Insert]
    @Name            NVARCHAR(120),
    @Description     NVARCHAR(MAX)  = NULL,
    @BillingPeriodId TINYINT,
    @Price           DECIMAL(18,4),
    @IncludesHours   INT            = NULL,
    @CurrencyCode    NVARCHAR(10)   = 'PKR',
    @CreatedById     INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_MembershipPlans
        (Name, Description, BillingPeriodId, Price,
         IncludesHours, CurrencyCode, IsActive, CreatedById)
    VALUES
        (@Name, @Description, @BillingPeriodId, @Price,
         @IncludesHours, @CurrencyCode, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_MembershipPlans WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_MembershipPlans_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_MembershipPlans_Update]
    @Id              INT,
    @Name            NVARCHAR(120) = NULL,
    @Description     NVARCHAR(MAX) = NULL,
    @BillingPeriodId TINYINT       = NULL,
    @Price           DECIMAL(18,4) = NULL,
    @IncludesHours   INT           = NULL,
    @CurrencyCode    NVARCHAR(10)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlans SET
        Name            = ISNULL(@Name,            Name),
        Description     = ISNULL(@Description,     Description),
        BillingPeriodId = ISNULL(@BillingPeriodId, BillingPeriodId),
        Price           = ISNULL(@Price,           Price),
        IncludesHours   = ISNULL(@IncludesHours,   IncludesHours),
        CurrencyCode    = ISNULL(@CurrencyCode,    CurrencyCode),
        UpdatedOn       = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StatusId')
    BEGIN
        SET @sql = N'UPDATE dbo.WN_Memberships SET StatusId = 3, UpdatedOn = SYSUTCDATETIME() WHERE Id = @Id;';
        EXEC sp_executesql @sql, N'@Id INT', @Id;
    END
    ELSE
    BEGIN
        UPDATE dbo.WN_MemberShips_Temp SET Status = 'Cancelled', IsActive = 0 WHERE Id = @Id;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_GetByPlanId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_GetByPlanId]
    @PlanId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
        SET @sql = N'
        SELECT m.Id, m.IdGUID AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Price AS PlanPrice,
            m.StartOn, m.EndOn, m.StatusId, m.AutoRenew, m.CreatedOn
        FROM dbo.WN_Memberships m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.PlanId = @PlanId
        ORDER BY m.CreatedOn DESC';
    ELSE
        SET @sql = N'
        SELECT m.Id, NULL AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Price AS PlanPrice,
            m.StartDate AS StartOn, m.EndDate AS EndOn,
            CASE m.Status WHEN ''Active'' THEN 1 WHEN ''Cancelled'' THEN 3 ELSE 2 END AS StatusId,
            CAST(0 AS BIT) AS AutoRenew, m.CreatedAt AS CreatedOn
        FROM dbo.WN_MemberShips_Temp m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.PlanId = @PlanId AND m.IsActive = 1
        ORDER BY m.CreatedAt DESC';

    EXEC sp_executesql @sql, N'@PlanId INT', @PlanId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 16: MEMBERSHIPS
-- -- NOTE: WN_Memberships is the new table created by Schema_Part0_Migration.sql
-- -- Old table was WN_MemberShips_Temp with columns:
-- -- Id, UserId(INT), PlanId, StartDate, EndDate, Status(NVARCHAR), IsActive, CreatedAt
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
        SET @sql = N'
        SELECT m.Id, m.IdGUID AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName,
            bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
            mp.Price AS PlanPrice,
            m.StartOn, m.EndOn, m.StatusId,
            m.AutoRenew, m.Notes, m.CreatedOn,
            COUNT(*) OVER () AS TotalCount
        FROM dbo.WN_Memberships m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = mp.BillingPeriodId
        WHERE (@Search IS NULL OR @Search = ''''
               OR u.Name  LIKE ''%''+@Search+''%''
               OR u.Email LIKE ''%''+@Search+''%''
               OR mp.Name LIKE ''%''+@Search+''%'')
        ORDER BY m.CreatedOn DESC
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY';
    ELSE
        SET @sql = N'
        SELECT m.Id, NULL AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName,
            NULL AS BillingPeriodCode, NULL AS BillingPeriodLabel,
            mp.Price AS PlanPrice,
            m.StartDate AS StartOn, m.EndDate AS EndOn,
            CASE m.Status WHEN ''Active'' THEN 1 WHEN ''Cancelled'' THEN 3 ELSE 2 END AS StatusId,
            CAST(0 AS BIT) AS AutoRenew, NULL AS Notes, m.CreatedAt AS CreatedOn,
            COUNT(*) OVER () AS TotalCount
        FROM dbo.WN_MemberShips_Temp m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.IsActive = 1
          AND (@Search IS NULL OR @Search = ''''
               OR u.Name  LIKE ''%''+@Search+''%''
               OR u.Email LIKE ''%''+@Search+''%''
               OR mp.Name LIKE ''%''+@Search+''%'')
        ORDER BY m.CreatedAt DESC
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY';

    EXEC sp_executesql @sql,
        N'@Search NVARCHAR(255), @Offset INT, @Limit INT',
        @Search, @Offset, @Limit;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_GetSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
        SET @sql = N'
        SELECT m.Id, m.IdGUID AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Description AS PlanDescription,
            bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
            mp.Price AS PlanPrice, mp.IncludesHours,
            m.StartOn, m.EndOn, m.StatusId, m.AutoRenew, m.Notes, m.CreatedOn,
            (SELECT f.FeatureName, f.FeatureValue, f.SortOrder
             FROM dbo.WN_MembershipPlanFeatures f
             WHERE f.PlanId = mp.Id AND f.IsActive = 1
             ORDER BY f.SortOrder FOR JSON PATH) AS PlanFeatures
        FROM dbo.WN_Memberships m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = mp.BillingPeriodId
        WHERE m.Id = @Id';
    ELSE
        SET @sql = N'
        SELECT m.Id, NULL AS PublicId,
            m.UserId, u.Name AS UserName, u.Email AS UserEmail,
            m.PlanId, mp.Name AS PlanName, mp.Description AS PlanDescription,
            NULL AS BillingPeriodCode, NULL AS BillingPeriodLabel,
            mp.Price AS PlanPrice, mp.IncludesHours,
            m.StartDate AS StartOn, m.EndDate AS EndOn,
            CASE m.Status WHEN ''Active'' THEN 1 WHEN ''Cancelled'' THEN 3 ELSE 2 END AS StatusId,
            CAST(0 AS BIT) AS AutoRenew, NULL AS Notes, m.CreatedAt AS CreatedOn,
            NULL AS PlanFeatures
        FROM dbo.WN_MemberShips_Temp m
        JOIN dbo.WN_Users u ON u.Id = m.UserId
        JOIN dbo.WN_MembershipPlans mp ON mp.Id = m.PlanId
        WHERE m.Id = @Id';

    EXEC sp_executesql @sql, N'@Id INT', @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_Insert]
    @UserId      INT,
    @PlanId      INT,
    @StartOn     DATETIME2,
    @EndOn       DATETIME2     = NULL,
    @AutoRenew   BIT           = 0,
    @Notes       NVARCHAR(500) = NULL,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StartOn')
    BEGIN
        SET @sql = N'
        INSERT INTO dbo.WN_Memberships
            (UserId, PlanId, StartOn, EndOn, StatusId, AutoRenew, Notes, CreatedById)
        VALUES
            (@UserId, @PlanId, @StartOn, @EndOn, 1, @AutoRenew, @Notes, @CreatedById);
        SELECT Id, PublicId FROM dbo.WN_Memberships WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT, @PlanId INT, @StartOn DATETIME2, @EndOn DATETIME2, @AutoRenew BIT, @Notes NVARCHAR(500), @CreatedById INT',
            @UserId, @PlanId, @StartOn, @EndOn, @AutoRenew, @Notes, @CreatedById;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_MemberShips_Temp
            (UserId, PlanId, StartDate, EndDate, Status, IsActive)
        VALUES
            (@UserId, @PlanId, @StartOn, @EndOn, 'Active', 1);
        SELECT SCOPE_IDENTITY() AS Id, NULL AS PublicId;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Memberships_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Memberships_UpdateStatus]
    @Id          INT,
    @StatusId    TINYINT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Memberships') AND name = 'StatusId')
    BEGIN
        SET @sql = N'UPDATE dbo.WN_Memberships SET StatusId = @StatusId, UpdatedOn = SYSUTCDATETIME() WHERE Id = @Id;';
        EXEC sp_executesql @sql, N'@StatusId TINYINT, @Id INT', @StatusId, @Id;
    END
    ELSE
    BEGIN
        UPDATE dbo.WN_MemberShips_Temp SET
            Status   = CASE @StatusId WHEN 1 THEN 'Active' WHEN 3 THEN 'Cancelled' ELSE 'Expired' END,
            IsActive = CASE WHEN @StatusId = 1 THEN 1 ELSE 0 END
        WHERE Id = @Id;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET StatusId = 5, UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GenerateVoucher]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_GenerateVoucher]
    @UserId      INT,
    @BookingId   INT           = NULL,
    @Amount      DECIMAL(18,4),
    @ExpiresOn   DATETIME2,
    @CreatedById INT           = NULL,
    @UserEmail   NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Today DATE = CAST(SYSUTCDATETIME() AS DATE);
    DECLARE @SeqNum INT;
    DECLARE @VoucherRef NVARCHAR(50);

    UPDATE dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
    SET LastNumber = LastNumber + 1 WHERE CounterDate = @Today;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.WN_ChallanCounter (CounterDate, LastNumber)
        SELECT @Today, 1 WHERE NOT EXISTS (
            SELECT 1 FROM dbo.WN_ChallanCounter WITH (UPDLOCK, HOLDLOCK)
            WHERE CounterDate = @Today
        );

    SELECT @SeqNum = LastNumber FROM dbo.WN_ChallanCounter WITH (NOLOCK)
    WHERE CounterDate = @Today;

    SET @VoucherRef = 'VCH-' + CONVERT(NVARCHAR(8), @Today, 112) + '-'
                    + RIGHT('000000' + CAST(@SeqNum AS NVARCHAR(6)), 6);

    DECLARE @PaymentId INT;
    DECLARE @PaymentGuid UNIQUEIDENTIFIER;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, StatusId, ExpiresOn, CreatedById)
        VALUES (@UserId,@BookingId,5,@Amt,''PKR'',@Ref,1,@Exp,@CBy);
        SET @OutId = SCOPE_IDENTITY();
        SELECT @OutGuid = PublicId FROM dbo.WN_Payments WHERE Id = @OutId;';
        
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@Amt DECIMAL(18,4),@Ref NVARCHAR(50),@Exp DATETIME2,@CBy INT, @OutId INT OUTPUT, @OutGuid UNIQUEIDENTIFIER OUTPUT',
            @UserId,@BookingId,@Amount,@VoucherRef,@ExpiresOn,@CreatedById, @PaymentId OUTPUT, @PaymentGuid OUTPUT;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, StatusId, ExpiresOn, CreatedAt)
        VALUES (@UG,@BG,5,@Amt,''PKR'',@Ref,1,@Exp,SYSUTCDATETIME());
        SET @OutId = SCOPE_IDENTITY();
        SELECT @OutGuid = PublicId FROM dbo.WN_Payments WHERE Id = @OutId;';
        
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@Amt DECIMAL(18,4),@Ref NVARCHAR(50),@Exp DATETIME2, @OutId INT OUTPUT, @OutGuid UNIQUEIDENTIFIER OUTPUT',
            @UG,@BG,@Amount,@VoucherRef,@ExpiresOn, @PaymentId OUTPUT, @PaymentGuid OUTPUT;
    END

    -- Return a single SELECT containing all voucher information
    SELECT 
        @PaymentId AS Id, 
        @PaymentGuid AS PublicId, 
        @VoucherRef AS VoucherRef,
        @VoucherRef AS VoucherNumber,
        @ExpiresOn AS ExpiryDate,
        @Amount AS Amount,
        1 AS IsSuccessful,
        N'Voucher generated successfully' AS Message;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetByMembershipId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_GetByMembershipId]
    @MembershipId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        p.Id, p.IdGUID AS PublicId, p.IdGUID, p.MembershipId, p.Amount, p.CurrencyCode,
        p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
        p.StatusId, ps.Code AS PaymentStatusCode, ps.Label AS PaymentStatusLabel,
        p.TransactionRef, p.GatewayRef, p.PaidOn, p.CreatedOn, p.ExpiresOn, p.Notes
    FROM dbo.WN_Payments p WITH (NOLOCK)
    LEFT JOIN dbo.WN_PaymentMethods  pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
    LEFT JOIN dbo.WN_BookingStatuses ps WITH (NOLOCK) ON ps.Id = p.StatusId
    WHERE p.MembershipId = @MembershipId
    ORDER BY p.CreatedOn DESC;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_GetList]
    @Page   INT           = 1,
    @Limit  INT           = 20,
    @Search NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT p.Id, p.IdGUID AS PublicId, p.IdGUID,
        p.InvoiceId, p.BookingIdInt AS BookingId, p.MembershipId,
        u.Id AS UserId, u.Name AS UserName, u.Email AS UserEmail,
        p.PaymentMethodId, ISNULL(pm.Code, 'Bank') AS PaymentMethodCode, ISNULL(pm.Label, 'Bank Transfer / Challan') AS PaymentMethodLabel,
        p.Amount, ISNULL(p.CurrencyCode, 'PKR') AS CurrencyCode, p.TransactionRef, p.GatewayRef,
        ISNULL(p.StatusId, 1) AS StatusId,
        CASE WHEN ISNULL(p.StatusId, 1) = 2 THEN 'Paid' WHEN ISNULL(p.StatusId, 1) = 3 THEN 'Partial' WHEN ISNULL(p.StatusId, 1) = 4 THEN 'Overdue' WHEN ISNULL(p.StatusId, 1) = 5 THEN 'Cancelled' ELSE 'Pending' END AS PaymentStatus,
        p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
        bs.SpaceNumber,
        bs.ContractStartDate,
        bs.ContractEndDate,
        bs.BillingPeriod,
        bs.BillingPeriodMonths,
        bs.MonthlyRent,
        bs.CurrentCycleAmount,
        bs.TotalContractAmount,
        bs.TotalPaidAmount,
        bs.BalanceLeft,
        bs.NextBillDueDate,
        bs.NextBillingDate,
        bs.SecurityDeposit,
        ch.ChallanNumber,
        ch.ChallanValidUntil,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Payments p WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserIdInt
    LEFT JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
    LEFT JOIN dbo.WN_vw_BookingSummary bs ON bs.BookingId = p.BookingIdInt
    OUTER APPLY (
        SELECT TOP 1 ChallanNumber, ValidUntil AS ChallanValidUntil
        FROM dbo.WN_Challans WHERE BookingId = bs.BookingId
        ORDER BY CreatedOn DESC
    ) ch
    WHERE (@Search IS NULL OR @Search = ''
        OR u.Name LIKE '%'+@Search+'%'
        OR u.Email LIKE '%'+@Search+'%'
        OR p.TransactionRef LIKE '%'+@Search+'%')
    ORDER BY p.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetMyList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_GetMyList]
    @UserEmail NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.WN_Payments')
          AND name = 'UserId' AND system_type_id = 56
    )
        SET @sql = N'
        SELECT p.Id, p.IdGUID AS PublicId, bs.BookingId, p.MembershipId,
            p.PaymentMethodId, ISNULL(pm.Code, ''Bank'') AS PaymentMethodCode, ISNULL(pm.Label, ''Bank Transfer / Challan'') AS PaymentMethodLabel,
            p.Amount, ISNULL(p.CurrencyCode, ''PKR'') AS CurrencyCode, p.TransactionRef, ISNULL(p.StatusId, 1) AS StatusId,
            CASE WHEN ISNULL(p.StatusId, 1) = 2 THEN ''Paid'' WHEN ISNULL(p.StatusId, 1) = 3 THEN ''Partial'' WHEN ISNULL(p.StatusId, 1) = 4 THEN ''Overdue'' WHEN ISNULL(p.StatusId, 1) = 5 THEN ''Cancelled'' ELSE ''Pending'' END AS PaymentStatus,
            p.ExpiresOn, p.PaidOn, p.CreatedOn,
            b.StartOn, b.EndOn,
            s.Name AS SpaceName, s.Code AS SpaceCode,
            ch.ChallanNumber, ch.ValidUntil AS ChallanValidUntil,
            bs.SpaceNumber,
            bs.ContractStartDate,
            bs.ContractEndDate,
            bs.BillingPeriod,
            bs.BillingPeriodMonths,
            bs.MonthlyRent,
            bs.CurrentCycleAmount,
            bs.TotalContractAmount,
            bs.TotalPaidAmount,
            bs.BalanceLeft,
            bs.NextBillDueDate,
            bs.NextBillingDate,
            bs.SecurityDeposit
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
        LEFT JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON (b.Id = p.BookingIdInt OR b.IdGUID = p.BookingId)
        LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        LEFT JOIN dbo.WN_vw_BookingSummary bs ON bs.BookingId = b.Id
        OUTER APPLY (
            SELECT TOP 1 ChallanNumber, ValidUntil
            FROM dbo.WN_Challans WHERE BookingId = b.Id
            ORDER BY CreatedOn DESC
        ) ch
        WHERE u.Email = @UserEmail AND ISNULL(p.StatusId, 1) <> 5
        ORDER BY p.CreatedOn DESC';
    ELSE
        SET @sql = N'
        SELECT p.Id, p.IdGUID AS PublicId, bs.BookingId, p.MembershipId,
            p.PaymentMethodId, ISNULL(pm.Code, ''Bank'') AS PaymentMethodCode, ISNULL(pm.Label, ''Bank Transfer / Challan'') AS PaymentMethodLabel,
            p.Amount, ISNULL(p.CurrencyCode, ''PKR'') AS CurrencyCode, p.TransactionRef, ISNULL(p.StatusId, 1) AS StatusId,
            CASE WHEN ISNULL(p.StatusId, 1) = 2 THEN ''Paid'' WHEN ISNULL(p.StatusId, 1) = 3 THEN ''Partial'' WHEN ISNULL(p.StatusId, 1) = 4 THEN ''Overdue'' WHEN ISNULL(p.StatusId, 1) = 5 THEN ''Cancelled'' ELSE ''Pending'' END AS PaymentStatus,
            p.ExpiresOn, p.PaidOn, p.CreatedOn,
            b.StartOn, b.EndOn,
            s.Name AS SpaceName, s.Code AS SpaceCode,
            ch.ChallanNumber, ch.ValidUntil AS ChallanValidUntil,
            bs.SpaceNumber,
            bs.ContractStartDate,
            bs.ContractEndDate,
            bs.BillingPeriod,
            bs.BillingPeriodMonths,
            bs.MonthlyRent,
            bs.CurrentCycleAmount,
            bs.TotalContractAmount,
            bs.TotalPaidAmount,
            bs.BalanceLeft,
            bs.NextBillDueDate,
            bs.NextBillingDate,
            bs.SecurityDeposit
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.IdGUID = p.UserId
        LEFT JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON (b.Id = p.BookingIdInt OR b.IdGUID = p.BookingId)
        LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
        LEFT JOIN dbo.WN_vw_BookingSummary bs ON bs.BookingId = b.Id
        OUTER APPLY (
            SELECT TOP 1 ChallanNumber, ValidUntil
            FROM dbo.WN_Challans WHERE BookingId = b.Id
            ORDER BY CreatedOn DESC
        ) ch
        WHERE u.Email = @UserEmail AND ISNULL(p.StatusId, 1) <> 5
        ORDER BY p.CreatedOn DESC';

    EXEC sp_executesql @sql, N'@UserEmail NVARCHAR(256)', @UserEmail;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_GetSummary]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_GetSummary]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
        SET @sql = N'
        SELECT p.Id, p.IdGUID AS PublicId,
            p.InvoiceId, p.BookingId, p.MembershipId,
            p.UserId, u.Name AS UserName, u.Email AS UserEmail,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.GatewayRef,
            p.StatusId, p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
            (SELECT pl.Id, ct.Code AS ChargeTypeCode, ct.Label AS ChargeTypeLabel,
                    pl.Amount, pl.AccountId, pl.Notes
             FROM dbo.WN_PaymentLines pl
             JOIN dbo.WN_ChargeTypes ct ON ct.Id = pl.ChargeTypeId
             WHERE pl.PaymentId = p.Id
             FOR JSON PATH) AS PaymentLines
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE p.Id = @Id';
    ELSE
        SET @sql = N'
        SELECT p.Id, p.IdGUID AS PublicId,
            NULL AS InvoiceId, NULL AS BookingId, NULL AS MembershipId,
            u.Id AS UserId, u.Name AS UserName, u.Email AS UserEmail,
            p.PaymentMethodId, pm.Code AS PaymentMethodCode, pm.Label AS PaymentMethodLabel,
            p.Amount, p.CurrencyCode, p.TransactionRef, p.GatewayRef,
            p.StatusId, p.ExpiresOn, p.PaidOn, p.Notes, p.CreatedOn,
            NULL AS PaymentLines
        FROM dbo.WN_Payments p WITH (NOLOCK)
        JOIN dbo.WN_Users u WITH (NOLOCK) ON u.IdGUID = p.UserId
        JOIN dbo.WN_PaymentMethods pm WITH (NOLOCK) ON pm.Id = p.PaymentMethodId
        WHERE p.Id = @Id';
    EXEC sp_executesql @sql, N'@Id INT', @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_InitiatePayFast]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_InitiatePayFast]
    @UserId      INT,
    @BookingId   INT           = NULL,
    @Amount      DECIMAL(18,4),
    @GatewayRef  NVARCHAR(200) = NULL,
    @CreatedById INT           = NULL,
    @UserEmail   NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode, GatewayRef, StatusId, CreatedById)
        VALUES (@UserId,@BookingId,4,@Amt,''PKR'',@GRef,1,@CBy);
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@Amt DECIMAL(18,4),@GRef NVARCHAR(200),@CBy INT',
            @UserId,@BookingId,@Amount,@GatewayRef,@CreatedById;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode, GatewayRef, StatusId, CreatedAt)
        VALUES (@UG,@BG,4,@Amt,''PKR'',@GRef,1,SYSUTCDATETIME());
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@Amt DECIMAL(18,4),@GRef NVARCHAR(200)',
            @UG,@BG,@Amount,@GatewayRef;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_Insert]
    @UserId          INT,
    @BookingId       INT           = NULL,
    @MembershipId    INT           = NULL,
    @InvoiceId       INT           = NULL,
    @PaymentMethodId TINYINT,
    @Amount          DECIMAL(18,4),
    @CurrencyCode    NVARCHAR(10)  = 'PKR',
    @TransactionRef  NVARCHAR(200) = NULL,
    @ExpiresOn       DATETIME2     = NULL,
    @Notes           NVARCHAR(500) = NULL,
    @CreatedById     INT           = NULL,
    @UserEmail       NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, MembershipId, InvoiceId, PaymentMethodId,
             Amount, CurrencyCode, TransactionRef, StatusId, ExpiresOn, Notes, CreatedById)
        VALUES (@UserId,@BookingId,@MembershipId,@InvoiceId,@PM,@Amt,@Cur,@Ref,1,@Exp,@Notes,@CBy);
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@MembershipId INT,@InvoiceId INT,@PM TINYINT,@Amt DECIMAL(18,4),@Cur NVARCHAR(10),@Ref NVARCHAR(200),@Exp DATETIME2,@Notes NVARCHAR(500),@CBy INT',
            @UserId,@BookingId,@MembershipId,@InvoiceId,@PaymentMethodId,@Amount,@CurrencyCode,@TransactionRef,@ExpiresOn,@Notes,@CreatedById;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, StatusId, ExpiresOn, CreatedAt)
        VALUES (@UG,@BG,@PM,@Amt,@Cur,@Ref,1,@Exp,SYSUTCDATETIME());
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@PM TINYINT,@Amt DECIMAL(18,4),@Cur NVARCHAR(10),@Ref NVARCHAR(200),@Exp DATETIME2',
            @UG,@BG,@PaymentMethodId,@Amount,@CurrencyCode,@TransactionRef,@ExpiresOn;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_InsertCard]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_InsertCard]
    @UserId         INT,
    @BookingId      INT           = NULL,
    @MembershipId   INT           = NULL,
    @Amount         DECIMAL(18,4),
    @TransactionRef NVARCHAR(200) = NULL,
    @GatewayRef     NVARCHAR(200) = NULL,
    @CreatedById    INT           = NULL,
    @UserEmail      NVARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_Payments') AND name = 'UserId' AND system_type_id = 56)
    BEGIN
        SET @sql = N'INSERT INTO dbo.WN_Payments
            (UserId, BookingId, MembershipId, PaymentMethodId,
             Amount, CurrencyCode, TransactionRef, GatewayRef, StatusId, PaidOn, CreatedById)
        VALUES (@UserId,@BookingId,@MembershipId,2,@Amt,''PKR'',@Ref,@GRef,2,SYSUTCDATETIME(),@CBy);
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql,
            N'@UserId INT,@BookingId INT,@MembershipId INT,@Amt DECIMAL(18,4),@Ref NVARCHAR(200),@GRef NVARCHAR(200),@CBy INT',
            @UserId,@BookingId,@MembershipId,@Amount,@TransactionRef,@GatewayRef,@CreatedById;
    END
    ELSE
    BEGIN
        DECLARE @UG UNIQUEIDENTIFIER, @BG UNIQUEIDENTIFIER;
        SELECT @UG = IdGUID FROM dbo.WN_Users    WHERE Id = @UserId;
        SELECT @BG = IdGUID FROM dbo.WN_Bookings WHERE Id = @BookingId;
        DECLARE @sql2 NVARCHAR(MAX) = N'
        INSERT INTO dbo.WN_Payments
            (UserId, BookingId, PaymentMethodId, Amount, CurrencyCode,
             TransactionRef, GatewayRef, StatusId, PaidOn, CreatedAt)
        VALUES (@UG,@BG,2,@Amt,''PKR'',@Ref,@GRef,2,SYSUTCDATETIME(),SYSUTCDATETIME());
        SELECT Id, PublicId FROM dbo.WN_Payments WHERE Id = SCOPE_IDENTITY();';
        EXEC sp_executesql @sql2,
            N'@UG UNIQUEIDENTIFIER,@BG UNIQUEIDENTIFIER,@Amt DECIMAL(18,4),@Ref NVARCHAR(200),@GRef NVARCHAR(200)',
            @UG,@BG,@Amount,@TransactionRef,@GatewayRef;
    END
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_UpdateStatus]
    @Id          INT,
    @StatusId    TINYINT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET
        StatusId  = @StatusId,
        PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
        UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatusByGuid]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_UpdateStatusByGuid]
    @IdGUID UNIQUEIDENTIFIER,
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
            DECLARE @StatusId TINYINT = ISNULL(TRY_CAST(@Status AS TINYINT), 1);

            UPDATE dbo.WN_Payments
            SET    StatusId  = @StatusId,
                   PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
                   UpdatedOn = SYSUTCDATETIME()
            WHERE  IdGUID = @IdGUID;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatusByPublicId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_UpdateStatusByPublicId]
    @PublicId UNIQUEIDENTIFIER,
    @StatusId TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET
        StatusId  = @StatusId,
        PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
        UpdatedOn = SYSUTCDATETIME()
    WHERE IdGUID = @PublicId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Payments_UpdateStatusByRef]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Payments_UpdateStatusByRef]
    @TransactionRef NVARCHAR(200),
    @StatusId       TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Payments SET
        StatusId  = @StatusId,
        PaidOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE PaidOn END,
        UpdatedOn = SYSUTCDATETIME()
    WHERE TransactionRef = @TransactionRef;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_PlanFeatures_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlanFeatures SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_GetByPlan]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 15: PLAN FEATURES
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_PlanFeatures_GetByPlan]
    @PlanId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, PublicId, PlanId, FeatureName, FeatureValue, SortOrder, IsActive
    FROM dbo.WN_MembershipPlanFeatures WITH (NOLOCK)
    WHERE PlanId = @PlanId AND IsActive = 1
    ORDER BY SortOrder;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_PlanFeatures_Insert]
    @PlanId       INT,
    @FeatureName  NVARCHAR(120),
    @FeatureValue NVARCHAR(120) = NULL,
    @SortOrder    SMALLINT      = 0
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_MembershipPlanFeatures
        (PlanId, FeatureName, FeatureValue, SortOrder, IsActive)
    VALUES
        (@PlanId, @FeatureName, @FeatureValue, @SortOrder, 1);
    SELECT Id, PublicId FROM dbo.WN_MembershipPlanFeatures WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PlanFeatures_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_PlanFeatures_Update]
    @Id           INT,
    @FeatureName  NVARCHAR(120) = NULL,
    @FeatureValue NVARCHAR(120) = NULL,
    @SortOrder    SMALLINT      = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_MembershipPlanFeatures SET
        FeatureName  = ISNULL(@FeatureName,  FeatureName),
        FeatureValue = ISNULL(@FeatureValue, FeatureValue),
        SortOrder    = ISNULL(@SortOrder,    SortOrder)
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_PricingPlans_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_PricingPlans_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.IdGUID        AS IdGuid,
           p.Id            AS Id,
           p.Name          AS Name,
           ISNULL(p.Price, 0) AS Price,
           p.BillingCycle  AS BillingCycle,
           p.IncludesHours AS IncludesHours,
           p.IsActive      AS IsActive,
           p.Description   AS Description,
           f.FeatureName   AS FeatureName
    FROM dbo.WN_PricingPlans p WITH (NOLOCK)
    LEFT JOIN dbo.WN_PlanFeatures f WITH (NOLOCK) ON p.Id = f.PlanId AND f.Status != 0
    WHERE p.IsActive = 1;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_QuotationDetails_GetByQuotation]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3. Get quotation details (line items) by quotation Id
CREATE OR ALTER PROCEDURE [dbo].[WN_QuotationDetails_GetByQuotation]
    @QuotationId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, QuotationId, FeeType, Description, Quantity, UnitPrice, Amount
    FROM dbo.WN_QuotationDetails
    WHERE QuotationId = @QuotationId
    ORDER BY Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Quotations_ConvertToBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 4. Update dbo.WN_Quotations_ConvertToBooking
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Quotations_ConvertToBooking]
    @QuotationId INT,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @CustomerId INT, @SpaceId INT, @StartOn DATETIME2, @EndOn DATETIME2, 
                @Subtotal DECIMAL(18,2), @DiscountPct DECIMAL(5,2), @DiscountType NVARCHAR(20), 
                @DiscountVal DECIMAL(18,2), @SecDep DECIMAL(18,2), @FloorId INT, @BPM INT, 
                @SupportChargesId TINYINT, @AdvRentMonths INT, @SecDepMonths INT,
                @Capacity INT, @PerSeatPrice DECIMAL(18,2), @MonthlyPrice DECIMAL(18,2),
                @OfferingType NVARCHAR(50);

        SELECT 
            @CustomerId = CustomerId,
            @SpaceId = SpaceId,
            @StartOn = StartDateTime,
            @EndOn = EndDateTime,
            @Subtotal = SubtotalAmount,
            @DiscountPct = ISNULL(DiscountPercentage, 0.0),
            @DiscountType = ISNULL(DiscountType, 'Percentage'),
            @DiscountVal = ISNULL(DiscountAmount, 0.0),
            @SecDep = SecurityDeposit,
            @FloorId = FloorId,
            @BPM = ISNULL(BillingPeriodMonths, 3),
            @SupportChargesId = ISNULL(SupportChargesId, 4),
            @AdvRentMonths = ISNULL(AdvanceRentMonths, 1),
            @SecDepMonths = ISNULL(SecurityDepositMonths, 0),
            @Capacity = Capacity,
            @PerSeatPrice = PerSeatBasePrice,
            @MonthlyPrice = MonthlyBasePrice,
            @OfferingType = OfferingType
        FROM dbo.WN_Quotations
        WHERE Id = @QuotationId;

        IF @CustomerId IS NULL
            RAISERROR('Quotation not found.', 16, 1);

        -- Map quotation offering type to booking shift type
        DECLARE @ShiftType NVARCHAR(20) = CASE 
            WHEN @OfferingType IN ('2', 'morning', 'Morning', 'Morning Shift', 'shift_morning') THEN 'morning'
            WHEN @OfferingType IN ('3', 'evening', 'Evening', 'Evening Shift', 'night', 'shift_evening') THEN 'evening'
            ELSE '24_7'
        END;

        DECLARE @OverrideUnitPrice DECIMAL(18,2) = NULL;

        IF @PerSeatPrice IS NOT NULL AND @PerSeatPrice > 0
            SET @OverrideUnitPrice = @PerSeatPrice;
        ELSE IF @MonthlyPrice IS NOT NULL AND @MonthlyPrice > 0
            SET @OverrideUnitPrice = CASE WHEN ISNULL(@Capacity, 1) > 0 THEN @MonthlyPrice / ISNULL(@Capacity, 1) ELSE @MonthlyPrice END;
        ELSE IF @Subtotal > 0 AND ISNULL(@AdvRentMonths, 1) > 0
            SET @OverrideUnitPrice = CASE 
                WHEN ISNULL(@Capacity, 1) > 0 THEN (@Subtotal / ISNULL(@AdvRentMonths, 1)) / ISNULL(@Capacity, 1) 
                ELSE @Subtotal / ISNULL(@AdvRentMonths, 1) 
            END;

        DECLARE @UserId INT, @CustomerEmail NVARCHAR(255), @CustomerFirstName NVARCHAR(100),
                @CustomerLastName NVARCHAR(100), @CustomerPhone NVARCHAR(50), @CustomerCnic NVARCHAR(50),
                @CustomerAddress NVARCHAR(500), @CustomerCityId INT, @CustomerNotes NVARCHAR(1000),
                @CustomerCode NVARCHAR(20);

        SELECT TOP 1 
            @UserId = UserId,
            @CustomerEmail = Email,
            @CustomerFirstName = FirstName,
            @CustomerLastName = LastName,
            @CustomerPhone = PhoneNumber,
            @CustomerCnic = CnicOrPassport,
            @CustomerAddress = Address,
            @CustomerCityId = CityId,
            @CustomerNotes = Notes,
            @CustomerCode = Code
        FROM dbo.WN_Customers
        WHERE Id = @CustomerId;

        IF @UserId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.WN_Users WHERE Id = @UserId)
        BEGIN
            SET @UserId = NULL;
        END

        IF @UserId IS NULL AND @CustomerEmail IS NOT NULL AND @CustomerEmail <> ''
        BEGIN
            SELECT TOP 1 @UserId = Id FROM dbo.WN_Users WHERE Email = @CustomerEmail;
        END

        DECLARE @CustFullName NVARCHAR(200) = RTRIM(LTRIM(ISNULL(@CustomerFirstName, '') + ' ' + ISNULL(@CustomerLastName, '')));
        IF @CustFullName = '' SET @CustFullName = ISNULL(@CustomerFirstName, 'Customer');

        DECLARE @MaxBookingIdBefore INT;
        SELECT @MaxBookingIdBefore = ISNULL(MAX(Id), 0) FROM dbo.WN_Bookings;

        EXEC dbo.WN_Bookings_Insert
            @UserId = @UserId,
            @SpaceId = @SpaceId,
            @PricingId = 0,
            @StartOn = @StartOn,
            @EndOn = @EndOn,
            @Notes = 'Converted from Quotation',
            @CreatedById = @CreatedById,
            @UserEmail = @CustomerEmail,
            @CustomerEmail = @CustomerEmail,
            @CustomerFirstName = @CustFullName,
            @CustomerLastName = @CustomerLastName,
            @CustomerPhone = @CustomerPhone,
            @CustomerCnic = @CustomerCnic,
            @CustomerAddress = @CustomerAddress,
            @CustomerCityId = @CustomerCityId,
            @CustomerNotes = @CustomerNotes,
            @DiscountPercentage = @DiscountPct,
            @DiscountAmount = @DiscountVal,
            @DiscountType = @DiscountType,
            @BillingPeriodMonths = @BPM,
            @SecurityDepositMonths = @SecDepMonths,
            @AdvanceRentMonths = @AdvRentMonths,
            @SupportChargesId = @SupportChargesId,
            @Capacity = @Capacity,
            @OverrideSubtotal = @Subtotal,
            @OverrideUnitPrice = @OverrideUnitPrice,
            @CustomerCode = @CustomerCode,
            @ShiftType = @ShiftType;

        DECLARE @NewBookingId INT = NULL;
        DECLARE @NewBookingPublicId UNIQUEIDENTIFIER = NULL;

        SELECT TOP 1 
            @NewBookingId = Id,
            @NewBookingPublicId = IdGUID
        FROM dbo.WN_Bookings
        WHERE Id > @MaxBookingIdBefore
          AND SpaceId = @SpaceId
          AND BookingDate >= DATEADD(MINUTE, -1, SYSUTCDATETIME())
        ORDER BY Id DESC;

        IF @NewBookingId IS NULL
        BEGIN
            SELECT TOP 1 
                @NewBookingId = Id,
                @NewBookingPublicId = IdGUID
            FROM dbo.WN_Bookings
            WHERE SpaceId = @SpaceId
              AND BookingDate >= DATEADD(MINUTE, -1, SYSUTCDATETIME())
            ORDER BY Id DESC;
        END

        UPDATE dbo.WN_Quotations
        SET Status = 'Converted', 
            BookingId = @NewBookingId,
            UpdatedDate = SYSUTCDATETIME(),
            UpdatedById = @CreatedById
        WHERE Id = @QuotationId;

        -- Ensure Security Deposit record is inserted if not already handled by WN_Bookings_Insert
        IF (@SecDep > 0 OR @SecDepMonths > 0) AND @NewBookingId IS NOT NULL
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.WN_SecurityDeposits WITH (UPDLOCK) WHERE BookingId = @NewBookingId)
            BEGIN
                DECLARE @DepositAccId INT = NULL;
                DECLARE @ARAccId INT = NULL;
                DECLARE @RentAccId INT = NULL;
                DECLARE @ServAccId INT = NULL;
                DECLARE @TaxAccId INT = NULL;

                SELECT TOP 1 
                    @DepositAccId = COALESCE(s.SecurityReceivedId, st.SecurityReceivedId, s.SecurityRecivedId),
                    @ARAccId      = COALESCE(s.AccountReceivableId, st.AccountReceivableId),
                    @RentAccId    = COALESCE(s.RentAccountId, st.RentAccountId),
                    @ServAccId    = COALESCE(s.ServicesIncomeId, st.ServicesIncomeId),
                    @TaxAccId     = COALESCE(s.SalesTaxId, st.SalesTaxId)
                FROM dbo.WN_Spaces s WITH (NOLOCK)
                LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                WHERE s.Id = @SpaceId;

                IF @DepositAccId IS NULL
                    SELECT TOP 1 @DepositAccId = SecurityReceivedId FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 2;

                INSERT INTO dbo.WN_SecurityDeposits
                    (BookingId, UserId, Amount, AccountId, StatusId, Notes, UpdatedById, SecurityReceivedId,
                     AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, CustomerId, RefId, RefNo)
                VALUES
                    (@NewBookingId, @UserId, ISNULL(@SecDep, 0), @DepositAccId, 1, 'Initial security deposit for booking ' + CAST(@NewBookingId AS NVARCHAR(20)), @CreatedById, @DepositAccId,
                     @ARAccId, @RentAccId, @ServAccId, @TaxAccId, @CustomerCode, NULL, NULL);
            END
        END

        INSERT INTO dbo.WN_QuotationActivities (
            QuotationId, Version, ActivityType, Message, CustomerNote, CreatedByUserId, CreatedDate
        )
        VALUES (
            @QuotationId, 1, 'Converted', 'Quotation converted to booking (ID: ' + CAST(ISNULL(@NewBookingId, 0) AS NVARCHAR(20)) + ')', NULL, @CreatedById, SYSUTCDATETIME()
        );

        COMMIT TRANSACTION;

        SELECT 
            @NewBookingId AS BookingId,
            @NewBookingPublicId AS BookingPublicId,
            'Quotation successfully converted to booking.' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Quotations_GetById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER OFF
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Quotations_GetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        q.Id,
        q.Guid,
        q.QuotationNumber,
        q.QuotationDate,
        q.ValidUntil,
        q.CustomerId,
        RTRIM(LTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS CustomerName,
        c.Company AS CustomerCompany,
        c.Email AS CustomerEmail,
        c.Address AS CustomerAddress,
        q.SpaceId,
        s.Name AS SpaceName,
        s.Code AS SpaceCode,
        st.Name AS SpaceTypeName,
        loc.Name AS LocationName,
        loc.Address AS LocationAddress,
        ISNULL(NULLIF(city.Description, ''), ISNULL(NULLIF(loc.City, ''), 'Islamabad')) AS CityName,
        q.StartDateTime,
        q.EndDateTime,
        q.SubtotalAmount,
        q.DiscountPercentage,
        q.DiscountAmount,
        q.DiscountType,
        q.SecurityDeposit,
        q.TotalAmount,
        q.Remarks,
        q.Status,
        q.Version,
        q.IsActive,
        q.FloorId,
        q.PerSeatBasePrice,
        q.Capacity,
        q.MonthlyBasePrice,
        q.MaxDiscountPercent,
        q.SecurityDepositMonths,
        q.OfferingType,
        ot.Id AS OfferingTypeId,
        ISNULL(ot.Description, q.OfferingType) AS OfferingTypeDescription,
        ISNULL(ot.DiscountCap, 10.00) AS OfferingTypeDiscountCap,
        ISNULL(q.BillingPeriodMonths, 3) AS BillingPeriodMonths,
        CASE 
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 1 THEN '1 Month (Per Month)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 2 THEN '2 Months'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 3 THEN '3 Months (Quarterly)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 6 THEN '6 Months (Semi-Annual)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 12 THEN '12 Months (Annual)'
          ELSE CAST(ISNULL(q.BillingPeriodMonths, 3) AS NVARCHAR(10)) + ' Month(s)'
        END AS BillingPeriodLabel,
        CASE 
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 1 THEN '1 Month (Per Month)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 2 THEN '2 Months'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 3 THEN '3 Months (Quarterly)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 6 THEN '6 Months (Semi-Annual)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 12 THEN '12 Months (Annual)'
          ELSE CAST(ISNULL(q.BillingPeriodMonths, 3) AS NVARCHAR(10)) + ' Month(s)'
        END AS BillingPeriod,
        CAST(
          q.SubtotalAmount / NULLIF(
            CASE 
              WHEN q.StartDateTime IS NOT NULL AND q.EndDateTime IS NOT NULL AND DATEDIFF(month, q.StartDateTime, q.EndDateTime) > 0 
              THEN DATEDIFF(month, q.StartDateTime, q.EndDateTime)
              ELSE 1 
            END, 0) AS DECIMAL(18,2)
        ) AS MonthlyRent,
        CAST(
          (q.SubtotalAmount / NULLIF(
            CASE 
              WHEN q.StartDateTime IS NOT NULL AND q.EndDateTime IS NOT NULL AND DATEDIFF(month, q.StartDateTime, q.EndDateTime) > 0 
              THEN DATEDIFF(month, q.StartDateTime, q.EndDateTime)
              ELSE 1 
            END, 0)) * ISNULL(q.BillingPeriodMonths, 3) * (1.0 - (ISNULL(q.DiscountPercentage, 0.0) / 100.0)) AS DECIMAL(18,2)
        ) AS CurrentCycleAmount,
        CAST(q.TotalAmount AS DECIMAL(18,2)) AS TotalContractAmount
    FROM dbo.WN_Quotations q
    JOIN dbo.WN_Customers c ON c.Id = q.CustomerId
    JOIN dbo.WN_Spaces s ON s.Id = q.SpaceId
    LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations loc ON loc.Id = s.LocationId
    LEFT JOIN dbo.City city ON city.Id = loc.CityId
    LEFT JOIN dbo.WN_OfferingType ot ON (TRY_CAST(q.OfferingType AS INT) = ot.Id OR ot.Description = q.OfferingType)
    WHERE q.Id = @Id;
END;

GO
/****** Object:  StoredProcedure [dbo].[WN_Quotations_GetHistory]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 5. Get quotation history (placeholder Ã¢â‚¬â€ returns empty for now)
CREATE OR ALTER PROCEDURE [dbo].[WN_Quotations_GetHistory]
    @QuotationId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, QuotationId, FeeType, Description, Quantity, UnitPrice, Amount
    FROM dbo.WN_QuotationDetails
    WHERE QuotationId = @QuotationId
    ORDER BY Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Quotations_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER OFF
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Quotations_GetList]
    @Page   INT = 1,
    @Limit  INT = 10,
    @Search NVARCHAR(100) = NULL,
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    DECLARE @Total INT;
    SELECT @Total = COUNT(*)
    FROM dbo.WN_Quotations q
    JOIN dbo.WN_Customers c ON c.Id = q.CustomerId
    JOIN dbo.WN_Spaces s ON s.Id = q.SpaceId
    WHERE (@Search IS NULL OR q.QuotationNumber LIKE '%' + @Search + '%' OR c.FirstName LIKE '%' + @Search + '%' OR c.LastName LIKE '%' + @Search + '%' OR c.Company LIKE '%' + @Search + '%')
      AND (@LocationId IS NULL OR s.LocationId = @LocationId);

    SELECT 
        q.Id,
        q.Guid,
        q.QuotationNumber,
        q.QuotationDate,
        q.ValidUntil,
        q.CustomerId,
        RTRIM(LTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS CustomerName,
        c.Company AS CustomerCompany,
        c.Email AS CustomerEmail,
        c.Address AS CustomerAddress,
        q.SpaceId,
        s.Name AS SpaceName,
        s.Code AS SpaceCode,
        st.Name AS SpaceTypeName,
        loc.Name AS LocationName,
        loc.Address AS LocationAddress,
        ISNULL(NULLIF(city.Description, ''), ISNULL(NULLIF(loc.City, ''), 'Islamabad')) AS CityName,
        q.StartDateTime,
        q.EndDateTime,
        q.SubtotalAmount,
        q.DiscountPercentage,
        q.DiscountAmount,
        q.DiscountType,
        q.SecurityDeposit,
        q.TotalAmount,
        q.Remarks,
        q.Status,
        q.Version,
        q.IsActive,
        q.FloorId,
        q.PerSeatBasePrice,
        q.Capacity,
        q.MonthlyBasePrice,
        q.MaxDiscountPercent,
        q.SecurityDepositMonths,
        q.OfferingType,
        ot.Id AS OfferingTypeId,
        ISNULL(ot.Description, q.OfferingType) AS OfferingTypeDescription,
        ISNULL(ot.DiscountCap, 10.00) AS OfferingTypeDiscountCap,
        ISNULL(q.BillingPeriodMonths, 3) AS BillingPeriodMonths,
        CASE 
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 1 THEN '1 Month (Per Month)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 2 THEN '2 Months'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 3 THEN '3 Months (Quarterly)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 6 THEN '6 Months (Semi-Annual)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 12 THEN '12 Months (Annual)'
          ELSE CAST(ISNULL(q.BillingPeriodMonths, 3) AS NVARCHAR(10)) + ' Month(s)'
        END AS BillingPeriodLabel,
        CASE 
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 1 THEN '1 Month (Per Month)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 2 THEN '2 Months'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 3 THEN '3 Months (Quarterly)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 6 THEN '6 Months (Semi-Annual)'
          WHEN ISNULL(q.BillingPeriodMonths, 3) = 12 THEN '12 Months (Annual)'
          ELSE CAST(ISNULL(q.BillingPeriodMonths, 3) AS NVARCHAR(10)) + ' Month(s)'
        END AS BillingPeriod,
        @Total AS TotalCount
    FROM dbo.WN_Quotations q
    JOIN dbo.WN_Customers c ON c.Id = q.CustomerId
    JOIN dbo.WN_Spaces s ON s.Id = q.SpaceId
    LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
    LEFT JOIN dbo.WN_Locations loc ON loc.Id = s.LocationId
    LEFT JOIN dbo.City city ON city.Id = loc.CityId
    LEFT JOIN dbo.WN_OfferingType ot ON (TRY_CAST(q.OfferingType AS INT) = ot.Id OR ot.Description = q.OfferingType)
    WHERE (@Search IS NULL OR q.QuotationNumber LIKE '%' + @Search + '%' OR c.FirstName LIKE '%' + @Search + '%' OR c.LastName LIKE '%' + @Search + '%' OR c.Company LIKE '%' + @Search + '%')
      AND (@LocationId IS NULL OR s.LocationId = @LocationId)
    ORDER BY q.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;

GO
/****** Object:  StoredProcedure [dbo].[WN_Quotations_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Quotations_Insert]
    @QuotationNumber      NVARCHAR(50),
    @ValidUntil           DATE,
    @CustomerId           INT,
    @SpaceId              INT,
    @StartDateTime        DATETIME2,
    @EndDateTime          DATETIME2,
    @SubtotalAmount       DECIMAL(18,2),
    @DiscountPercentage   DECIMAL(5,2),
    @Remarks              NVARCHAR(500) = NULL,
    @CreatedById          INT           = NULL,
    @DiscountType         NVARCHAR(20)  = 'Percentage',
    @DiscountValue        DECIMAL(18,2) = 0,
    @SecurityDepositOverride DECIMAL(18,2) = NULL,
    @FloorId              INT           = NULL,
    @BillingPeriodMonths  INT           = 3
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @PrevVersion INT = 0;
        SELECT TOP 1 @PrevVersion = Version
        FROM dbo.WN_Quotations WITH (UPDLOCK)
        WHERE CustomerId = @CustomerId AND SpaceId = @SpaceId AND IsActive = 1
        ORDER BY Version DESC;

        UPDATE dbo.WN_Quotations
        SET IsActive = 0, Status = 'Inactive'
        WHERE CustomerId = @CustomerId AND SpaceId = @SpaceId AND IsActive = 1;

        DECLARE @SafeDiscountPct DECIMAL(5,2) = ISNULL(@DiscountPercentage, 0.00);
        DECLARE @DiscountAmount DECIMAL(18,2) = CASE 
            WHEN ISNULL(@DiscountType, '') IN ('Amount', 'Fixed') THEN (CASE WHEN ISNULL(@DiscountValue, 0.00) > @SubtotalAmount THEN @SubtotalAmount ELSE ISNULL(@DiscountValue, 0.00) END)
            ELSE ROUND(@SubtotalAmount * @SafeDiscountPct / 100.00, 2)
        END;
        DECLARE @BaseRent DECIMAL(18,2) = CASE WHEN @SubtotalAmount > @DiscountAmount THEN @SubtotalAmount - @DiscountAmount ELSE 0.00 END;
        DECLARE @NewVersion INT = @PrevVersion + 1;
        DECLARE @SecDeposit DECIMAL(18,2) = ISNULL(@SecurityDepositOverride, 0.00);

        -- Mirror Discount to Security Deposit
        DECLARE @RentDiscountPct DECIMAL(5,2) = CASE 
            WHEN @SafeDiscountPct > 0 THEN @SafeDiscountPct
            WHEN @SubtotalAmount > 0 AND @DiscountAmount > 0 THEN ROUND((@DiscountAmount / @SubtotalAmount) * 100.00, 2)
            ELSE 0.00
        END;
        IF @RentDiscountPct > 0 AND @SecDeposit > 0
            SET @SecDeposit = ROUND(@SecDeposit * (1.0 - (@RentDiscountPct / 100.00)), 2);

        -- Resolve Tax & Service Charge details based on Space Location & Province
        DECLARE @LocationId INT, @ProvinceId INT, @SpaceCategoryCode NVARCHAR(50), @SpaceCapacity INT;
        SELECT 
            @LocationId = s.LocationId,
            @SpaceCategoryCode = sc.Code,
            @SpaceCapacity = ISNULL(s.Capacity, 1)
        FROM dbo.WN_Spaces s
        LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
        LEFT JOIN dbo.WN_SpaceCategories sc ON sc.Id = st.CategoryId
        WHERE s.Id = @SpaceId;

        IF @LocationId IS NOT NULL
            SELECT @ProvinceId = l.ProvinceId FROM dbo.WN_Locations l WHERE l.Id = @LocationId;

        DECLARE @SupportChargesId TINYINT = 4; -- Service Fee
        DECLARE @TaxApplicable BIT = 0;
        DECLARE @AppliedChargePercentage DECIMAL(5,2) = 10.00;
        DECLARE @AppliedTaxPercentage DECIMAL(5,2) = 16.00;
        DECLARE @SupportChargeAmount DECIMAL(18,2) = 0.00;
        DECLARE @TaxAmount DECIMAL(18,2) = 0.00;

        IF @ProvinceId IS NOT NULL
        BEGIN
            SELECT TOP 1 @TaxApplicable = ISNULL(TaxApplicable, 0)
            FROM dbo.WN_ProvinceChargeTypeTax WITH (NOLOCK)
            WHERE ProvinceId = @ProvinceId AND ChargeTypeId = @SupportChargesId;

            SELECT TOP 1 @AppliedChargePercentage = ISNULL(ChargePercentage, 10.00)
            FROM dbo.WN_ChargeTypeRate WITH (NOLOCK)
            WHERE ChargeTypeId = @SupportChargesId
              AND StartDate <= CAST(SYSUTCDATETIME() AS DATE)
              AND (EndDate IS NULL OR EndDate > CAST(SYSUTCDATETIME() AS DATE))
            ORDER BY StartDate DESC;

            IF @TaxApplicable = 1
            BEGIN
                SELECT TOP 1 @AppliedTaxPercentage = ISNULL(TaxPercentage, 16.00)
                FROM dbo.WN_Tax WITH (NOLOCK)
                WHERE ProvinceId = @ProvinceId
                  AND StartDate <= CAST(SYSUTCDATETIME() AS DATE)
                  AND (EndDate IS NULL OR EndDate > CAST(SYSUTCDATETIME() AS DATE))
                ORDER BY StartDate DESC;
            END
        END

        IF @SpaceCategoryCode IN ('ConferenceRoom', 'Conference', 'MeetingRoom', 'Meeting')
            SET @SupportChargeAmount = ROUND(@BaseRent * (@AppliedChargePercentage / 100.00), 2);
        ELSE
            SET @SupportChargeAmount = ROUND(2000.00 * @SpaceCapacity * ISNULL(@BillingPeriodMonths, 3), 2);

        IF @AppliedTaxPercentage > 0 AND @SupportChargeAmount > 0
            SET @TaxAmount = ROUND(@SupportChargeAmount * (@AppliedTaxPercentage / 100.00), 2);

        DECLARE @TotalAmount DECIMAL(18,2) = @BaseRent + @SecDeposit + @TaxAmount;

        INSERT INTO dbo.WN_Quotations (
            QuotationNumber, QuotationDate, ValidUntil, CustomerId, SpaceId, StartDateTime, EndDateTime,
            SubtotalAmount, DiscountPercentage, DiscountAmount, TotalAmount, Remarks, Status, Version, IsActive,
            CreatedDate, CreatedById, DiscountType, SecurityDeposit, FloorId, BillingPeriodMonths,
            SupportChargesId, AppliedChargePercentage, AppliedTaxPercentage, SupportChargeAmount, TaxAmount
        )
        VALUES (
            @QuotationNumber, CAST(SYSUTCDATETIME() AS DATE), @ValidUntil, @CustomerId, @SpaceId, @StartDateTime, @EndDateTime,
            @SubtotalAmount, @SafeDiscountPct, @DiscountAmount, @TotalAmount, @Remarks, 'Active', @NewVersion, 1,
            SYSUTCDATETIME(), @CreatedById, ISNULL(@DiscountType, 'Percentage'), @SecDeposit, @FloorId, ISNULL(@BillingPeriodMonths, 3),
            @SupportChargesId, @AppliedChargePercentage, @AppliedTaxPercentage, @SupportChargeAmount, @TaxAmount
        );

        DECLARE @QuotationId INT = SCOPE_IDENTITY();

        COMMIT TRANSACTION;

        SELECT 
            q.Id,
            q.QuotationNumber,
            q.QuotationDate,
            q.ValidUntil,
            q.CustomerId,
            q.SpaceId,
            q.StartDateTime,
            q.EndDateTime,
            q.SubtotalAmount,
            q.DiscountPercentage,
            q.DiscountAmount,
            q.TotalAmount,
            q.Remarks,
            q.Status,
            q.Version,
            q.IsActive,
            q.CreatedDate,
            q.CreatedById,
            q.DiscountType,
            q.SecurityDeposit,
            q.FloorId,
            q.BillingPeriodMonths,
            q.SupportChargesId,
            q.AppliedChargePercentage,
            q.AppliedTaxPercentage,
            q.SupportChargeAmount,
            q.TaxAmount
        FROM dbo.WN_Quotations q WITH (NOLOCK)
        WHERE q.Id = @QuotationId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_RecordInvoicePayment]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 4. WN_RecordInvoicePayment
CREATE OR ALTER PROCEDURE [dbo].[WN_RecordInvoicePayment]
    @InvoiceId INT,
    @PaidAmount DECIMAL(18,4),
    @PaymentMethod NVARCHAR(50) = 'Cash',
    @TransactionRef NVARCHAR(100) = NULL,
    @Notes NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @GrandTotal DECIMAL(18,4), @CurrentPaid DECIMAL(18,4), @UserId INT, @BookingId INT;
    DECLARE @BillingPeriodStart DATETIME2, @BillingPeriodEnd DATETIME2, @MonthlyRent DECIMAL(18,4);

    SELECT 
        @GrandTotal = i.GrandTotal, 
        @CurrentPaid = i.PaidTotal, 
        @UserId = i.UserId, 
        @BookingId = i.BookingId,
        @BillingPeriodStart = ISNULL(i.BillingPeriodStart, b.StartOn),
        @BillingPeriodEnd = ISNULL(i.BillingPeriodEnd, b.EndOn),
        @MonthlyRent = ISNULL(NULLIF(i.GrandTotal, 0), b.TotalAmount)
    FROM dbo.WN_Invoices i WITH (UPDLOCK) 
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    WHERE i.Id = @InvoiceId;

    IF @GrandTotal IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR('Invoice not found', 16, 1);
        RETURN;
    END;

    DECLARE @NewPaidTotal DECIMAL(18,4) = @CurrentPaid + @PaidAmount;
    DECLARE @NewStatusId TINYINT = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Un Paid' ORDER BY Id); -- OrderStatus 'Un Paid' 
    DECLARE @MonthsCovered INT = 0;

    IF @MonthlyRent > 0
        SET @MonthsCovered = FLOOR(@NewPaidTotal / @MonthlyRent);

    IF @NewPaidTotal >= @GrandTotal
    BEGIN
        SET @NewStatusId = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Paid' ORDER BY Id); -- OrderStatus 'Paid'
        IF @BookingId IS NOT NULL AND @BookingId > 0
        BEGIN
            UPDATE dbo.WN_AccessCards
            SET Status = 1, -- 1 = Active
                EndDate = ISNULL(@BillingPeriodEnd, EndDate),
                UpdatedOn = SYSUTCDATETIME()
            WHERE BookingId = @BookingId;
        END;
    END
    ELSE IF @NewPaidTotal > 0
    BEGIN
        SET @NewStatusId = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Partial' ORDER BY Id); -- OrderStatus 'Partial'
        IF @MonthsCovered >= 1 AND @BookingId IS NOT NULL AND @BookingId > 0
        BEGIN
            DECLARE @AccessValidUntil DATETIME2 = DATEADD(month, @MonthsCovered, ISNULL(@BillingPeriodStart, SYSUTCDATETIME()));
            IF @BillingPeriodEnd IS NOT NULL AND @AccessValidUntil > @BillingPeriodEnd
                SET @AccessValidUntil = @BillingPeriodEnd;

            UPDATE dbo.WN_AccessCards
            SET Status = 1, -- 1 = Active
                EndDate = @AccessValidUntil,
                UpdatedOn = SYSUTCDATETIME()
            WHERE BookingId = @BookingId;
        END;
    END;

    UPDATE dbo.WN_Invoices 
    SET PaidTotal = @NewPaidTotal, StatusId = @NewStatusId 
    WHERE Id = @InvoiceId;

    -- Insert Payment record
    INSERT INTO dbo.WN_Payments (
        IdGUID, Amount, TransactionRef, InvoiceId, PaymentMethodId, 
        CurrencyCode, StatusId, PaidOn, CreatedOn, Notes, UserIdInt, BookingIdInt
    )
    VALUES (
        NEWID(), @PaidAmount, @TransactionRef, @InvoiceId, 1, 
        'PKR', 2, SYSUTCDATETIME(), SYSUTCDATETIME(), @Notes, @UserId, @BookingId
    );

    COMMIT TRANSACTION;

    SELECT 
        1 AS IsSuccessful,
        'Payment recorded successfully' AS Message,
        @NewPaidTotal AS PaidTotal,
        @NewStatusId AS StatusId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_RecurringInvoice_GenerateMonthlyInvoices]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_RecurringInvoice_GenerateMonthlyInvoices]
    @RunDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @RunDate IS NULL SET @RunDate = CAST(SYSUTCDATETIME() AS DATE);

    DECLARE @InvoicesGenerated INT = 0;
    DECLARE @InvoicesSkipped   INT = 0;

    DECLARE @BookingId INT, @PublicId UNIQUEIDENTIFIER, @UserId INT, @SpaceId INT;
    DECLARE @StartOn DATETIME2, @EndOn DATETIME2, @BillingPeriodMonths INT, @MonthlyRent DECIMAL(18,2);
    DECLARE @DiscountAmt DECIMAL(18,2), @AdvanceRentMonths INT, @CreatedById INT;
    DECLARE @RentAccountId INT, @TaxAmount DECIMAL(18,2), @SupportChargeAmount DECIMAL(18,2);

    DECLARE curBookings CURSOR LOCAL FAST_FORWARD FOR
    SELECT 
        b.Id, b.IdGUID, b.UserId, b.SpaceId, b.StartOn, b.EndOn,
        ISNULL(b.BillingPeriodMonths, 1), ISNULL(b.MonthlyRent, 0),
        ISNULL(b.DiscountAmount, 0), ISNULL(b.AdvanceRentMonths, 1),
        b.CreatedById, ISNULL(b.RentAccountId, 2852)
    FROM dbo.WN_Bookings b WITH (NOLOCK)
    WHERE b.IsDeleted = 0
      AND b.BookingStatusId IN (1, 5, 33)
      AND b.StartOn <= @RunDate
      AND b.EndOn >= @RunDate;

    OPEN curBookings;
    FETCH NEXT FROM curBookings INTO 
        @BookingId, @PublicId, @UserId, @SpaceId, @StartOn, @EndOn,
        @BillingPeriodMonths, @MonthlyRent, @DiscountAmt, @AdvanceRentMonths,
        @CreatedById, @RentAccountId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @InvoicesSkipped = @InvoicesSkipped + 1;

        FETCH NEXT FROM curBookings INTO 
            @BookingId, @PublicId, @UserId, @SpaceId, @StartOn, @EndOn,
            @BillingPeriodMonths, @MonthlyRent, @DiscountAmt, @AdvanceRentMonths,
            @CreatedById, @RentAccountId;
    END

    CLOSE curBookings;
    DEALLOCATE curBookings;

    SELECT @InvoicesGenerated AS Generated, @InvoicesSkipped AS Skipped;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_GetByHash]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_RefreshTokens_GetByHash]
    @TokenHash NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT rt.Id, rt.UserId, rt.TokenHash, rt.CreatedOn, rt.ExpiresOn,
           rt.RevokedOn, rt.ReplacedByTokenId,
           u.Email, u.RoleId, u.IsActive
    FROM dbo.WN_RefreshTokens rt WITH (NOLOCK)
    JOIN dbo.WN_Users          u  WITH (NOLOCK) ON u.Id = rt.UserId
    WHERE rt.TokenHash = @TokenHash;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 2: AUTH TOKENS
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_RefreshTokens_Insert]
    @UserId    INT,
    @TokenHash NVARCHAR(450),
    @ExpiresOn DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_RefreshTokens (UserId, TokenHash, ExpiresOn)
    VALUES (@UserId, @TokenHash, @ExpiresOn);
    SELECT Id FROM dbo.WN_RefreshTokens WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_Revoke]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_RefreshTokens_Revoke]
    @Id                  UNIQUEIDENTIFIER,
    @ReplacedByTokenId   UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_RefreshTokens SET
        RevokedOn          = SYSUTCDATETIME(),
        ReplacedByTokenId  = @ReplacedByTokenId
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_RefreshTokens_RevokeAllForUser]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_RefreshTokens_RevokeAllForUser]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_RefreshTokens SET
        RevokedOn = SYSUTCDATETIME()
    WHERE UserId    = @UserId
      AND RevokedOn IS NULL;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Refunds_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 19: REFUNDS
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_Refunds_Insert]
    @PaymentId   INT,
    @Amount      DECIMAL(18,4),
    @Reason      NVARCHAR(500) = NULL,
    @CreatedById INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PaidAmount      DECIMAL(18,4);
    DECLARE @AlreadyRefunded DECIMAL(18,4);

    SELECT @PaidAmount = Amount FROM dbo.WN_Payments WHERE Id = @PaymentId;

    SELECT @AlreadyRefunded = ISNULL(SUM(Amount), 0)
    FROM dbo.WN_Refunds
    WHERE PaymentId = @PaymentId AND StatusId IN (1, 2);

    IF (@AlreadyRefunded + @Amount) > @PaidAmount
    BEGIN
        RAISERROR('Refund amount exceeds available payment amount.', 16, 1);
        RETURN;
    END

    INSERT INTO dbo.WN_Refunds
        (PaymentId, Amount, Reason, StatusId, CreatedById)
    VALUES
        (@PaymentId, @Amount, @Reason, 1, @CreatedById);
    SELECT Id, PublicId FROM dbo.WN_Refunds WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Refunds_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Refunds_UpdateStatus]
    @Id            INT,
    @StatusId      TINYINT,
    @ProcessedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Refunds SET
        StatusId      = @StatusId,
        ProcessedOn   = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE ProcessedOn END,
        ProcessedById = ISNULL(@ProcessedById, ProcessedById)
    WHERE Id = @Id;

    IF @StatusId = 2
        UPDATE dbo.WN_Payments SET
            StatusId  = 4,
            UpdatedOn = SYSUTCDATETIME()
        WHERE Id = (SELECT PaymentId FROM dbo.WN_Refunds WHERE Id = @Id);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_ReRestrictLapsedPartialPayments]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 4b. WN_ReRestrictLapsedPartialPayments
CREATE OR ALTER PROCEDURE [dbo].[WN_ReRestrictLapsedPartialPayments]
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    UPDATE ac
    SET ac.Status = 85, -- 2 = Restricted 
        ac.UpdatedOn = SYSUTCDATETIME()
    FROM dbo.WN_AccessCards ac
    INNER JOIN dbo.WN_Invoices i ON i.BookingId = ac.BookingId
    WHERE ac.EndDate < SYSUTCDATETIME()
      AND ac.Status = 50 -- Currently active
      AND i.StatusId != 14; -- Not fully paid
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Roles_GetAll]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Roles_GetAll]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, RoleName, DisplayName, IsActive, CreatedAt
    FROM dbo.WN_Roles
    WHERE IsActive = 1
    ORDER BY Id ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SecurityDeposits_GetByBooking]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 2. Update dbo.WN_SecurityDeposits_GetByBooking
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_SecurityDeposits_GetByBooking]
    @BookingId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        sd.Id, sd.PublicId, sd.BookingId, sd.UserId,
        u.Name AS UserName, u.Email AS UserEmail,
        sd.Amount, sd.StatusId,
        sd.HeldOn, sd.ReleasedOn, sd.ForfeitedOn,
        sd.ForfeitReason, sd.Notes,
        sd.CustomerId, sd.RefId, sd.RefNo
    FROM dbo.WN_SecurityDeposits sd WITH (NOLOCK)
    JOIN dbo.WN_Users            u  WITH (NOLOCK) ON u.Id = sd.UserId
    WHERE sd.BookingId = @BookingId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_SecurityDeposits_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================================
-- 1. Update dbo.WN_SecurityDeposits_Insert
-- ============================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_SecurityDeposits_Insert]
    @BookingId   INT,
    @UserId      INT,
    @Amount      DECIMAL(18,4),
    @AccountId   INT           = NULL,
    @Notes       NVARCHAR(500) = NULL,
    @UpdatedById INT           = NULL,
    @CustomerId  VARCHAR(50)   = NULL,
    @RefId       INT           = NULL,
    @RefNo       VARCHAR(50)   = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Auto-resolve CustomerId (CustomerCode) if omitted
    IF (@CustomerId IS NULL OR @CustomerId = '') AND @BookingId IS NOT NULL
    BEGIN
        SELECT TOP 1 @CustomerId = CustomerCode 
        FROM dbo.WN_Bookings WITH (NOLOCK) 
        WHERE Id = @BookingId;

        IF (@CustomerId IS NULL OR @CustomerId = '') AND @UserId IS NOT NULL
        BEGIN
            SELECT TOP 1 @CustomerId = Code 
            FROM dbo.WN_Customers WITH (NOLOCK) 
            WHERE UserId = @UserId;
        END
    END

    INSERT INTO dbo.WN_SecurityDeposits
        (BookingId, UserId, Amount, AccountId, StatusId, Notes, UpdatedById, CustomerId, RefId, RefNo)
    VALUES
        (@BookingId, @UserId, @Amount, @AccountId, 1, @Notes, @UpdatedById, @CustomerId, @RefId, @RefNo);

    SELECT Id, PublicId, CustomerId, RefId, RefNo FROM dbo.WN_SecurityDeposits WHERE Id = SCOPE_IDENTITY();
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_SecurityDeposits_UpdateStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SecurityDeposits_UpdateStatus]
    @Id            INT,
    @StatusId      TINYINT,
    @ForfeitReason NVARCHAR(500) = NULL,
    @UpdatedById   INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SecurityDeposits SET
        StatusId      = @StatusId,
        ReleasedOn    = CASE WHEN @StatusId = 2 THEN SYSUTCDATETIME() ELSE ReleasedOn  END,
        ForfeitedOn   = CASE WHEN @StatusId IN (3,4) THEN SYSUTCDATETIME() ELSE ForfeitedOn END,
        ForfeitReason = ISNULL(@ForfeitReason, ForfeitReason),
        UpdatedById   = @UpdatedById
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- 5. WN_SpaceConfig_Delete (soft)
-- ============================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceConfig SET Status = 0, UpdatedOn = GETUTCDATE()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_DeleteSpaces]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_DeleteSpaces]
    @ConfigId INT,
    @SpaceGuids NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LocationId INT = 0, @SpaceTypeId INT = 0;

    SELECT 
        @LocationId = ISNULL(LocationId, 0),
        @SpaceTypeId = ISNULL(SpaceTypeId, 0)
    FROM dbo.WN_SpaceConfig
    WHERE Id = @ConfigId;

    IF @LocationId = 0 AND @SpaceTypeId = 0
    BEGIN
        SELECT CAST(NULL AS NVARCHAR(50)) AS DeletedCode WHERE 1 = 0;
        SELECT CAST(NULL AS NVARCHAR(50)) AS BlockedCode WHERE 1 = 0;
        RETURN;
    END

    CREATE TABLE #TargetGuids (GuidStr NVARCHAR(50) PRIMARY KEY);

    IF @SpaceGuids IS NOT NULL AND LTRIM(RTRIM(@SpaceGuids)) <> ''
    BEGIN
        INSERT INTO #TargetGuids (GuidStr)
        SELECT DISTINCT LTRIM(RTRIM(value))
        FROM STRING_SPLIT(@SpaceGuids, ',')
        WHERE LTRIM(RTRIM(value)) <> '';
    END

    SELECT 
        s.Id, 
        s.Code, 
        CAST(ISNULL(s.IdGUID, s.Id) AS NVARCHAR(50)) AS IdGuidStr
    INTO #TargetSpaces
    FROM dbo.WN_Spaces s
    WHERE s.LocationId = @LocationId 
      AND s.SpaceTypeId = @SpaceTypeId 
      AND (s.IsActive IS NULL OR s.IsActive = 1)
      AND (
          NOT EXISTS (SELECT 1 FROM #TargetGuids)
          OR CAST(ISNULL(s.IdGUID, s.Id) AS NVARCHAR(50)) IN (SELECT GuidStr FROM #TargetGuids)
          OR CAST(s.Id AS NVARCHAR(50)) IN (SELECT GuidStr FROM #TargetGuids)
      );

    CREATE TABLE #Deleted (Code NVARCHAR(50));
    CREATE TABLE #Blocked (Code NVARCHAR(50));

    INSERT INTO #Blocked (Code)
    SELECT ts.Code
    FROM #TargetSpaces ts
    WHERE EXISTS (
        SELECT 1 FROM dbo.WN_Bookings b
        WHERE b.SpaceId = ts.Id 
          AND b.IsDeleted = 0 
          AND b.BookingStatusId IN (1, 2) 
          AND b.EndOn > SYSUTCDATETIME()
    );

    INSERT INTO #Deleted (Code)
    SELECT ts.Code
    FROM #TargetSpaces ts
    WHERE ts.Code NOT IN (SELECT Code FROM #Blocked);

    UPDATE s
    SET s.IsActive = 0, s.Status = 0
    FROM dbo.WN_Spaces s
    JOIN #TargetSpaces ts ON ts.Id = s.Id
    WHERE ts.Code IN (SELECT Code FROM #Deleted);

    SELECT Code AS DeletedCode FROM #Deleted;
    SELECT Code AS BlockedCode FROM #Blocked;

    DROP TABLE #TargetGuids;
    DROP TABLE #TargetSpaces;
    DROP TABLE #Deleted;
    DROP TABLE #Blocked;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GenerateSpaces]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_GenerateSpaces]
    @ConfigId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SpaceCategory    NVARCHAR(20);
    DECLARE @TotalSpaces      INT;
    DECLARE @MinCode          INT;
    DECLARE @FloorId          INT;
    DECLARE @PricePerHour     DECIMAL(18,2);
    DECLARE @PricePerDay      DECIMAL(18,2);
    DECLARE @PricePerMonth    DECIMAL(18,2);
    DECLARE @Amenities        NVARCHAR(MAX);
    DECLARE @RentAccountId    INT;
    DECLARE @DepositAccountId INT;
    DECLARE @LocationId       INT;
    DECLARE @SpaceTypeId      INT;

    SELECT
        @SpaceCategory    = SpaceCategory,
        @TotalSpaces      = TotalSpaces,
        @MinCode          = MinCode,
        @FloorId          = FloorId,
        @PricePerHour     = ISNULL(PricePerHour, 0),
        @PricePerDay      = ISNULL(PricePerDay, 0),
        @PricePerMonth    = ISNULL(PricePerMonth, 0),
        @Amenities        = Amenities,
        @RentAccountId    = RentAccountId,
        @DepositAccountId = SecurityReceivedId,
        @LocationId       = LocationId,
        @SpaceTypeId      = SpaceTypeId
    FROM dbo.WN_SpaceConfig
    WHERE Id = @ConfigId AND Status = 1;

    IF @SpaceCategory IS NULL
    BEGIN
        RAISERROR('Configuration not found or inactive.', 16, 1); RETURN;
    END

    IF @LocationId IS NULL OR @SpaceTypeId IS NULL
    BEGIN
        RAISERROR('Location or SpaceType not found.', 16, 1); RETURN;
    END

    DECLARE @NamePrefix NVARCHAR(50) = CASE
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

        IF EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE LocationId = @LocationId
              AND SpaceTypeId = @SpaceTypeId
              AND Code = @Code
              AND (IsActive = 0 OR Status = 0)
        )
        BEGIN
            UPDATE dbo.WN_Spaces 
            SET IsActive = 1, Status = 1, Name = @Name
            WHERE LocationId = @LocationId
              AND SpaceTypeId = @SpaceTypeId
              AND Code = @Code;

            SET @Created = @Created + 1;
        END
        ELSE IF NOT EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE LocationId = @LocationId
              AND SpaceTypeId = @SpaceTypeId
              AND Code = @Code
        )
        BEGIN
            INSERT INTO dbo.WN_Spaces
                (IdGUID, Name, Code, LocationId, SpaceTypeId, FloorId,
                 Price, Amenities, RentAccountId, SecurityRecivedId,
                 IsActive, Status, CreatedOn)
            VALUES
                (NEWID(), @Name, @Code, @LocationId, @SpaceTypeId, @FloorId,
                 @PricePerMonth, @Amenities, @RentAccountId, @DepositAccountId,
                 1, 1, GETUTCDATE());

            SET @Created = @Created + 1;
        END
        ELSE
            SET @Skipped = @Skipped + 1;

        SET @i = @i + 1;
    END

    SELECT @Created AS Created, @Skipped AS Skipped, @TotalSpaces AS Configured;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetDepositByCategory]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_GetDepositByCategory]
    @CategoryCode NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1
        sp.SecurityDeposit,
        sp.SeatPrice,
        sp.SeatPrice * s.Capacity AS RoomPrice,
        s.Capacity,
        bp.Code AS BillingPeriodCode
    FROM dbo.WN_SpacePricing      sp WITH (NOLOCK)
    JOIN dbo.WN_Spaces            s  WITH (NOLOCK) ON s.Id  = sp.SpaceId
    JOIN dbo.WN_SpaceTypes        st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN dbo.WN_SpaceCategories   sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    JOIN dbo.WN_BillingPeriods    bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    WHERE sc.Code         = @CategoryCode
      AND bp.Code         = 'Monthly'
      AND sp.IsActive     = 1
      AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
    ORDER BY sp.EffectiveFrom DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_GetList]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.Id, c.SpaceCategory, c.TotalSpaces, c.CodePrefix, c.MinCode,
        c.OpeningTime, c.ClosingTime,
        c.UpdatedOn, c.UpdatedBy, c.SpaceTypeId,
        st.Name AS SpaceTypeName, c.SecurityDeposit, c.SecurityReceivedId,
        c.LocationId, l.Name AS LocationName,
        c.RentAccountId,
        c.PricePerHour, c.PricePerDay, c.PricePerMonth,
        c.Amenities, c.Status, c.FloorId
    FROM dbo.WN_SpaceConfig c WITH (NOLOCK)
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = c.SpaceTypeId
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.Id  = c.LocationId
    ORDER BY c.LocationId, c.SpaceCategory;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetListV2]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_GetListV2]
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
        sc.OpeningTime,
        sc.ClosingTime,
        ISNULL(sc.SecurityDeposit, 0) AS SecurityDeposit,
        sc.SecurityReceivedId         AS DepositAccountId,
        sc.RentAccountId,
        sc.PricePerHour,
        sc.PricePerDay,
        sc.PricePerMonth,
        sc.Amenities,
        sc.FloorId,
        sc.LocationId,
        l.CompanyId    AS CompanyId,
        l.BranchId     AS BranchId,
        sc.SpaceTypeId,
        sc.Status,
        sc.UpdatedOn,
        sc.UpdatedBy,
        l.Name         AS LocationName,
        st.Description AS SpaceTypeName
    FROM dbo.WN_SpaceConfig sc
    LEFT JOIN dbo.WN_Locations  l  WITH (NOLOCK) ON l.Id  = sc.LocationId
    LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = sc.SpaceTypeId
    WHERE sc.Status = 1
      AND (@CompanyId  IS NULL OR l.CompanyId   = @CompanyId)
      AND (@BranchId   IS NULL OR l.BranchId    = @BranchId)
      AND (@LocationId IS NULL OR sc.LocationId = @LocationId)
    ORDER BY sc.LocationId, sc.SpaceCategory;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_GetSpaceStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_GetSpaceStatus]
    @ConfigId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LocationId    INT;
    DECLARE @SpaceTypeId   INT;

    SELECT 
        @LocationId  = LocationId,
        @SpaceTypeId = SpaceTypeId
    FROM dbo.WN_SpaceConfig WHERE Id = @ConfigId;

    SELECT
        s.Id,
        COALESCE(CAST(s.IdGUID AS NVARCHAR(50)), CAST(s.Id AS NVARCHAR(50))) AS IdGuid,
        COALESCE(CAST(s.IdGUID AS NVARCHAR(50)), CAST(s.Id AS NVARCHAR(50))) AS PublicId,
        s.Code,
        s.Name,
        ISNULL(s.Status, 1) AS Status,
        ISNULL(s.IsActive, 1) AS IsActive,
        CASE WHEN EXISTS (
            SELECT 1 FROM dbo.WN_Bookings b
            WHERE b.SpaceId = s.Id
              AND (b.IsDeleted = 0 OR b.IsDeleted IS NULL)
              AND b.BookingStatusId IN (1, 2)
              AND b.StartOn <= SYSUTCDATETIME()
              AND b.EndOn >= SYSUTCDATETIME()
        ) THEN 1 ELSE 0 END AS HasBookings
    FROM dbo.WN_Spaces s
    WHERE (s.IsActive IS NULL OR s.IsActive = 1)
      AND s.LocationId = @LocationId
      AND s.SpaceTypeId = @SpaceTypeId
    ORDER BY s.Id ASC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 5. Remove RentAccountId / DepositAccountId from WN_SpaceConfig_Insert
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_Insert]
    @SpaceCategory   NVARCHAR(50)   = NULL,
    @TotalSpaces     INT,
    @CodePrefix      NVARCHAR(20),
    @MinCode         INT,
    @OpeningTime     NVARCHAR(10)   = '08:00',
    @ClosingTime     NVARCHAR(10)   = '20:00',
    @SecurityDeposit DECIMAL(18,2)  = 0,
    @FloorId         INT            = NULL,
    @PricePerHour    DECIMAL(18,2)  = 0,
    @PricePerDay     DECIMAL(18,2)  = 0,
    @PricePerMonth   DECIMAL(18,2)  = 0,
    @Amenities       NVARCHAR(MAX)  = NULL,
    @LocationId      INT            = NULL,
    @SpaceTypeId     INT            = NULL,
    @CreatedBy       NVARCHAR(256)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_SpaceConfig
        (SpaceCategory, TotalSpaces, CodePrefix, MinCode, OpeningTime, ClosingTime,
         SecurityDeposit, FloorId, PricePerHour, PricePerDay, PricePerMonth,
         Amenities, LocationId, SpaceTypeId, Status )
    VALUES
        (@SpaceCategory, @TotalSpaces, @CodePrefix, @MinCode, @OpeningTime, @ClosingTime,
         @SecurityDeposit, @FloorId, @PricePerHour, @PricePerDay, @PricePerMonth,
         @Amenities, @LocationId, @SpaceTypeId, 1);

    SELECT SCOPE_IDENTITY() AS NewId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceConfig_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 6. Remove RentAccountId / DepositAccountId from WN_SpaceConfig_Update
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceConfig_Update]
    @Id              INT,
    @TotalSpaces     INT            = NULL,
    @CodePrefix      NVARCHAR(20)   = NULL,
    @MinCode         INT            = NULL,
    @OpeningTime     NVARCHAR(10)   = NULL,
    @ClosingTime     NVARCHAR(10)   = NULL,
    @SecurityDeposit DECIMAL(18,2)  = NULL,
    @FloorId         INT            = NULL,
    @PricePerHour    DECIMAL(18,2)  = NULL,
    @PricePerDay     DECIMAL(18,2)  = NULL,
    @PricePerMonth   DECIMAL(18,2)  = NULL,
    @Amenities       NVARCHAR(MAX)  = NULL,
    @UpdatedBy       NVARCHAR(256)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceConfig SET
        TotalSpaces     = ISNULL(@TotalSpaces,     TotalSpaces),
        CodePrefix      = ISNULL(@CodePrefix,      CodePrefix),
        MinCode         = ISNULL(@MinCode,         MinCode),
        OpeningTime     = ISNULL(@OpeningTime,     OpeningTime),
        ClosingTime     = ISNULL(@ClosingTime,     ClosingTime),
        SecurityDeposit = ISNULL(@SecurityDeposit, SecurityDeposit),
        FloorId         = ISNULL(@FloorId,         FloorId),
        PricePerHour    = ISNULL(@PricePerHour,    PricePerHour),
        PricePerDay     = ISNULL(@PricePerDay,     PricePerDay),
        PricePerMonth   = ISNULL(@PricePerMonth,   PricePerMonth),
        Amenities       = ISNULL(@Amenities,       Amenities),
        UpdatedBy       = @UpdatedBy,
        UpdatedOn       = GETUTCDATE()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceInventoryConfig_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceInventoryConfig_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceInventoryConfig SET
        IsActive  = 0,
        UpdatedOn = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceInventoryConfig_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ============================================================
-- SECTION 5: SPACE INVENTORY CONFIG & PRICING
-- ============================================================

CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceInventoryConfig_GetList]
    @LocationId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.Id, c.LocationId, l.Name AS LocationName,
        c.SpaceTypeId, st.Name AS SpaceTypeName,
        c.CodePrefix, c.MinCode, c.TotalSpaces,
        c.IsActive, c.UpdatedOn
    FROM dbo.WN_SpaceInventoryConfig c  WITH (NOLOCK)
    JOIN dbo.WN_Locations            l  WITH (NOLOCK) ON l.Id  = c.LocationId
    JOIN dbo.WN_SpaceTypes           st WITH (NOLOCK) ON st.Id = c.SpaceTypeId
    WHERE c.IsActive = 1
      AND (@LocationId IS NULL OR c.LocationId = @LocationId)
    ORDER BY c.LocationId, st.Name;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceInventoryConfig_Upsert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceInventoryConfig_Upsert]
    @LocationId  INT,
    @SpaceTypeId INT,
    @CodePrefix  NVARCHAR(10),
    @MinCode     INT,
    @TotalSpaces INT,
    @UpdatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM dbo.WN_SpaceInventoryConfig
        WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId
    )
    BEGIN
        UPDATE dbo.WN_SpaceInventoryConfig SET
            CodePrefix  = @CodePrefix,
            MinCode     = @MinCode,
            TotalSpaces = @TotalSpaces,
            UpdatedOn   = SYSUTCDATETIME(),
            UpdatedById = @UpdatedById
        WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.WN_SpaceInventoryConfig
            (LocationId, SpaceTypeId, CodePrefix, MinCode, TotalSpaces, IsActive, UpdatedById)
        VALUES
            (@LocationId, @SpaceTypeId, @CodePrefix, @MinCode, @TotalSpaces, 1, @UpdatedById);
    END
    SELECT Id FROM dbo.WN_SpaceInventoryConfig
    WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpacePricing_GetActive]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpacePricing_GetActive]
    @SpaceId         INT,
    @BillingPeriodId TINYINT = NULL,
    @TierTypeId      TINYINT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        sp.Id AS PricingId, sp.SpaceId,
        sp.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        sp.TierTypeId, tt.Code AS TierCode,
        sp.SeatPrice, sp.SeatPrice * s.Capacity AS RoomPrice,
        sp.SecurityDeposit, sp.CurrencyCode,
        sp.EffectiveFrom, sp.EffectiveTo
    FROM dbo.WN_SpacePricing      sp WITH (NOLOCK)
    JOIN dbo.WN_Spaces            s  WITH (NOLOCK) ON s.Id  = sp.SpaceId
    JOIN dbo.WN_BillingPeriods    bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    JOIN dbo.WN_PricingTierTypes  tt WITH (NOLOCK) ON tt.Id = sp.TierTypeId
    WHERE sp.SpaceId      = @SpaceId
      AND sp.IsActive     = 1
      AND sp.TierTypeId   = @TierTypeId
      AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
      AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
      AND (@BillingPeriodId IS NULL OR sp.BillingPeriodId = @BillingPeriodId)
    ORDER BY sp.BillingPeriodId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpacePricing_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- When a new pricing row is inserted for a space, also update the parent
-- SpaceConfig.PricePerSeat so the config stays in sync.
-- This ensures the admin panel shows the current per-seat price.
CREATE OR ALTER PROCEDURE [dbo].[WN_SpacePricing_Insert]
    @SpaceId          INT,
    @BillingPeriodId  TINYINT,
    @TierTypeId       TINYINT       = 1,
    @SeatPrice        DECIMAL(18,4),
    @SecurityDeposit  DECIMAL(18,4) = 0,
    @RentAccountId    INT           = NULL,
    @DepositAccountId INT           = NULL,
    @CurrencyCode     NVARCHAR(10)  = 'PKR',
    @EffectiveFrom    DATE          = NULL,
    @EffectiveTo      DATE          = NULL,
    @Notes            NVARCHAR(500) = NULL,
    @CreatedById      INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate SeatPrice
    IF @SeatPrice IS NULL OR @SeatPrice <= 0
    BEGIN
        RAISERROR('SeatPrice must be greater than 0.', 16, 1);
        RETURN;
    END

    SET @EffectiveFrom = ISNULL(@EffectiveFrom, CAST(SYSUTCDATETIME() AS DATE));

    -- Close the previous active pricing row for this space/period/tier
    UPDATE dbo.WN_SpacePricing SET
        EffectiveTo = DATEADD(DAY, -1, @EffectiveFrom),
        IsActive    = 0
    WHERE SpaceId         = @SpaceId
      AND BillingPeriodId = @BillingPeriodId
      AND TierTypeId      = @TierTypeId
      AND IsActive        = 1
      AND EffectiveTo     IS NULL;

    INSERT INTO dbo.WN_SpacePricing
        (SpaceId, BillingPeriodId, TierTypeId, SeatPrice, SecurityDeposit,
         RentAccountId, DepositAccountId, CurrencyCode,
         EffectiveFrom, EffectiveTo, IsActive, Notes, CreatedById)
    VALUES
        (@SpaceId, @BillingPeriodId, @TierTypeId, @SeatPrice, @SecurityDeposit,
         @RentAccountId, @DepositAccountId, @CurrencyCode,
         @EffectiveFrom, @EffectiveTo, 1, @Notes, @CreatedById);

    SELECT Id, PublicId FROM dbo.WN_SpacePricing WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM dbo.WN_Bookings
        WHERE SpaceId = @Id 
          AND IsDeleted = 0 
          AND BookingStatusId IN (1, 2)
          AND EndOn > SYSUTCDATETIME()
    )
    BEGIN
        RAISERROR('Cannot delete space with active bookings.', 16, 1);
        RETURN;
    END
    UPDATE dbo.WN_Spaces SET IsActive = 0, Status = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GenerateInventory]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GenerateInventory]
    @ConfigId    INT,
    @CreatedById INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @LocationId  INT, @SpaceTypeId INT, @CodePrefix NVARCHAR(10),
            @MinCode     INT, @TotalSpaces  INT;

    SELECT @LocationId  = LocationId,  @SpaceTypeId = SpaceTypeId,
           @CodePrefix  = CodePrefix,  @MinCode     = MinCode,
           @TotalSpaces = TotalSpaces
    FROM dbo.WN_SpaceInventoryConfig
    WHERE Id = @ConfigId AND IsActive = 1;

    IF @LocationId IS NULL
    BEGIN
        RAISERROR('Config not found or inactive.', 16, 1); RETURN;
    END

    DECLARE @st_Name NVARCHAR(100);
    SELECT @st_Name = Name FROM dbo.WN_SpaceTypes WHERE Id = @SpaceTypeId;

    DECLARE @PricePerSeat    DECIMAL(18,4) = 0;
    DECLARE @SecurityDeposit DECIMAL(18,4) = 0;
    DECLARE @RentAccountId   INT = NULL;
    DECLARE @DepositAccountId INT = NULL;

    SELECT TOP 1
        @PricePerSeat     = ISNULL(PricePerSeat, 0),
        @SecurityDeposit  = ISNULL(SecurityDeposit, 0),
        @RentAccountId    = RentAccountId,
        @DepositAccountId = SecurityReceivedId
    FROM dbo.WN_SpaceConfig
    WHERE LocationId = @LocationId AND SpaceTypeId = @SpaceTypeId AND Status = 1;

    DECLARE @MonthlyBillingPeriodId TINYINT;
    SELECT @MonthlyBillingPeriodId = Id FROM dbo.WN_BillingPeriods WHERE Code = 'Monthly';

    DECLARE @Created INT = 0, @Skipped INT = 0, @i INT = 0;
    DECLARE @Code NVARCHAR(20), @SpaceName NVARCHAR(200);
    DECLARE @NewSpaceId INT;

    WHILE @i < @TotalSpaces
    BEGIN
        SET @Code      = @CodePrefix + CAST(@MinCode + @i AS NVARCHAR(10));
        SET @SpaceName = @st_Name + ' ' + @Code;

        IF NOT EXISTS (
            SELECT 1 FROM dbo.WN_Spaces
            WHERE LocationId = @LocationId AND Code = @Code
        )
        BEGIN
            INSERT INTO dbo.WN_Spaces
                (Code, Name, LocationId, SpaceTypeId, Capacity, IsActive, CreatedById)
            VALUES
                (@Code, @SpaceName, @LocationId, @SpaceTypeId, 1, 1, @CreatedById);

            SET @NewSpaceId = SCOPE_IDENTITY();
            SET @Created = @Created + 1;

            IF @PricePerSeat > 0 AND @MonthlyBillingPeriodId IS NOT NULL
            BEGIN
                INSERT INTO dbo.WN_SpacePricing
                    (SpaceId, BillingPeriodId, TierTypeId, SeatPrice, SecurityDeposit,
                     RentAccountId, DepositAccountId, CurrencyCode,
                     EffectiveFrom, EffectiveTo, IsActive, Notes, CreatedById)
                VALUES
                    (@NewSpaceId, @MonthlyBillingPeriodId, 1, @PricePerSeat, @SecurityDeposit,
                     @RentAccountId, @DepositAccountId, N'PKR',
                     CAST(SYSUTCDATETIME() AS DATE), NULL, 1,
                     N'Auto-generated from SpaceInventoryConfig', @CreatedById);
            END
        END
        ELSE
            SET @Skipped = @Skipped + 1;

        SET @i = @i + 1;
    END

    SELECT @Created AS Created, @Skipped AS Skipped, @TotalSpaces AS Configured;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetAvailabilityCounts]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 7. PROCEDURE: dbo.WN_Spaces_GetAvailabilityCounts
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetAvailabilityCounts]
    @ShiftType NVARCHAR(20) = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    WITH SpaceAvail AS (
        SELECT
            s.Id,
            st.CategoryId,
            CASE WHEN EXISTS (
                SELECT 1 FROM dbo.WN_Bookings bk
                WHERE bk.SpaceId = s.Id AND bk.IsDeleted = 0
                  AND bk.BookingStatusId IN (5, 33)
                  AND SYSUTCDATETIME() < bk.EndOn
                  AND SYSUTCDATETIME() >= bk.StartOn
                  AND (
                      @NormalizedShift = '24_7'
                      OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                      OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
                  )
            ) THEN 0 ELSE 1 END AS IsAvailable
        FROM dbo.WN_Spaces     s  WITH (NOLOCK)
        JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
        WHERE s.IsActive = 1
    )
    SELECT
        sc.Code  AS CategoryCode,
        sc.Label AS CategoryLabel,
        COUNT(a.Id)        AS TotalSpaces,
        SUM(a.IsAvailable) AS AvailableNow
    FROM SpaceAvail             a
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = a.CategoryId
    GROUP BY sc.Id, sc.Code, sc.Label
    ORDER BY sc.Label;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetAvailable]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 6. PROCEDURE: dbo.WN_Spaces_GetAvailable
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetAvailable]
    @ShiftType NVARCHAR(20) = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name,
        s.LocationId AS LocationId, l.Name AS LocationName,
        s.SpaceTypeId AS SpaceTypeId, st.Description AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit, vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationId
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice, sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id AND sp.IsActive = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId = s.Id AND bk.IsDeleted = 0
            AND bk.BookingStatusId IN (5, 33)
            AND SYSUTCDATETIME() < bk.EndOn
            AND SYSUTCDATETIME() >= bk.StartOn
            AND (
                @NormalizedShift = '24_7'
                OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
            )
      )
    ORDER BY s.LocationId, TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetAvailableByType]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ==========================================================================================
-- 5. PROCEDURE: dbo.WN_Spaces_GetAvailableByType
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetAvailableByType]
    @SpaceTypeId      INT,
    @StartOn          DATETIME2,
    @EndOn            DATETIME2,
    @ExcludeBookingId INT = NULL,
    @ShiftType        NVARCHAR(20) = '24_7'
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NormalizedShift NVARCHAR(20) = CASE 
        WHEN @ShiftType IS NULL OR @ShiftType = '' OR @ShiftType IN ('24_7', '24/7', '24-by-7', '1') THEN '24_7'
        WHEN LOWER(@ShiftType) IN ('morning', 'morning_shift', 'shift_morning', '2') THEN 'morning'
        WHEN LOWER(@ShiftType) IN ('evening', 'evening_shift', 'shift_evening', 'night', '3') THEN 'evening'
        ELSE LOWER(@ShiftType)
    END;

    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name,
        s.LocationId AS LocationId, l.Name AS LocationName,
        s.SpaceTypeId AS SpaceTypeId, st.Description AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit, vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationId
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice, sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId = s.Id AND sp.IsActive = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IsActive       = 1
      AND s.SpaceTypeId = @SpaceTypeId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings bk
          WHERE bk.SpaceId = s.Id AND bk.IsDeleted = 0
            AND bk.BookingStatusId IN (5, 33)
            AND (@ExcludeBookingId IS NULL OR bk.Id <> @ExcludeBookingId)
            AND @StartOn < bk.EndOn AND @EndOn > bk.StartOn
            AND (
                @NormalizedShift = '24_7'
                OR ISNULL(bk.ShiftType, '24_7') = '24_7'
                OR @NormalizedShift = ISNULL(bk.ShiftType, '24_7')
            )
      )
    ORDER BY TRY_CAST(s.Code AS INT), s.Code;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetByGuid]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3.17 WN_Spaces_GetByGuid (legacy â€” returns NULL for deprecated columns)
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetByGuid] 
    @IdGUID UNIQUEIDENTIFIER 
AS 
BEGIN
    SET NOCOUNT ON; 
    BEGIN TRY
        SELECT s.Id, s.IdGUID, s.Name, s.Code, s.Description, s.LocationId, s.SpaceTypeId, s.FloorId,
            NULL AS PricePerDay, NULL AS PricePerHour, s.ImageUrl, s.Amenities, s.Status,
            l.Name AS LocationName, st.Description AS SpaceTypeName
        FROM dbo.WN_Spaces s WITH(NOLOCK) 
        LEFT JOIN dbo.WN_Locations l WITH(NOLOCK) ON l.Id = s.LocationId 
        LEFT JOIN dbo.WN_SpaceTypes st WITH(NOLOCK) ON st.Id = s.SpaceTypeId
        WHERE s.IdGUID = @IdGUID;
    END TRY 
    BEGIN CATCH 
        THROW; 
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetByPublicId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetByPublicId]
    @PublicId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id, s.IdGUID AS PublicId, s.IdGUID, s.Code, s.Name,
        s.LocationId, l.Name AS LocationName,
        s.SpaceTypeId, st.Description AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl,
        vp.SeatPrice, vp.RoomPrice, vp.SecurityDeposit, vp.BillingPeriodCode
    FROM dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationId
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    OUTER APPLY (
        SELECT TOP 1
            sp.SeatPrice, sp.SeatPrice * s.Capacity AS RoomPrice,
            sp.SecurityDeposit, bp.Code AS BillingPeriodCode
        FROM dbo.WN_SpacePricing   sp
        JOIN dbo.WN_BillingPeriods bp ON bp.Id = sp.BillingPeriodId
        WHERE sp.SpaceId      = s.Id AND sp.IsActive = 1
          AND sp.EffectiveFrom <= CAST(SYSUTCDATETIME() AS DATE)
          AND (sp.EffectiveTo IS NULL OR sp.EffectiveTo > CAST(SYSUTCDATETIME() AS DATE))
        ORDER BY sp.EffectiveFrom DESC
    ) vp
    WHERE s.IdGUID = @PublicId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetConfig]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetConfig]
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, SpaceCategory, TotalSpaces, CodePrefix, MinCode,
           OpeningTime, ClosingTime,
           SecurityDeposit, UpdatedOn, UpdatedBy
    FROM dbo.WN_SpaceConfig
    ORDER BY Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetList]
    @Page        INT           = 1,
    @Limit       INT           = 20,
    @Search      NVARCHAR(255) = NULL,
    @LocationId  INT           = NULL,
    @SpaceTypeId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT
        s.Id, s.IdGUID AS PublicId, s.Code, s.Name, s.Description,
        s.LocationId, l.Name AS LocationName,
        s.FloorId, f.Name AS FloorName, f.FloorNumber,
        s.SpaceTypeId, st.Name AS SpaceTypeName,
        sc.Code AS CategoryCode, sc.Label AS CategoryLabel,
        s.Capacity, s.ImageUrl, s.IsActive,
        CASE
            WHEN s.IsActive = 0 THEN 'Inactive'
            WHEN EXISTS (
                SELECT 1 FROM dbo.WN_Bookings b WITH (NOLOCK)
                WHERE b.SpaceId = s.Id
                  AND b.BookingStatusId IN (1, 2)
                  AND b.EndOn >= SYSUTCDATETIME()
                  AND (b.IsDeleted IS NULL OR b.IsDeleted = 0)
            ) THEN 'Booked'
            ELSE 'Available'
        END AS Status,
        (
            SELECT MAX(b.EndOn)
            FROM dbo.WN_Bookings b WITH (NOLOCK)
            WHERE b.SpaceId = s.Id
              AND b.BookingStatusId IN (1, 2)
              AND b.EndOn >= SYSUTCDATETIME()
              AND (b.IsDeleted IS NULL OR b.IsDeleted = 0)
        ) AS BookedTill,
        s.Price, s.BillingPeriodId, bp.Code AS BillingPeriodCode, bp.Label AS BillingPeriodLabel,
        s.Price AS SeatPrice, s.Price AS RoomPrice,
        s.Amenities,
        COUNT(*) OVER() AS TotalCount
    FROM  dbo.WN_Spaces          s  WITH (NOLOCK)
    JOIN  dbo.WN_Locations       l  WITH (NOLOCK) ON l.Id  = s.LocationId
    JOIN  dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN  dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    LEFT JOIN dbo.WN_Floors      f  WITH (NOLOCK) ON f.Id  = s.FloorId
    LEFT JOIN dbo.WN_BillingPeriods bp WITH (NOLOCK) ON bp.Id = s.BillingPeriodId
    WHERE (@Search IS NULL
            OR s.Name LIKE '%' + @Search + '%'
            OR s.Code LIKE '%' + @Search + '%')
      AND (@LocationId  IS NULL OR s.LocationId  = @LocationId)
      AND (@SpaceTypeId IS NULL OR s.SpaceTypeId = @SpaceTypeId)
    ORDER BY s.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_GetVacant]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 3.15 WN_Spaces_GetVacant (legacy â€” returns NULL for deprecated columns)
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_GetVacant] 
    @CompanyId INT = NULL, 
    @BranchId INT = NULL 
AS 
BEGIN
    SET NOCOUNT ON;
    SELECT s.Id, s.IdGUID, s.Name, s.Code, s.Status,
        NULL AS PricePerDay, NULL AS PricePerHour, NULL AS PricePerMonth,
        l.Name AS LocationName, l.CompanyId, l.BranchId, st.Description AS SpaceTypeName
    FROM dbo.WN_Spaces s 
    LEFT JOIN dbo.WN_Locations l ON l.Id = s.LocationId 
    LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeId
    WHERE s.Status = 1 
      AND (@CompanyId IS NULL OR l.CompanyId = @CompanyId) 
      AND (@BranchId IS NULL OR l.BranchId = @BranchId)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings b 
          WHERE b.SpaceId = s.Id 
            AND b.BookingStatusId = 1 
            AND b.StartOn <= GETDATE() 
            AND b.EndOn >= GETDATE()
      )
    ORDER BY TRY_CAST(s.Code AS INT);
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_Insert]
    @Code            NVARCHAR(20),
    @Name            NVARCHAR(200),
    @Description     NVARCHAR(1000) = NULL,
    @LocationId      INT,
    @FloorId         INT            = NULL,
    @SpaceTypeId     INT,
    @Capacity        SMALLINT,
    @ImageUrl        NVARCHAR(500)  = NULL,
    @CreatedById     INT            = NULL,
    @Price           DECIMAL(18,2)  = NULL,
    @BillingPeriodId TINYINT        = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.WN_Spaces WHERE LocationId = @LocationId AND Code = @Code)
    BEGIN
        RAISERROR('Space code %s already exists at this location.', 16, 1, @Code);
        RETURN;
    END
    INSERT INTO dbo.WN_Spaces
        (Code, Name, Description, LocationId, FloorId, SpaceTypeId,
         Capacity, ImageUrl, IsActive, CreatedById, Price, BillingPeriodId)
    VALUES
        (@Code, @Name, @Description, @LocationId, @FloorId, @SpaceTypeId,
         @Capacity, @ImageUrl, 1, @CreatedById, @Price, @BillingPeriodId);
    SELECT Id, IdGUID AS PublicId FROM dbo.WN_Spaces WHERE Id = SCOPE_IDENTITY();
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_SetAmenities]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_SetAmenities]
    @SpaceId    INT,
    @AmenityIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.WN_SpaceAmenities WHERE SpaceId = @SpaceId;
    INSERT INTO dbo.WN_SpaceAmenities (SpaceId, AmenityId)
    SELECT @SpaceId, CAST(LTRIM(RTRIM(value)) AS INT)
    FROM STRING_SPLIT(@AmenityIds, ',')
    WHERE LTRIM(RTRIM(value)) <> '';
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_Update]
    @Id              INT,
    @Name            NVARCHAR(200)  = NULL,
    @Description     NVARCHAR(1000) = NULL,
    @FloorId         INT            = NULL,
    @SpaceTypeId     INT            = NULL,
    @Capacity        SMALLINT       = NULL,
    @ImageUrl        NVARCHAR(500)  = NULL,
    @LocationId      INT            = NULL,
    @Price           DECIMAL(18,2)  = NULL,
    @BillingPeriodId TINYINT        = NULL,
    @PricePerHour    DECIMAL(18,2)  = NULL,
    @PricePerDay     DECIMAL(18,2)  = NULL,
    @PricePerMonth   DECIMAL(18,2)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.WN_Spaces SET
        Name            = ISNULL(@Name,            Name),
        Description     = ISNULL(@Description,     Description),
        FloorId         = ISNULL(@FloorId,         FloorId),
        SpaceTypeId     = ISNULL(@SpaceTypeId,     SpaceTypeId),
        Capacity        = ISNULL(@Capacity,        Capacity),
        ImageUrl        = ISNULL(@ImageUrl,        ImageUrl),
        LocationId      = ISNULL(@LocationId,      LocationId),
        Price           = ISNULL(@Price,           ISNULL(@PricePerMonth, ISNULL(@PricePerDay, ISNULL(@PricePerHour, Price)))),
        BillingPeriodId = ISNULL(@BillingPeriodId, BillingPeriodId),
        UpdatedOn       = SYSUTCDATETIME()
    WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Spaces_UpdateConfig]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Update the UpdateConfig SP to support SecurityDeposit
CREATE OR ALTER PROCEDURE [dbo].[WN_Spaces_UpdateConfig]
    @SpaceCategory      NVARCHAR(20),
    @TotalSpaces        INT,
    @DefaultCapacities  NVARCHAR(50)  = NULL,
    @OpeningTime        NVARCHAR(5)   = NULL,
    @ClosingTime        NVARCHAR(5)   = NULL,
    @AdminEmail         NVARCHAR(255) = NULL,
    @SecurityDeposit    DECIMAL(10,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceConfig
    SET TotalSpaces       = @TotalSpaces,
        OpeningTime       = ISNULL(@OpeningTime,       OpeningTime),
        ClosingTime       = ISNULL(@ClosingTime,       ClosingTime),
        SecurityDeposit   = ISNULL(@SecurityDeposit,   SecurityDeposit),
        UpdatedOn         = GETUTCDATE(),
        UpdatedBy         = @AdminEmail
    WHERE SpaceCategory = @SpaceCategory;

    SELECT @@ROWCOUNT AS AffectedRows;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceTypes_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceTypes_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceTypes SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceTypes_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceTypes_GetList]
    @CategoryId TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        st.Id,
        st.IdGUID AS PublicId,
        st.Name,
        st.Description,
        st.CategoryId,
        sc.Code AS CategoryCode,
        sc.Label AS CategoryLabel,
        ISNULL(st.Capacity, 1) AS Capacity,
        st.HourlyAllowed,
        st.IsActive,
        st.AccountReceivableId,
        st.RentAccountId,
        st.ServicesIncomeId,
        st.SalesTaxId,
        st.SecurityReceivedId,
        coa_ar.Description   AS AccountReceivableName,
        coa_rent.Description AS RentAccountName,
        coa_serv.Description AS ServicesIncomeName,
        coa_tax.Description  AS SalesTaxName,
        coa_sec.Description  AS SecurityReceivedName
    FROM dbo.WN_SpaceTypes st WITH (NOLOCK)
    LEFT JOIN dbo.WN_SpaceCategories sc WITH (NOLOCK) ON sc.Id = st.CategoryId
    LEFT JOIN dbo.AccountsCOA coa_ar   WITH (NOLOCK) ON coa_ar.Id = st.AccountReceivableId
    LEFT JOIN dbo.AccountsCOA coa_rent WITH (NOLOCK) ON coa_rent.Id = st.RentAccountId
    LEFT JOIN dbo.AccountsCOA coa_serv WITH (NOLOCK) ON coa_serv.Id = st.ServicesIncomeId
    LEFT JOIN dbo.AccountsCOA coa_tax  WITH (NOLOCK) ON coa_tax.Id = st.SalesTaxId
    LEFT JOIN dbo.AccountsCOA coa_sec  WITH (NOLOCK) ON coa_sec.Id = st.SecurityReceivedId
    WHERE st.IsActive = 1
      AND (@CategoryId IS NULL OR st.CategoryId = @CategoryId)
    ORDER BY sc.Label, st.Name;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceTypes_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- ==========================================================================================
-- Update WN_SpaceTypes_Insert to synchronize WN_ChargeTypeAccountMapping
-- ==========================================================================================
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceTypes_Insert]
    @Name                NVARCHAR(100),
    @Description         NVARCHAR(500) = NULL,
    @CategoryId          TINYINT       = NULL,
    @Capacity            SMALLINT      = NULL,
    @HourlyAllowed       BIT           = 1,
    @AccountReceivableId INT           = NULL,
    @RentAccountId       INT           = NULL,
    @ServicesIncomeId    INT           = NULL,
    @SalesTaxId          INT           = NULL,
    @SecurityReceivedId  INT           = NULL,
    @CreatedById         INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY

        DECLARE @NewIdGUID UNIQUEIDENTIFIER = NEWID();

        INSERT INTO dbo.WN_SpaceTypes 
            (IdGUID, Name, Description, CategoryId, Capacity, HourlyAllowed, IsActive,
             AccountReceivableId, RentAccountId, ServicesIncomeId, SalesTaxId, SecurityReceivedId,
             CreatedBy, CreatedOn)
        VALUES 
            (@NewIdGUID, @Name, @Description, @CategoryId, ISNULL(@Capacity, 1), @HourlyAllowed, 1,
             @AccountReceivableId, @RentAccountId, @ServicesIncomeId, @SalesTaxId, @SecurityReceivedId,
             @CreatedById, GETUTCDATE());

        DECLARE @InsertedId INT = SCOPE_IDENTITY();

        -- Synchronize WN_ChargeTypeAccountMapping
        IF @RentAccountId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 1)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET RentAccountId = @RentAccountId WHERE ChargeTypeId = 1;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, RentAccountId, EffectiveFrom) VALUES (1, @RentAccountId, CAST(GETDATE() AS DATE));
        END

        IF @SecurityReceivedId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 2)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET SecurityReceivedId = @SecurityReceivedId WHERE ChargeTypeId = 2;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, SecurityReceivedId, EffectiveFrom) VALUES (2, @SecurityReceivedId, CAST(GETDATE() AS DATE));
        END

        IF @SalesTaxId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 3)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET SalesTaxId = @SalesTaxId WHERE ChargeTypeId = 3;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, SalesTaxId, EffectiveFrom) VALUES (3, @SalesTaxId, CAST(GETDATE() AS DATE));
        END

        IF @ServicesIncomeId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 4)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET ServicesIncomeId = @ServicesIncomeId WHERE ChargeTypeId = 4;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, ServicesIncomeId, EffectiveFrom) VALUES (4, @ServicesIncomeId, CAST(GETDATE() AS DATE));
        END

        IF @AccountReceivableId IS NOT NULL
        BEGIN
            UPDATE dbo.WN_ChargeTypeAccountMapping 
            SET AccountReceivableId = @AccountReceivableId 
            WHERE ChargeTypeId IN (1, 2, 3, 4);
        END

        COMMIT TRANSACTION;

        SELECT Id, IdGUID, Capacity FROM dbo.WN_SpaceTypes WHERE Id = @InsertedId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_SpaceTypes_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- 6. Update WN_SpaceTypes_Update to automatically cascade updated accounts to child spaces
CREATE OR ALTER PROCEDURE [dbo].[WN_SpaceTypes_Update]
    @Id                  INT,
    @Name                NVARCHAR(100) = NULL,
    @Description         NVARCHAR(500) = NULL,
    @CategoryId          TINYINT       = NULL,
    @Capacity            SMALLINT      = NULL,
    @HourlyAllowed       BIT           = NULL,
    @AccountReceivableId INT           = NULL,
    @RentAccountId       INT           = NULL,
    @ServicesIncomeId    INT           = NULL,
    @SalesTaxId          INT           = NULL,
    @SecurityReceivedId  INT           = NULL,
    @UpdatedById         INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;
    BEGIN TRY

        UPDATE dbo.WN_SpaceTypes SET
            Name                = ISNULL(@Name,                Name),
            Description         = ISNULL(@Description,         Description),
            CategoryId          = ISNULL(@CategoryId,          CategoryId),
            Capacity            = ISNULL(@Capacity,            Capacity),
            HourlyAllowed       = ISNULL(@HourlyAllowed,       HourlyAllowed),
            AccountReceivableId = @AccountReceivableId,
            RentAccountId       = @RentAccountId,
            ServicesIncomeId    = @ServicesIncomeId,
            SalesTaxId          = @SalesTaxId,
            SecurityReceivedId  = @SecurityReceivedId,
            UpdatedBy           = @UpdatedById,
            UpdatedOn           = GETUTCDATE()
        WHERE Id = @Id;

        -- Cascade accounts to child spaces of this space type
        UPDATE dbo.WN_Spaces SET
            AccountReceivableId = @AccountReceivableId,
            RentAccountId       = @RentAccountId,
            ServicesIncomeId    = @ServicesIncomeId,
            SalesTaxId          = @SalesTaxId,
            SecurityReceivedId  = @SecurityReceivedId,
            SecurityRecivedId   = @SecurityReceivedId
        WHERE SpaceTypeId = @Id;

        -- Synchronize WN_ChargeTypeAccountMapping
        IF @RentAccountId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 1)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET RentAccountId = @RentAccountId WHERE ChargeTypeId = 1;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, RentAccountId, EffectiveFrom) VALUES (1, @RentAccountId, CAST(GETDATE() AS DATE));
        END

        IF @SecurityReceivedId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 2)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET SecurityReceivedId = @SecurityReceivedId WHERE ChargeTypeId = 2;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, SecurityReceivedId, EffectiveFrom) VALUES (2, @SecurityReceivedId, CAST(GETDATE() AS DATE));
        END

        IF @SalesTaxId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 3)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET SalesTaxId = @SalesTaxId WHERE ChargeTypeId = 3;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, SalesTaxId, EffectiveFrom) VALUES (3, @SalesTaxId, CAST(GETDATE() AS DATE));
        END

        IF @ServicesIncomeId IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.WN_ChargeTypeAccountMapping WHERE ChargeTypeId = 4)
                UPDATE dbo.WN_ChargeTypeAccountMapping SET ServicesIncomeId = @ServicesIncomeId WHERE ChargeTypeId = 4;
            ELSE
                INSERT INTO dbo.WN_ChargeTypeAccountMapping (ChargeTypeId, ServicesIncomeId, EffectiveFrom) VALUES (4, @ServicesIncomeId, CAST(GETDATE() AS DATE));
        END

        IF @AccountReceivableId IS NOT NULL
        BEGIN
            UPDATE dbo.WN_ChargeTypeAccountMapping 
            SET AccountReceivableId = @AccountReceivableId 
            WHERE ChargeTypeId IN (1, 2, 3, 4);
        END

        COMMIT TRANSACTION;

        SELECT Id, IdGUID, Capacity FROM dbo.WN_SpaceTypes WHERE Id = @Id;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_ToggleAccessStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_ToggleAccessStatus]
    @BookingDetailId INT,
    @CustomerId INT,
    @PersonId INT = NULL,
    @IsEnabled BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF @PersonId IS NOT NULL
    BEGIN
        -- Single Person Toggle
        IF EXISTS (SELECT 1 FROM [dbo].[WN_AccessStatus] WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId)
        BEGIN
            UPDATE [dbo].[WN_AccessStatus]
            SET IsEnabled = @IsEnabled,
                RevokedAt = CASE WHEN @IsEnabled = 0 THEN SYSUTCDATETIME() ELSE NULL END
            WHERE BookingDetailId = @BookingDetailId AND PersonId = @PersonId;
        END
        ELSE
        BEGIN
            INSERT INTO [dbo].[WN_AccessStatus] (BookingDetailId, PersonId, CustomerId, IsEnabled, GrantedAt, RevokedAt)
            VALUES (@BookingDetailId, @PersonId, @CustomerId, @IsEnabled, SYSUTCDATETIME(), CASE WHEN @IsEnabled = 0 THEN SYSUTCDATETIME() ELSE NULL END);
        END
    END
    ELSE
    BEGIN
        -- Batch Toggle for all attendants under CustomerId & BookingDetailId
        UPDATE [dbo].[WN_AccessStatus]
        SET IsEnabled = @IsEnabled,
            RevokedAt = CASE WHEN @IsEnabled = 0 THEN SYSUTCDATETIME() ELSE NULL END
        WHERE BookingDetailId = @BookingDetailId AND CustomerId = @CustomerId;
    END

    SELECT @@ROWCOUNT AS RowsUpdated;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_Delete]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_Delete]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET IsActive = 0 WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetByEmail]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_GetByEmail]
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        u.*,
        c.Id        AS CustomerId,
        c.IdGUID    AS CustomerGuid
    FROM dbo.WN_Users u
    LEFT JOIN dbo.WN_Customers c ON c.Email = u.Email AND c.IsActive = 1
    WHERE u.Email = @Email
      AND u.IsActive = 1;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetByGuid]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_GetByGuid]
    @IdGUID UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT
            u.Id,
            u.IdGUID,
            u.Email,
            u.Name,
            u.PhoneNumber,
            u.CreatedOn,
            u.RoleId,
            u.CompanyId,
            u.Status
        FROM dbo.WN_Users u WITH (NOLOCK)
        WHERE u.IdGUID = @IdGUID;
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetById]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 10. WN_Users_GetById
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_GetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        u.Id, u.IdGUID AS PublicId, u.IdGUID, u.Email, u.Name, u.PhoneNumber,
        u.RoleId, u.CompanyId, co.CompanyName AS CompanyName,
        u.CityId, ci.Description AS CityName,
        u.Address, u.CnicOrPassport, u.AvatarUrl,
        u.IsActive, u.Notes, u.CreatedOn
    FROM dbo.WN_Users u WITH (NOLOCK)
    LEFT JOIN dbo.Company co WITH (NOLOCK) ON co.Id = u.CompanyId
    LEFT JOIN dbo.City         ci WITH (NOLOCK) ON ci.Id = u.CityId
    WHERE u.Id = @Id;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetByPublicId]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 11. WN_Users_GetByPublicId
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_GetByPublicId]
    @PublicId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        u.Id, u.IdGUID AS PublicId, u.IdGUID, u.Email, u.Name, u.PhoneNumber,
        u.RoleId, u.CompanyId, co.CompanyName AS CompanyName,
        u.CityId, ci.Description AS CityName,
        u.Address, u.CnicOrPassport, u.AvatarUrl,
        u.IsActive, u.Notes, u.CreatedOn
    FROM dbo.WN_Users u WITH (NOLOCK)
    LEFT JOIN dbo.Company co WITH (NOLOCK) ON co.Id = u.CompanyId
    LEFT JOIN dbo.City         ci WITH (NOLOCK) ON ci.Id = u.CityId
    WHERE u.IdGUID = @PublicId;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetHistory]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- Fix WN_Users_GetHistory
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_GetHistory]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        b.Id            AS BookingId,
        b.IdGUID        AS BookingPublicId,
        s.Name          AS SpaceName,
        s.Code          AS SpaceCode,
        st.Description  AS SpaceTypeName,
        b.StartOn, b.EndOn,
        bs.Label        AS BookingStatus,
        bp.Label        AS BillingPeriod,
        sp.SeatPrice * s.Capacity AS RoomPrice,
        b.CreatedOn     AS BookedOn
    FROM dbo.WN_Bookings        b  WITH (NOLOCK)
    JOIN dbo.WN_Spaces          s  WITH (NOLOCK) ON s.Id  = b.SpaceId
    JOIN dbo.WN_SpaceTypes      st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
    JOIN dbo.WN_SpacePricing    sp WITH (NOLOCK) ON sp.Id = b.PricingId
    JOIN dbo.WN_BillingPeriods  bp WITH (NOLOCK) ON bp.Id = sp.BillingPeriodId
    JOIN dbo.WN_BookingStatuses bs WITH (NOLOCK) ON bs.Id = b.BookingStatusId
    WHERE b.UserId    = @UserId
      AND (b.IsDeleted = 0 OR b.IsDeleted IS NULL)
    ORDER BY b.CreatedOn DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_GetList]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 5. Update Stored Procedure dbo.WN_Users_GetList
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_GetList]
    @Page       INT           = 1,
    @Limit      INT           = 20,
    @Search     NVARCHAR(255) = NULL,
    @LocationId INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    SELECT
        u.Id,
        u.IdGUID AS PublicId,
        u.Email,
        u.Name,
        u.PhoneNumber,
        u.RoleId,
        u.CompanyId,
        co.CompanyName  AS CompanyName,
        u.CityId,
        ci.Description  AS CityName,
        u.Address,
        u.CnicOrPassport,
        u.AvatarUrl,
        u.IsActive,
        u.Notes,
        u.CreatedOn,
        u.LocationId,
        l.Name          AS LocationName,
        COUNT(*) OVER () AS TotalCount
    FROM dbo.WN_Users u WITH (NOLOCK)
    LEFT JOIN dbo.Company      co WITH (NOLOCK) ON co.Id = u.CompanyId
    LEFT JOIN dbo.City         ci WITH (NOLOCK) ON ci.Id = u.CityId
    LEFT JOIN dbo.WN_Locations l  WITH (NOLOCK) ON l.Id = u.LocationId
    WHERE u.IsActive = 1
      AND (@LocationId IS NULL OR u.LocationId = @LocationId)
      AND (@Search IS NULL OR @Search = ''
           OR u.Name        LIKE '%' + @Search + '%'
           OR u.Email       LIKE '%' + @Search + '%'
           OR u.PhoneNumber LIKE '%' + @Search + '%')
    ORDER BY u.CreatedOn DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_Insert]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 3. Update Stored Procedure dbo.WN_Users_Insert
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
    @LocationId     INT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WN_Users (
        IdGUID, Email, PasswordHash, Name, PhoneNumber,
        RoleId, CompanyId, CityId, Address, CnicOrPassport,
        AvatarUrl, Notes, IsActive, CreatedOn, CreatedById, LocationId
    ) VALUES (
        NEWID(), @Email, @PasswordHash, @Name, @PhoneNumber,
        @RoleId, @CompanyId, @CityId, @Address, @CnicOrPassport,
        @AvatarUrl, @Notes, 1, GETUTCDATE(), @CreatedById, @LocationId
    );

    SELECT 
        u.Id, u.IdGUID AS PublicId, u.IdGUID, u.Email, u.Name, u.PhoneNumber,
        u.RoleId, u.CompanyId, u.CityId, u.Address, u.CnicOrPassport, u.AvatarUrl,
        u.IsActive, u.Notes, u.CreatedOn, u.LocationId
    FROM dbo.WN_Users u WITH (NOLOCK)
    WHERE u.Id = SCOPE_IDENTITY();
END;
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_SetRole]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_SetRole]
    @Id     INT,
    @RoleId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET RoleId = @RoleId WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_SetStatus]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE [dbo].[WN_Users_SetStatus]
    @Id       INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET IsActive = @IsActive WHERE Id = @Id;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_Users_Update]    Script Date: 30/09/2026 1:23:18 pm ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- 4. Update Stored Procedure dbo.WN_Users_Update
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
    @LocationId     INT            = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_Users SET
        Name           = ISNULL(@Name,           Name),
        PhoneNumber    = ISNULL(@PhoneNumber,     PhoneNumber),
        CompanyId      = ISNULL(@CompanyId,       CompanyId),
        CityId         = ISNULL(@CityId,          CityId),
        Address        = ISNULL(@Address,         Address),
        CnicOrPassport = ISNULL(@CnicOrPassport,  CnicOrPassport),
        AvatarUrl      = ISNULL(@AvatarUrl,       AvatarUrl),
        Notes          = ISNULL(@Notes,           Notes),
        LocationId     = ISNULL(@LocationId,      LocationId)
    WHERE Id = @Id;
    SELECT @@ROWCOUNT AS AffectedRows;
END;
GO

/****** Object:  StoredProcedure [dbo].[WN_HIK_AccessSuspension_Run]    Script Date: 05/10/2026 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.WN_HIK_AccessSuspension_Run
    @Today DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @Today = ISNULL(@Today, CAST(GETDATE() AS DATE));
    -- Status IDs from dbo.OrderStatus (old invoices keep legacy 1/2/4, so both are accepted)
    DECLARE @UnPaid INT = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Un Paid' ORDER BY Id), @Overdue INT = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Challan Expire' ORDER BY Id),
            @Paid INT = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Paid' ORDER BY Id), @Partial INT = (SELECT TOP 1 Id FROM dbo.OrderStatus WITH (NOLOCK) WHERE LTRIM(RTRIM(Description)) = 'Partial' ORDER BY Id);
    -- Bookings with something overdue right now
    DECLARE @Overdue TABLE (BookingId INT PRIMARY KEY, InvoiceId INT NULL, Reason NVARCHAR(200));
    -- a) Unpaid / Overdue invoices past their due date
    INSERT INTO @Overdue (BookingId, InvoiceId, Reason)
    SELECT i.BookingId, MIN(i.Id),
           CONCAT('Challan ', MIN(i.InvoiceNumber), ' overdue (due ', CONVERT(VARCHAR(10), MIN(i.DueOn), 23), ')')
      FROM SAC400.dbo.WN_Invoices i WITH (NOLOCK)
     WHERE i.StatusId IN (1, 4, @UnPaid, @Overdue) AND i.BookingId IS NOT NULL
       AND i.DueOn IS NOT NULL AND i.DueOn < @Today
     GROUP BY i.BookingId;
    -- b) Booking challans past their expiry with nothing paid. Expiry = later of WN_Challans.ValidUntil
    --    and WN_Bookings.ValidityDate (extended on the Challan Validity page).
    INSERT INTO @Overdue (BookingId, InvoiceId, Reason)
    SELECT c.BookingId, NULL,
           CONCAT('Booking challan ', MIN(c.ChallanNumber), ' expired (valid until ', CONVERT(VARCHAR(10), MAX(x.Expiry), 23), ')')
      FROM SAC400.dbo.WN_Challans c WITH (NOLOCK)
      LEFT JOIN SAC400.dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = c.BookingId
      LEFT JOIN SAC400.dbo.WN_vw_BookingSummary v WITH (NOLOCK) ON v.BookingId = c.BookingId
      CROSS APPLY (SELECT CAST(CASE WHEN b.ValidityDate > c.ValidUntil OR c.ValidUntil IS NULL THEN b.ValidityDate ELSE c.ValidUntil END AS DATE) AS Expiry) x
     WHERE c.BookingId IS NOT NULL
       AND ISNULL(c.StatusId, 1) NOT IN (3, 4)
       AND x.Expiry IS NOT NULL AND x.Expiry < @Today
       AND ISNULL(v.TotalPaidAmount, 0) = 0
       AND NOT EXISTS (SELECT 1 FROM SAC400.dbo.WN_Payments p WITH (NOLOCK)
                        WHERE (p.TransactionRef = c.ChallanNumber OR p.BookingIdInt = c.BookingId)
                          AND p.StatusId IN (2, @Paid, @Partial))
       AND NOT EXISTS (SELECT 1 FROM @Overdue o WHERE o.BookingId = c.BookingId)
     GROUP BY c.BookingId;
    -- 1) Open a suspension for overdue bookings with none open yet
    INSERT INTO SAC400.dbo.WN_HIK_BookingAccessSuspensions (BookingId, InvoiceId, Reason)
    SELECT o.BookingId, o.InvoiceId, o.Reason
      FROM @Overdue o
     WHERE NOT EXISTS (SELECT 1 FROM SAC400.dbo.WN_HIK_BookingAccessSuspensions s WITH (NOLOCK)
                        WHERE s.BookingId = o.BookingId AND s.ResolvedAt IS NULL);
    -- 2) Close suspensions once nothing is overdue for the booking (paid / partial / voided / cancelled)
    UPDATE s
       SET ResolvedAt = SYSDATETIME(), ResolvedReason = 'Challan paid'
      FROM SAC400.dbo.WN_HIK_BookingAccessSuspensions s
     WHERE s.ResolvedAt IS NULL
       AND NOT EXISTS (SELECT 1 FROM @Overdue o WHERE o.BookingId = s.BookingId);
    -- 3) Bookings whose machine state must change (block / unblock)
    SELECT x.Id AS SuspensionId, x.BookingId, x.ShouldBlock
      FROM (SELECT s.Id, s.BookingId, s.MachinesBlocked,
                   CAST(CASE WHEN s.ResolvedAt IS NULL AND (s.OverrideUntil IS NULL OR s.OverrideUntil < @Today)
                             THEN 1 ELSE 0 END AS BIT) AS ShouldBlock
              FROM SAC400.dbo.WN_HIK_BookingAccessSuspensions s WITH (NOLOCK)
             WHERE s.ResolvedAt IS NULL OR s.MachinesBlocked = 1) x
     WHERE x.ShouldBlock <> x.MachinesBlocked;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_AccessSuspension_SetApplied]    Script Date: 05/10/2026 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.WN_HIK_AccessSuspension_SetApplied
    @SuspensionId INT,
    @MachinesBlocked BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE SAC400.dbo.WN_HIK_BookingAccessSuspensions SET MachinesBlocked = @MachinesBlocked WHERE Id = @SuspensionId;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_AccessSuspension_GetByBookingDetail]    Script Date: 05/10/2026 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.WN_HIK_AccessSuspension_GetByBookingDetail
    @BookingDetailId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 s.Id AS SuspensionId, s.BookingId, s.SuspendedAt, s.Reason, s.OverrideUntil, s.MachinesBlocked,
           i.InvoiceNumber, i.DueOn, i.GrandTotal, ISNULL(i.GrandTotal - i.PaidTotal, 0) AS BalanceDue,
           (SELECT TOP 1 o.CreatedByEmail FROM SAC400.dbo.WN_HIK_BookingAccessOverrides o WITH (NOLOCK)
             WHERE o.SuspensionId = s.Id ORDER BY o.Id DESC) AS OverrideByEmail,
           (SELECT TOP 1 o.Reason FROM SAC400.dbo.WN_HIK_BookingAccessOverrides o WITH (NOLOCK)
             WHERE o.SuspensionId = s.Id ORDER BY o.Id DESC) AS OverrideReason
      FROM SAC400.dbo.WN_BookingDetails bd WITH (NOLOCK)
      JOIN SAC400.dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
      JOIN SAC400.dbo.WN_HIK_BookingAccessSuspensions s WITH (NOLOCK) ON s.BookingId = b.Id AND s.ResolvedAt IS NULL
      LEFT JOIN SAC400.dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = s.InvoiceId
     WHERE bd.Id = @BookingDetailId
     ORDER BY s.Id DESC;
END
GO
/****** Object:  StoredProcedure [dbo].[WN_HIK_AccessSuspension_Extend]    Script Date: 05/10/2026 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE OR ALTER PROCEDURE dbo.WN_HIK_AccessSuspension_Extend
    @BookingDetailId INT,
    @OverrideUntil   DATE,
    @Reason          NVARCHAR(500),
    @CreatedById     INT = NULL,
    @CreatedByEmail  NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SuspensionId INT, @BookingId INT;
    SELECT TOP 1 @SuspensionId = s.Id, @BookingId = s.BookingId
      FROM SAC400.dbo.WN_BookingDetails bd WITH (NOLOCK)
      JOIN SAC400.dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
      JOIN SAC400.dbo.WN_HIK_BookingAccessSuspensions s WITH (NOLOCK) ON s.BookingId = b.Id AND s.ResolvedAt IS NULL
     WHERE bd.Id = @BookingDetailId
     ORDER BY s.Id DESC;
    IF @SuspensionId IS NULL
    BEGIN
        SELECT CAST(NULL AS INT) AS SuspensionId, CAST(NULL AS INT) AS BookingId;  -- nothing suspended
        RETURN;
    END
    UPDATE SAC400.dbo.WN_HIK_BookingAccessSuspensions SET OverrideUntil = @OverrideUntil WHERE Id = @SuspensionId;
    INSERT INTO SAC400.dbo.WN_HIK_BookingAccessOverrides (SuspensionId, BookingId, OverrideUntil, Reason, CreatedById, CreatedByEmail)
    VALUES (@SuspensionId, @BookingId, @OverrideUntil, @Reason, @CreatedById, @CreatedByEmail);
    SELECT @SuspensionId AS SuspensionId, @BookingId AS BookingId;
END
GO

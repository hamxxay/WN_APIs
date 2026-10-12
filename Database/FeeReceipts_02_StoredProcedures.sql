-- ============================================================================
-- WorkNest Fee Receipts: Stored Procedures
-- All procedures created with SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. dbo.WN_FeeReceipts_GetUnpaidInvoices
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.WN_FeeReceipts_GetUnpaidInvoices
    @Search             NVARCHAR(200) = NULL,
    @LocationId         INT           = NULL,
    @AllowedLocationIds NVARCHAR(MAX) = NULL, -- CSV for location-bound staff; NULL for SuperAdmin
    @Top                INT           = 50
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    IF @Top IS NULL OR @Top <= 0 SET @Top = 50;
    IF @Top > 200 SET @Top = 200;

    -- Table variable for allowed location IDs if filtered
    DECLARE @AllowedLocs TABLE (LocId INT PRIMARY KEY);
    IF @AllowedLocationIds IS NOT NULL AND LTRIM(RTRIM(@AllowedLocationIds)) <> ''
    BEGIN
        INSERT INTO @AllowedLocs (LocId)
        SELECT DISTINCT CAST(value AS INT)
        FROM STRING_SPLIT(@AllowedLocationIds, ',')
        WHERE ISNUMERIC(value) = 1 AND CAST(value AS INT) > 0;
    END

    SELECT TOP (@Top)
        i.Id                                  AS InvoiceId,
        i.PublicId                            AS InvoiceIdGUID,
        i.InvoiceNumber,
        -- Display name formatted as {CustomerName}-{CustomerCode}-{InvoiceNumber}
        CONCAT(
            ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)),
            '-',
            ISNULL(c.Code, 'WN-N/A'),
            '-',
            i.InvoiceNumber
        ) AS DisplayName,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
        c.Code                                AS CustomerCode,
        u.Email                               AS CustomerEmail,
        c.PhoneNumber                         AS CustomerPhone,
        c.Company                             AS CustomerCompany,
        c.IdGUID                              AS CustomerIdGUID,
        i.IssuedOn,
        i.DueOn,
        ISNULL(i.RoomRentExclTax, 0)          AS RoomRentExclTax,
        ISNULL(i.ServiceCharges, 0)           AS ServiceCharges,
        ISNULL(i.TaxOnServiceCharges, 0)      AS TaxOnServiceCharges,
        ISNULL(i.WHTAmount, 0)                AS WHTAmount,
        ISNULL(i.WHTRate, 0)                  AS WHTRate,
        ISNULL(i.SecurityDepositAmount, 0)    AS SecurityDepositAmount,
        ISNULL(i.GrandTotal, 0)               AS GrandTotal,
        ISNULL(i.PaidTotal, 0)                AS PaidTotal,
        -- Net receivable amount the customer actually pays (GrandTotal - WHTAmount for WHT invoices)
        CASE 
            WHEN i.InvoiceTypeId = 6 OR ISNULL(i.WHTAmount, 0) > 0 
                THEN ISNULL(i.GrandTotal, 0) - ISNULL(i.WHTAmount, 0)
            ELSE ISNULL(i.GrandTotal, 0)
        END AS NetReceivableAmount,
        i.InvoiceTypeId,
        i.CurrencyCode,
        i.BookingId,
        i.UserId,
        loc.Id                                AS LocationId,
        loc.Name                              AS LocationName,
        loc.CompanyId,
        loc.BranchId
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = ISNULL(s.LocationId, u.LocationId)
    WHERE 
      -- Unpaid status (61 = Un Paid, 1 = Legacy Unpaid)
      i.StatusId IN (61, 1)
      -- Exclude Partial (12), Paid (62, 2)
      AND i.StatusId NOT IN (12, 62, 2)
      -- Exclude Cancelled (86, 5)
      AND i.StatusId NOT IN (86, 5)
      -- PaidTotal must be zero
      AND ISNULL(i.PaidTotal, 0) = 0
      -- No active fee receipt
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_FeeReceipts fr WITH (NOLOCK)
          WHERE fr.InvoiceIdGUID = i.PublicId AND fr.Status = 1
      )
      -- Single location filter (if provided)
      AND (@LocationId IS NULL OR ISNULL(s.LocationId, u.LocationId) = @LocationId)
      -- Staff allowed locations scope
      AND (@AllowedLocationIds IS NULL OR ISNULL(s.LocationId, u.LocationId) IN (SELECT LocId FROM @AllowedLocs))
      -- Search matching invoice number, customer name, email, or code
      AND (
          @Search IS NULL OR LTRIM(RTRIM(@Search)) = ''
          OR i.InvoiceNumber LIKE '%' + @Search + '%'
          OR c.Code LIKE '%' + @Search + '%'
          OR c.FirstName LIKE '%' + @Search + '%'
          OR c.LastName LIKE '%' + @Search + '%'
          OR u.Email LIKE '%' + @Search + '%'
          OR u.Name LIKE '%' + @Search + '%'
      )
    ORDER BY i.Id DESC;
END;
GO

-- ----------------------------------------------------------------------------
-- 2. dbo.WN_FeeReceipts_GetAccounts
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.WN_FeeReceipts_GetAccounts
    @CompanyId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    -- Determine if the company has dedicated cash/bank accounts in AccountsCOA
    DECLARE @HasCompanyAccounts BIT = 0;
    IF @CompanyId IS NOT NULL AND @CompanyId > 0
    BEGIN
        IF EXISTS (
            SELECT 1 FROM dbo.AccountsCOA WITH (NOLOCK)
            WHERE CompanyId = @CompanyId 
              AND (AccountNature IN ('Cash in Hand', 'Bank Account', 'Petty Cash', 'POS Cash', 'Bank Credit Card Account')
                   OR AccountsCategoryId = 21 
                   OR Description LIKE '%Cash%' 
                   OR Description LIKE '%Bank%')
        )
            SET @HasCompanyAccounts = 1;
    END

    SELECT 
        a.Id                              AS AccountId,
        LTRIM(RTRIM(a.Description))       AS Description,
        LTRIM(RTRIM(ISNULL(a.ShortDesc, ''))) AS ShortDesc,
        ISNULL(a.AccountNature, '')       AS AccountNature,
        a.CompanyId,
        -- Flag Cash In Hand to enable frontend preselection without hardcoding an ID
        CASE 
            WHEN a.Id = 101 THEN CAST(1 AS BIT)
            WHEN a.Description = 'CASH' THEN CAST(1 AS BIT)
            WHEN a.Description = 'CASH IN HAND' AND a.Id = 21 THEN CAST(1 AS BIT)
            WHEN a.AccountNature = 'Cash in Hand' AND a.Description NOT LIKE '%DONATION%' THEN CAST(1 AS BIT)
            ELSE CAST(0 AS BIT)
        END AS IsCashInHand
    FROM dbo.AccountsCOA a WITH (NOLOCK)
    WHERE 
        (
            -- Use company accounts if present
            (@HasCompanyAccounts = 1 AND a.CompanyId = @CompanyId)
            -- Otherwise fallback to default company (CompanyId 1 or universal accounts)
            OR (@HasCompanyAccounts = 0 AND (a.CompanyId = 1 OR a.IsForAllCompanies = 1 OR a.CompanyId IS NULL))
            -- Always include Account 101
            OR (a.Id = 101)
        )
        AND (
            a.Id = 101
            OR a.AccountNature IN ('Cash in Hand', 'Bank Account', 'Petty Cash', 'POS Cash', 'Bank Credit Card Account')
            OR a.AccountsCategoryId = 21
            OR a.Description LIKE '%Cash%'
            OR a.Description LIKE '%Bank%'
        )
        AND ISNULL(a.ActiveStatus, 1) = 1
        AND a.Description NOT LIKE '%DONATION%'
    ORDER BY 
        -- Account 101 (CASH) strictly first, then CASH IN HAND (21), then other cash accounts, then alphabetical
        CASE 
            WHEN a.Id = 101 THEN 0
            WHEN a.Id = 21 THEN 1
            WHEN a.Description = 'CASH' THEN 2
            WHEN a.Description = 'CASH IN HAND' THEN 3
            WHEN a.AccountNature = 'Cash in Hand' THEN 4
            ELSE 5
        END,
        a.Description ASC;
END;
GO

-- ----------------------------------------------------------------------------
-- 3. dbo.WN_FeeReceipts_Insert
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.WN_FeeReceipts_Insert
    @InvoiceIdGUID         UNIQUEIDENTIFIER,
    @CashInHandAccountId   INT,
    @PaymentType           VARCHAR(50)   = 'Cash',
    @ReceiptDate           DATE,
    @Remarks               NVARCHAR(500) = NULL,
    @CreatedById           INT,
    @AllowedLocationIds    NVARCHAR(MAX) = NULL -- CSV of allowed location IDs; NULL = super_admin (all locations)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        -- 1. Read invoice with UPDLOCK & HOLDLOCK
        DECLARE 
            @InvoiceId              INT,
            @InvoiceNumber          NVARCHAR(50),
            @IssuedOn               DATE,
            @StatusId               TINYINT,
            @GrandTotal             DECIMAL(18,4),
            @PaidTotal              DECIMAL(18,4),
            @InvoiceTypeId          INT,
            @RoomRentExclTax        DECIMAL(18,4),
            @ServiceCharges         DECIMAL(18,4),
            @TaxOnServiceCharges    DECIMAL(18,4),
            @WHTRate                DECIMAL(9,4),
            @WHTAmount              DECIMAL(18,4),
            @SecurityDepositAmount  DECIMAL(18,2),
            @BookingId              INT,
            @UserId                 INT,
            @BillingPeriodEnd       DATETIME2;

        SELECT 
            @InvoiceId             = i.Id,
            @InvoiceNumber         = i.InvoiceNumber,
            @IssuedOn              = i.IssuedOn,
            @StatusId              = i.StatusId,
            @GrandTotal            = ISNULL(i.GrandTotal, 0),
            @PaidTotal             = ISNULL(i.PaidTotal, 0),
            @InvoiceTypeId         = ISNULL(i.InvoiceTypeId, 1),
            @RoomRentExclTax       = ISNULL(i.RoomRentExclTax, 0),
            @ServiceCharges        = ISNULL(i.ServiceCharges, 0),
            @TaxOnServiceCharges   = ISNULL(i.TaxOnServiceCharges, 0),
            @WHTRate               = ISNULL(i.WHTRate, 0),
            @WHTAmount             = ISNULL(i.WHTAmount, 0),
            @SecurityDepositAmount = ISNULL(i.SecurityDepositAmount, 0),
            @BookingId             = i.BookingId,
            @UserId                = i.UserId,
            @BillingPeriodEnd      = i.BillingPeriodEnd
        FROM dbo.WN_Invoices i WITH (UPDLOCK, HOLDLOCK)
        WHERE i.PublicId = @InvoiceIdGUID;

        IF @InvoiceId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Invoice not found.', 16, 1);
            RETURN;
        END;

        -- 2. Reject cancelled
        IF @StatusId IN (86, 5)
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Invoice is cancelled and cannot receive fee receipts.', 16, 1);
            RETURN;
        END;

        -- 3. Reject already paid or partial
        IF @StatusId IN (62, 2) OR @PaidTotal > 0 OR @StatusId = 12
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Invoice is already paid or partially settled.', 16, 1);
            RETURN;
        END;

        -- 4. Must be eligible unpaid
        IF @StatusId NOT IN (61, 1)
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Invoice is not in an eligible unpaid status.', 16, 1);
            RETURN;
        END;

        -- 5. Reject if active receipt exists
        IF EXISTS (
            SELECT 1 FROM dbo.WN_FeeReceipts WITH (UPDLOCK, HOLDLOCK)
            WHERE InvoiceIdGUID = @InvoiceIdGUID AND Status = 1
        )
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('An active fee receipt already exists for this invoice.', 16, 1);
            RETURN;
        END;

        -- 6. Date validation: not in the future, not before invoice issue date
        DECLARE @Today DATE = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE);
        IF @ReceiptDate > @Today
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Receipt date cannot be in the future.', 16, 1);
            RETURN;
        END;

        IF @ReceiptDate < @IssuedOn
        BEGIN
            ROLLBACK TRANSACTION;
            DECLARE @IssuedOnStr NVARCHAR(30) = CONVERT(NVARCHAR(30), @IssuedOn, 23);
            RAISERROR('Receipt date cannot be earlier than invoice issue date (%s).', 16, 1, @IssuedOnStr);
            RETURN;
        END;

        -- 7. Resolve location, company and branch
        DECLARE @LocationId INT = NULL;
        IF @BookingId IS NOT NULL
        BEGIN
            SELECT TOP 1 @LocationId = s.LocationId
            FROM dbo.WN_Bookings b WITH (NOLOCK)
            JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
            WHERE b.Id = @BookingId;
        END;

        IF @LocationId IS NULL AND @UserId IS NOT NULL
        BEGIN
            SELECT TOP 1 @LocationId = s.LocationId
            FROM dbo.WN_Bookings b WITH (NOLOCK)
            JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
            WHERE b.UserId = @UserId
            ORDER BY b.Id DESC;
        END;

        IF @LocationId IS NULL AND @UserId IS NOT NULL
        BEGIN
            SELECT TOP 1 @LocationId = u.LocationId
            FROM dbo.WN_Users u WITH (NOLOCK)
            WHERE u.Id = @UserId;
        END;

        -- Location scoping validation for location-bound roles
        IF @AllowedLocationIds IS NOT NULL AND LTRIM(RTRIM(@AllowedLocationIds)) <> ''
        BEGIN
            IF @LocationId IS NULL OR @LocationId NOT IN (
                SELECT CAST(value AS INT) 
                FROM STRING_SPLIT(@AllowedLocationIds, ',') 
                WHERE ISNUMERIC(value) = 1
            )
            BEGIN
                ROLLBACK TRANSACTION;
                RAISERROR('User is not authorized to create receipts for invoices outside assigned location(s).', 16, 1);
                RETURN;
            END;
        END;

        DECLARE @CompanyId INT = NULL, @BranchId INT = NULL;
        IF @LocationId IS NOT NULL
        BEGIN
            SELECT TOP 1 
                @CompanyId = CompanyId, 
                @BranchId  = BranchId
            FROM dbo.WN_Locations WITH (NOLOCK)
            WHERE Id = @LocationId;
        END;

        -- 8. Validate account belongs to company / cash/bank
        IF NOT EXISTS (
            SELECT 1 FROM dbo.AccountsCOA WITH (NOLOCK)
            WHERE Id = @CashInHandAccountId
              AND (CompanyId = @CompanyId OR CompanyId = 1 OR @CompanyId IS NULL OR IsForAllCompanies = 1)
              AND (AccountNature IN ('Cash in Hand', 'Bank Account', 'Petty Cash', 'POS Cash', 'Bank Credit Card Account')
                   OR AccountsCategoryId = 21
                   OR Description LIKE '%Cash%'
                   OR Description LIKE '%Bank%')
        )
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Selected account is invalid or not an allowed cash/bank account for this invoice.', 16, 1);
            RETURN;
        END;

        -- 9. Resolve CustomerIdGUID
        DECLARE @CustomerIdGUID UNIQUEIDENTIFIER = NULL;
        SELECT TOP 1 @CustomerIdGUID = c.IdGUID
        FROM dbo.WN_Customers c WITH (NOLOCK)
        WHERE c.UserId = @UserId;

        -- 10. Derive Net TotalAmount (for WHT invoices = GrandTotal - WHTAmount)
        DECLARE @NetTotal DECIMAL(18,4);
        IF @InvoiceTypeId = 6 OR @WHTAmount > 0
            SET @NetTotal = @GrandTotal - @WHTAmount;
        ELSE
            SET @NetTotal = @GrandTotal;

        -- 11. Generate Code: FR-{CompanyId}-{BranchId}-{YYYY}-{NNNNN} with UPDLOCK/HOLDLOCK
        DECLARE @NextSeq INT;
        SELECT @NextSeq = ISNULL(MAX(CAST(RIGHT(Code, 5) AS INT)), 0) + 1
        FROM dbo.WN_FeeReceipts WITH (UPDLOCK, HOLDLOCK)
        WHERE ISNULL(CompanyId, 0) = ISNULL(@CompanyId, 0)
          AND ISNULL(BranchId, 0) = ISNULL(@BranchId, 0)
          AND YEAR(ReceiptDate) = YEAR(@ReceiptDate);

        DECLARE @Code VARCHAR(50);
        SET @Code = 'FR-' + CAST(ISNULL(@CompanyId, 0) AS VARCHAR(10))
                          + '-' + CAST(ISNULL(@BranchId, 0) AS VARCHAR(10))
                          + '-' + CAST(YEAR(@ReceiptDate) AS VARCHAR(4))
                          + '-' + RIGHT('00000' + CAST(@NextSeq AS VARCHAR(10)), 5);

        -- 12. Insert into WN_FeeReceipts
        DECLARE @ReceiptId INT;
        DECLARE @ReceiptGUID UNIQUEIDENTIFIER = NEWID();

        INSERT INTO dbo.WN_FeeReceipts (
            IdGUID,
            Code,
            CustomerIdGUID,
            InvoiceIdGUID,
            InvoiceTypeId,
            TotalAmount,
            RoomRentExclTax,
            ServiceCharges,
            TaxOnServiceCharges,
            WHTAmount,
            SecurityDepositAmount,
            CashInHandAccountId,
            PaymentType,
            BranchId,
            CompanyId,
            FeeStatus,
            Status,
            ReceiptDate,
            CreatedOn,
            UpdatedOn,
            CreatedById,
            Remarks,
            UpdatedById,
            VoidReason
        )
        VALUES (
            @ReceiptGUID,
            @Code,
            @CustomerIdGUID,
            @InvoiceIdGUID,
            @InvoiceTypeId,
            @NetTotal,
            @RoomRentExclTax,
            @ServiceCharges,
            @TaxOnServiceCharges,
            @WHTAmount,
            @SecurityDepositAmount,
            @CashInHandAccountId,
            ISNULL(NULLIF(LTRIM(RTRIM(@PaymentType)), ''), 'Cash'),
            @BranchId,
            @CompanyId,
            1, -- FeeStatus: 1 = Paid
            1, -- Status: 1 = Active
            @ReceiptDate,
            SYSUTCDATETIME(),
            NULL,
            @CreatedById,
            @Remarks,
            NULL,
            NULL
        );

        SET @ReceiptId = SCOPE_IDENTITY();

        -- 13. Update invoice: mark fully paid
        UPDATE dbo.WN_Invoices
        SET PaidTotal = @GrandTotal,
            StatusId  = 62, -- OrderStatus Paid
            UpdatedOn = SYSUTCDATETIME()
        WHERE Id = @InvoiceId;

        -- 14. Mirror access card activation if booking exists
        IF @BookingId IS NOT NULL AND @BookingId > 0
        BEGIN
            UPDATE dbo.WN_AccessCards
            SET Status    = 1, -- Active
                EndDate   = ISNULL(@BillingPeriodEnd, EndDate),
                UpdatedOn = SYSUTCDATETIME()
            WHERE BookingId = @BookingId;
        END;

        COMMIT TRANSACTION;

        -- Return the inserted receipt record
        EXEC dbo.WN_FeeReceipts_GetById @Id = @ReceiptId;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- ----------------------------------------------------------------------------
-- 4. dbo.WN_FeeReceipts_Void
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.WN_FeeReceipts_Void
    @ReceiptId    INT              = NULL,
    @ReceiptGUID  UNIQUEIDENTIFIER = NULL,
    @VoidReason   NVARCHAR(500),
    @UpdatedById  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    IF @VoidReason IS NULL OR LTRIM(RTRIM(@VoidReason)) = ''
    BEGIN
        RAISERROR('A void reason is required.', 16, 1);
        RETURN;
    END;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE 
            @TargetReceiptId INT,
            @TargetGUID      UNIQUEIDENTIFIER,
            @InvoiceGUID     UNIQUEIDENTIFIER,
            @CurrentStatus   TINYINT;

        SELECT 
            @TargetReceiptId = fr.Id,
            @TargetGUID      = fr.IdGUID,
            @InvoiceGUID     = fr.InvoiceIdGUID,
            @CurrentStatus   = fr.Status
        FROM dbo.WN_FeeReceipts fr WITH (UPDLOCK, HOLDLOCK)
        WHERE (@ReceiptId IS NOT NULL AND fr.Id = @ReceiptId)
           OR (@ReceiptGUID IS NOT NULL AND fr.IdGUID = @ReceiptGUID);

        IF @TargetReceiptId IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Fee receipt not found.', 16, 1);
            RETURN;
        END;

        IF @CurrentStatus = 0
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Fee receipt is already void.', 16, 1);
            RETURN;
        END;

        -- Update receipt to Void
        UPDATE dbo.WN_FeeReceipts
        SET Status      = 0, -- 0 = Void
            VoidReason  = @VoidReason,
            UpdatedById = @UpdatedById,
            UpdatedOn   = SYSUTCDATETIME()
        WHERE Id = @TargetReceiptId;

        -- Revert invoice paid status
        UPDATE dbo.WN_Invoices
        SET PaidTotal = 0,
            StatusId  = 61, -- OrderStatus Un Paid
            UpdatedOn = SYSUTCDATETIME()
        WHERE PublicId = @InvoiceGUID;

        COMMIT TRANSACTION;

        SELECT 
            1 AS IsSuccessful,
            'Fee receipt voided successfully and invoice restored to unpaid.' AS Message,
            @TargetReceiptId AS ReceiptId;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

-- ----------------------------------------------------------------------------
-- 5. dbo.WN_FeeReceipts_GetList
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.WN_FeeReceipts_GetList
    @Page               INT           = 1,
    @Limit              INT           = 10,
    @Search             NVARCHAR(200) = NULL,
    @LocationId         INT           = NULL,
    @Status             TINYINT       = NULL, -- 1: Active, 0: Void, NULL: All
    @AllowedLocationIds NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    IF @Page < 1 SET @Page = 1;
    IF @Limit < 1 SET @Limit = 10;
    DECLARE @Offset INT = (@Page - 1) * @Limit;

    DECLARE @AllowedLocs TABLE (LocId INT PRIMARY KEY);
    IF @AllowedLocationIds IS NOT NULL AND LTRIM(RTRIM(@AllowedLocationIds)) <> ''
    BEGIN
        INSERT INTO @AllowedLocs (LocId)
        SELECT DISTINCT CAST(value AS INT)
        FROM STRING_SPLIT(@AllowedLocationIds, ',')
        WHERE ISNUMERIC(value) = 1 AND CAST(value AS INT) > 0;
    END

    -- Total count
    SELECT COUNT(1) AS TotalCount
    FROM dbo.WN_FeeReceipts fr WITH (NOLOCK)
    LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.PublicId = fr.InvoiceIdGUID
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.IdGUID = fr.CustomerIdGUID
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = ISNULL(s.LocationId, u.LocationId)
    WHERE (@Status IS NULL OR fr.Status = @Status)
      AND (@LocationId IS NULL OR loc.Id = @LocationId)
      AND (@AllowedLocationIds IS NULL OR loc.Id IN (SELECT LocId FROM @AllowedLocs))
      AND (
          @Search IS NULL OR LTRIM(RTRIM(@Search)) = ''
          OR fr.Code LIKE '%' + @Search + '%'
          OR i.InvoiceNumber LIKE '%' + @Search + '%'
          OR c.Code LIKE '%' + @Search + '%'
          OR c.FirstName LIKE '%' + @Search + '%'
          OR c.LastName LIKE '%' + @Search + '%'
          OR u.Email LIKE '%' + @Search + '%'
      );

    -- Items
    SELECT 
        fr.Id,
        fr.IdGUID,
        fr.Code,
        fr.CustomerIdGUID,
        fr.InvoiceIdGUID,
        fr.InvoiceTypeId,
        fr.TotalAmount,
        fr.RoomRentExclTax,
        fr.ServiceCharges,
        fr.TaxOnServiceCharges,
        fr.WHTAmount,
        fr.SecurityDepositAmount,
        fr.CashInHandAccountId,
        coa.Description                     AS CashInHandAccountName,
        fr.PaymentType,
        fr.BranchId,
        fr.CompanyId,
        fr.FeeStatus,
        fr.Status,
        CASE fr.Status WHEN 1 THEN 'Active' WHEN 0 THEN 'Void' ELSE 'Unknown' END AS StatusLabel,
        fr.ReceiptDate,
        fr.Remarks,
        fr.VoidReason,
        fr.CreatedOn,
        fr.UpdatedOn,
        fr.CreatedById,
        creator.Name                        AS CreatedByName,
        fr.UpdatedById,
        updater.Name                        AS UpdatedByName,
        i.Id                                AS InvoiceId,
        i.InvoiceNumber,
        i.IssuedOn                          AS InvoiceIssuedOn,
        i.DueOn                             AS InvoiceDueOn,
        i.GrandTotal                        AS InvoiceGrandTotal,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
        c.Code                              AS CustomerCode,
        u.Email                             AS CustomerEmail,
        c.Company                           AS CustomerCompany,
        loc.Id                              AS LocationId,
        loc.Name                            AS LocationName
    FROM dbo.WN_FeeReceipts fr WITH (NOLOCK)
    LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.PublicId = fr.InvoiceIdGUID
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.IdGUID = fr.CustomerIdGUID
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = ISNULL(s.LocationId, u.LocationId)
    LEFT JOIN dbo.AccountsCOA coa WITH (NOLOCK) ON coa.Id = fr.CashInHandAccountId
    LEFT JOIN dbo.WN_Users creator WITH (NOLOCK) ON creator.Id = fr.CreatedById
    LEFT JOIN dbo.WN_Users updater WITH (NOLOCK) ON updater.Id = fr.UpdatedById
    WHERE (@Status IS NULL OR fr.Status = @Status)
      AND (@LocationId IS NULL OR loc.Id = @LocationId)
      AND (@AllowedLocationIds IS NULL OR loc.Id IN (SELECT LocId FROM @AllowedLocs))
      AND (
          @Search IS NULL OR LTRIM(RTRIM(@Search)) = ''
          OR fr.Code LIKE '%' + @Search + '%'
          OR i.InvoiceNumber LIKE '%' + @Search + '%'
          OR c.Code LIKE '%' + @Search + '%'
          OR c.FirstName LIKE '%' + @Search + '%'
          OR c.LastName LIKE '%' + @Search + '%'
          OR u.Email LIKE '%' + @Search + '%'
      )
    ORDER BY fr.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO

-- ----------------------------------------------------------------------------
-- 6. dbo.WN_FeeReceipts_GetById
-- ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.WN_FeeReceipts_GetById
    @Id     INT              = NULL,
    @IdGUID UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET QUOTED_IDENTIFIER ON;
    SET ANSI_NULLS ON;

    SELECT 
        fr.Id,
        fr.IdGUID,
        fr.Code,
        fr.CustomerIdGUID,
        fr.InvoiceIdGUID,
        fr.InvoiceTypeId,
        fr.TotalAmount,
        fr.RoomRentExclTax,
        fr.ServiceCharges,
        fr.TaxOnServiceCharges,
        fr.WHTAmount,
        fr.SecurityDepositAmount,
        fr.CashInHandAccountId,
        coa.Description                     AS CashInHandAccountName,
        fr.PaymentType,
        fr.BranchId,
        fr.CompanyId,
        fr.FeeStatus,
        fr.Status,
        CASE fr.Status WHEN 1 THEN 'Active' WHEN 0 THEN 'Void' ELSE 'Unknown' END AS StatusLabel,
        fr.ReceiptDate,
        fr.Remarks,
        fr.VoidReason,
        fr.CreatedOn,
        fr.UpdatedOn,
        fr.CreatedById,
        creator.Name                        AS CreatedByName,
        fr.UpdatedById,
        updater.Name                        AS UpdatedByName,
        i.Id                                AS InvoiceId,
        i.InvoiceNumber,
        i.IssuedOn                          AS InvoiceIssuedOn,
        i.DueOn                             AS InvoiceDueOn,
        i.GrandTotal                        AS InvoiceGrandTotal,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
        c.Code                              AS CustomerCode,
        u.Email                             AS CustomerEmail,
        c.PhoneNumber                       AS CustomerPhone,
        c.Company                           AS CustomerCompany,
        c.Address                           AS CustomerAddress,
        loc.Id                              AS LocationId,
        loc.Name                            AS LocationName,
        loc.City                            AS LocationCity
    FROM dbo.WN_FeeReceipts fr WITH (NOLOCK)
    LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.PublicId = fr.InvoiceIdGUID
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.IdGUID = fr.CustomerIdGUID
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = ISNULL(s.LocationId, u.LocationId)
    LEFT JOIN dbo.AccountsCOA coa WITH (NOLOCK) ON coa.Id = fr.CashInHandAccountId
    LEFT JOIN dbo.WN_Users creator WITH (NOLOCK) ON creator.Id = fr.CreatedById
    LEFT JOIN dbo.WN_Users updater WITH (NOLOCK) ON updater.Id = fr.UpdatedById
    WHERE (@Id IS NOT NULL AND fr.Id = @Id)
       OR (@IdGUID IS NOT NULL AND fr.IdGUID = @IdGUID);
END;
GO

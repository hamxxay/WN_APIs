-- ============================================================================
-- WorkNest Stored Procedures for Invoice Management (All prefixed with WN_)
-- ============================================================================

-- 1. WN_GetInvoicesList & WN_GetInvoicesList
CREATE OR ALTER PROCEDURE dbo.WN_GetInvoicesList
    @Page INT = 1,
    @Limit INT = 10,
    @Search NVARCHAR(200) = NULL,
    @TypeId INT = NULL
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
    WHERE (@TypeId IS NULL OR @TypeId <= 0 OR i.InvoiceTypeId = @TypeId)
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
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
        u.Email AS CustomerEmail,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), '-') AS CompanyName,
        CASE i.StatusId 
            WHEN 1 THEN 'Unpaid' 
            WHEN 2 THEN 'Paid' 
            WHEN 3 THEN 'Partial' 
            WHEN 4 THEN 'Overdue' 
            ELSE 'Unknown' 
        END AS StatusLabel
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    WHERE (@TypeId IS NULL OR @TypeId <= 0 OR i.InvoiceTypeId = @TypeId)
      AND (@Search IS NULL OR @Search = '' 
           OR i.InvoiceNumber LIKE '%' + @Search + '%' 
           OR u.Email LIKE '%' + @Search + '%' 
           OR c.FirstName LIKE '%' + @Search + '%'
           OR c.LastName LIKE '%' + @Search + '%')
    ORDER BY i.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
END;
GO

-- 2. WN_GetStatementInvoicePdfData
CREATE OR ALTER PROCEDURE dbo.WN_GetStatementInvoicePdfData
    @InvoiceId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result 1: Header / Vendor / Customer / Center Info
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
        ISNULL(i.CurrencyCode, 'PKR') AS CurrencyCode,
        COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), ''), NULLIF(LTRIM(RTRIM(ucomp.CompanyName)), ''), '-') AS AccountName,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), u.Name) AS AttnName,
        COALESCE(c.Address, u.Address, '') AS BillingAddress,
        ISNULL(c.Code, 'WN' + RIGHT('00000' + CAST(ISNULL(c.Id, i.UserId) AS VARCHAR(10)), 5)) AS AccountNumber,
        ISNULL(c.CnicOrPassport, '') AS SntnNtnNic,
        ISNULL(loc.Name, 'WorkNest') AS CenterName,
        ISNULL(comp.CompanyName, 'WorkNest Coworking Spaces (Pvt) Ltd') AS VendorLegalName,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(comp.AddressLine1, '') + ' ' + ISNULL(comp.AddressLine2, ''))), ''), ISNULL(loc.Address, '3rd Floor EOBI Building-II, I-8 Markaz, Islamabad')) AS VendorAddress,
        ISNULL(comp.Contact, '+92 309 9771774 / +92 308 0256000') AS VendorPhone,
        ISNULL(comp.Fax, '+92 51 8439201') AS VendorFax,
        ISNULL(comp.NTN, '7492018-3') AS VendorNtn,
        ISNULL(bd.AppliedChargePercentage, 10.00) AS AppliedChargePercentage,
        ISNULL(bd.AppliedTaxPercentage, 16.00) AS AppliedTaxPercentage,
        ISNULL(bd.SupportChargeAmount, 0.00) AS SupportChargeAmount,
        COALESCE(NULLIF(b.SecurityDepositRequired, 0), NULLIF(i.SecurityDepositAmount, 0), ISNULL(bd.SecurityDeposit, 0)) AS SecurityDepositAmount
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON (c.UserId = i.UserId OR c.Id = i.UserId OR (u.Email IS NOT NULL AND c.Email = u.Email)) AND (c.IsActive = 1 OR c.IsActive IS NULL)
    LEFT JOIN dbo.Company ucomp WITH (NOLOCK) ON ucomp.Id = u.CompanyId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    LEFT JOIN dbo.WN_Locations loc WITH (NOLOCK) ON loc.Id = s.LocationId
    LEFT JOIN dbo.Company comp WITH (NOLOCK) ON comp.Id = ISNULL(NULLIF(loc.CompanyId, 0), 486)
    WHERE i.Id = @InvoiceId;

    -- Result 2: Line Items
    SELECT 
        l.ChargeTypeId,
        l.Description,
        (l.Quantity * l.UnitPrice - l.DiscountAmount) AS PriceExclVat,
        l.TaxAmount AS VatAmount,
        l.LineTotal AS TotalInclVat,
        l.TaxRate,
        ISNULL(ct.Label, 'Business Support Services') AS CategoryName
    FROM dbo.WN_InvoiceLines l WITH (NOLOCK)
    LEFT JOIN dbo.WN_ChargeTypes ct WITH (NOLOCK) ON ct.Id = l.ChargeTypeId
    WHERE l.InvoiceId = @InvoiceId
    ORDER BY l.SortOrder, l.Id;

    -- Result 3: Prior Balances
    DECLARE @UserId INT;
    SELECT @UserId = UserId FROM dbo.WN_Invoices WHERE Id = @InvoiceId;

    SELECT 
        ISNULL(SUM(GrandTotal - PaidTotal), 0) AS PriorBalance,
        ISNULL(SUM(PaidTotal), 0) AS PaymentReceived
    FROM dbo.WN_Invoices WITH (NOLOCK)
    WHERE UserId = @UserId AND Id < @InvoiceId;

    -- Result 4: Bank Details
    SELECT TOP 1 Description AS BankName, ShortDesc AS BankAccountNumber
    FROM dbo.AccountsCOA WITH (NOLOCK)
    WHERE AccountNature = 'Bank' OR Description LIKE '%Bank%';
END;
GO

-- 3. WN_GetInvoiceDetailsById
CREATE OR ALTER PROCEDURE dbo.WN_GetInvoiceDetailsById
    @InvoiceId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result 1: Invoice Header
    SELECT 
        i.*,
        ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))), ''), ISNULL(u.Name, u.Email)) AS CustomerName,
        u.Email AS UserEmail,
        s.Name AS SpaceName,
        s.Code AS SpaceCode,
        b.StartOn,
        b.EndOn
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    WHERE i.Id = @InvoiceId;

    -- Result 2: Invoice Lines
    SELECT * 
    FROM dbo.WN_InvoiceLines WITH (NOLOCK)
    WHERE InvoiceId = @InvoiceId 
    ORDER BY SortOrder, Id;
END;
GO

-- 4. WN_RecordInvoicePayment
CREATE OR ALTER PROCEDURE dbo.WN_RecordInvoicePayment
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
    DECLARE @NewStatusId TINYINT = 1; -- 1 = Unpaid (from WN_StatusLookUp)
    DECLARE @MonthsCovered INT = 0;

    IF @MonthlyRent > 0
        SET @MonthsCovered = FLOOR(@NewPaidTotal / @MonthlyRent);

    IF @NewPaidTotal >= @GrandTotal
    BEGIN
        SET @NewStatusId = 2; -- 2 = Paid
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
        SET @NewStatusId = 3; -- 3 = Partial
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

-- 4b. WN_ReRestrictLapsedPartialPayments
CREATE OR ALTER PROCEDURE dbo.WN_ReRestrictLapsedPartialPayments
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE ac
    SET ac.Status = 2, -- 2 = Restricted (from WN_StatusLookUp)
        ac.UpdatedOn = SYSUTCDATETIME()
    FROM dbo.WN_AccessCards ac
    INNER JOIN dbo.WN_Invoices i ON i.BookingId = ac.BookingId
    WHERE ac.EndDate < SYSUTCDATETIME()
      AND ac.Status = 1 -- Currently active
      AND i.StatusId != 2; -- Not fully paid
END;
GO

-- 5. WN_CreateCustomInvoice
CREATE OR ALTER PROCEDURE dbo.WN_CreateCustomInvoice
    @UserId INT,
    @IssuedOn DATE,
    @DueOn DATE,
    @CurrencyCode NVARCHAR(10) = 'PKR',
    @Notes NVARCHAR(MAX) = NULL,
    @SubTotal DECIMAL(18,4),
    @DiscountTotal DECIMAL(18,4),
    @TaxTotal DECIMAL(18,4),
    @BookingId INT = NULL,
    @GrandTotal DECIMAL(18,4),
    @InvoiceId INT OUTPUT,
    @InvoiceNumber NVARCHAR(50) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @NextId INT;
    SELECT @NextId = ISNULL(MAX(Id), 0) + 1 FROM dbo.WN_Invoices;
    SET @InvoiceNumber = 'INV-' + CAST(YEAR(GETDATE()) AS VARCHAR(4)) + '-' + RIGHT('00000' + CAST(@NextId AS VARCHAR(10)), 5);

    INSERT INTO dbo.WN_Invoices (
        PublicId, InvoiceNumber, UserId, BookingId, IssuedOn, DueOn, SubTotal, DiscountTotal,
        TaxTotal, GrandTotal, PaidTotal, CurrencyCode, StatusId, Notes, CreatedOn, InvoiceTypeId, SecurityDepositAmount
    )
    VALUES (
        NEWID(), @InvoiceNumber, @UserId, @BookingId, @IssuedOn, @DueOn, @SubTotal, @DiscountTotal,
        @TaxTotal, @GrandTotal, 0, ISNULL(NULLIF(@CurrencyCode, ''), 'PKR'), 1, @Notes, SYSUTCDATETIME(), 1, 0
    );

    SET @InvoiceId = SCOPE_IDENTITY();

    COMMIT TRANSACTION;
END;
GO

-- 6. WN_GetInvoiceEmailData
CREATE OR ALTER PROCEDURE dbo.WN_GetInvoiceEmailData
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
        ISNULL(NULLIF(i.SecurityDepositAmount, 0), ISNULL(NULLIF(b.SecurityDepositRequired, 0), ISNULL(bd.SecurityDeposit, 0))) AS SecurityDepositAmount,
        ISNULL(NULLIF(u.Email, ''), ISNULL(NULLIF(c.Email, ''), '')) AS TargetEmail,
        ISNULL(NULLIF(c.Company, ''), ISNULL(NULLIF(u.Name, ''), 'Valued Customer')) AS CustomerName,
        ISNULL(s.Name, 'WorkNest Workspace') AS SpaceName
    FROM dbo.WN_Invoices i WITH (NOLOCK)
    LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = i.UserId
    LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = i.UserId
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = i.BookingId
    LEFT JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.BookingGuid = b.IdGUID
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
    WHERE i.Id = @InvoiceId;
END;
GO

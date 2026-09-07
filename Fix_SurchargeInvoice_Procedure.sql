USE [SAC400];
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE [dbo].[WN_sp_CreateSurchargeInvoice]
    @BookingDetailId INT,
    @PersonId INT,
    @CustomerId INT,
    @SurchargeAmount DECIMAL(10,2),
    @ExcessSeatCount INT
AS
BEGIN
    SET NOCOUNT ON;
    SET ANSI_NULLS ON;
    SET QUOTED_IDENTIFIER ON;

    DECLARE @UserId INT = NULL;
    DECLARE @CustomerEmail NVARCHAR(255) = '';
    DECLARE @CustomerName NVARCHAR(255) = '';
    DECLARE @CustomerAddress NVARCHAR(255) = '';
    DECLARE @CustomerNtn NVARCHAR(50) = '';
    DECLARE @SpaceName NVARCHAR(255) = 'Workspace';
    DECLARE @PersonName NVARCHAR(150) = '';
    DECLARE @PersonIdNumber VARCHAR(30) = '';
    DECLARE @TaxRate DECIMAL(5,2) = 16.00;
    DECLARE @SupportChargeRate DECIMAL(5,2) = 10.00;
    DECLARE @SubTotal DECIMAL(18,4) = @SurchargeAmount;
    DECLARE @SupportChargeAmount DECIMAL(18,4) = 0;
    DECLARE @TaxTotal DECIMAL(18,4) = 0;
    DECLARE @GrandTotal DECIMAL(18,4) = @SurchargeAmount;
    DECLARE @NextId INT = 1;
    DECLARE @InvoiceId INT;
    DECLARE @InvoiceNumber NVARCHAR(50);
    DECLARE @BillingStart DATETIME = NULL;
    DECLARE @BillingEnd DATETIME = NULL;

    -- Fetch customer & user info
    SELECT TOP 1
        @UserId = c.UserId,
        @CustomerEmail = ISNULL(c.Email, ''),
        @CustomerName = ISNULL(NULLIF(c.Company, ''), ISNULL(c.FirstName + ' ' + ISNULL(c.LastName, ''), 'Valued Customer')),
        @CustomerAddress = ISNULL(c.Address, ''),
        @CustomerNtn = ISNULL(c.CnicOrPassport, '')
    FROM [dbo].[WN_Customers] c WITH (NOLOCK)
    WHERE c.Id = @CustomerId;

    IF @UserId IS NULL AND @CustomerEmail <> ''
        SELECT TOP 1 @UserId = Id FROM [dbo].[WN_Users] WHERE Email = @CustomerEmail;

    IF @UserId IS NULL SET @UserId = 1; -- Fallback system user ID

    -- Fetch BookingDetail & Space info & tax percentage
    SELECT TOP 1
        @SpaceName = ISNULL(bd.SpaceName, 'Workspace'),
        @TaxRate = ISNULL(bd.AppliedTaxPercentage, 16.00),
        @SupportChargeRate = ISNULL(bd.AppliedChargePercentage, 10.00),
        @BillingStart = bd.StartDateTime,
        @BillingEnd = bd.EndDateTime
    FROM [dbo].[WN_BookingDetails] bd WITH (NOLOCK)
    WHERE bd.Id = @BookingDetailId;

    -- Calculate Tax on 10% support charges (room rent tax model)
    SET @SubTotal = @SurchargeAmount;
    SET @SupportChargeAmount = ROUND(@SubTotal * (@SupportChargeRate / 100.0), 2);
    SET @TaxTotal = ROUND(@SupportChargeAmount * (@TaxRate / 100.0), 2);
    SET @GrandTotal = @SubTotal + @TaxTotal;

    -- Fetch Person info
    SELECT TOP 1
        @PersonName = Name,
        @PersonIdNumber = IdNumber
    FROM [dbo].[WN_Persons] WITH (NOLOCK)
    WHERE PersonId = @PersonId;

    -- Generate Invoice Number
    SELECT @NextId = ISNULL(MAX(Id), 0) + 1 FROM [dbo].[WN_Invoices];
    SET @InvoiceNumber = 'INV-SUR-' + CAST(YEAR(GETDATE()) AS VARCHAR(4)) + '-' + RIGHT('00000' + CAST(@NextId AS VARCHAR(10)), 5);

    -- Insert into WN_Invoices (BookingId set to NULL to prevent unique index collision with main booking invoice)
    INSERT INTO [dbo].[WN_Invoices] (
        PublicId,
        InvoiceNumber,
        UserId,
        BookingId,
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
        InvoiceTypeId,
        SecurityDepositAmount,
        BillingPeriodStart,
        BillingPeriodEnd
    )
    VALUES (
        NEWID(),
        @InvoiceNumber,
        @UserId,
        NULL,
        CAST(SYSUTCDATETIME() AS DATE),
        DATEADD(day, 7, CAST(SYSUTCDATETIME() AS DATE)),
        @SubTotal,
        0,
        @TaxTotal,
        @GrandTotal,
        0,
        'PKR',
        1, -- Unpaid
        'Attendant Capacity Overage Surcharge - ' + @PersonName + ' (' + @PersonIdNumber + ')',
        SYSUTCDATETIME(),
        1, -- Custom Invoice
        0,
        @BillingStart,
        @BillingEnd
    );

    SET @InvoiceId = SCOPE_IDENTITY();

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
        1,
        'Capacity Overage Surcharge for Attendant: ' + @PersonName + ' (' + @PersonIdNumber + ') in ' + @SpaceName,
        ISNULL(@ExcessSeatCount, 1),
        (@SubTotal / CASE WHEN ISNULL(@ExcessSeatCount, 1) > 0 THEN @ExcessSeatCount ELSE 1 END),
        0,
        (@TaxRate / 100.0),
        1
    );

    SELECT 
        @InvoiceId AS InvoiceId, 
        @InvoiceNumber AS InvoiceNumber,
        @CustomerEmail AS TargetEmail,
        @CustomerName AS CustomerName,
        @CustomerAddress AS CustomerAddress,
        @CustomerNtn AS CustomerNtn,
        @PersonName AS AttendantName,
        @PersonIdNumber AS AttendantIdNumber,
        @SpaceName AS SpaceName,
        @SubTotal AS SubTotal,
        @SupportChargeAmount AS SupportChargeAmount,
        @SupportChargeRate AS SupportChargeRate,
        @TaxRate AS TaxRate,
        @TaxTotal AS TaxTotal,
        @GrandTotal AS GrandTotal,
        @BillingStart AS BillingPeriodStart,
        @BillingEnd AS BillingPeriodEnd;
END;
GO

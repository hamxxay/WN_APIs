USE [SAC400];
GO

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

    -- Result 1: Total Count
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

    -- Result 2: Paginated Items
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

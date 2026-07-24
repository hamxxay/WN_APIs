-- Run this in SSMS to fix WN_Payments_GetMyList
CREATE OR ALTER PROCEDURE dbo.WN_Payments_GetMyList
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserGuid UNIQUEIDENTIFIER;
    SELECT @UserGuid = IdGUID FROM dbo.WN_Users WHERE Id = @UserId;

    SELECT
        p.Id,
        CAST(p.IdGUID AS NVARCHAR(36))  AS IdGuid,
        p.Amount,
        p.PaymentMethod,
        p.PaymentStatus,
        p.TransactionRef,
        p.CreatedAt                     AS PaidAt,
        b.ChallanNumber,
        b.ValidityDate                  AS Validity,
        b.StartDateTime,
        b.EndDateTime,
        s.Name                          AS SpaceName
    FROM dbo.WN_Payments p WITH (NOLOCK)
    LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK)
        ON CAST(b.IdGUID AS NVARCHAR(36)) = CAST(p.BookingId AS NVARCHAR(36))
    LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK)
        ON CAST(s.IdGUID AS NVARCHAR(36)) = CAST(b.SpaceGuid AS NVARCHAR(36))
    WHERE p.UserId = @UserGuid
    AND ISNULL(p.PaymentStatus, '') <> 'Deleted'
    ORDER BY p.CreatedAt DESC;
END
GO

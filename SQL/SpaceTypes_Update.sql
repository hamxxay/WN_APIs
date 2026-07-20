CREATE OR ALTER PROCEDURE dbo.WN_SpaceTypes_Update
    @IdGUID       UNIQUEIDENTIFIER,
    @Name         NVARCHAR(200),
    @Capacity     INT,
    @HourlyAllowed BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.WN_SpaceTypes
    SET
        Description    = @Name,
        Capacity       = @Capacity,
        HourlyAllowed  = @HourlyAllowed
    WHERE IdGUID = @IdGUID;
END
GO

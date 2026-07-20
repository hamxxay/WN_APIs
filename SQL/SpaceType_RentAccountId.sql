-- Add RentAccountId to WN_SpaceTypes if not exists
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WN_SpaceTypes') AND name = 'RentAccountId')
    ALTER TABLE dbo.WN_SpaceTypes ADD RentAccountId INT NULL;
GO

-- Update WN_SpaceTypes_GetList to return RentAccountId
ALTER PROCEDURE dbo.WN_SpaceTypes_GetList
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        st.Id,
        st.IdGUID,
        st.Name,
        st.Capacity,
        st.HourlyAllowed,
        st.IsActive,
        st.RentAccountId
    FROM dbo.WN_SpaceTypes st
    WHERE st.IsDeleted = 0
    ORDER BY st.Name;
END
GO

-- First run this to see what BookingStatus values exist:
-- SELECT DISTINCT BookingStatus FROM dbo.WN_Bookings

-- From the existing SP WN_Bookings_UpdateStatus, status is passed as int.
-- Typically: 1=Confirmed, 2=Cancelled, 3=Completed
-- Adjust the value below if different.

ALTER PROCEDURE dbo.WN_Spaces_GetVacant
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        s.Id,
        s.IdGUID,
        s.Name,
        s.Code,
        s.Status,
        s.PricePerDay,
        s.PricePerHour,
        s.PricePerMonth,
        l.Name         AS LocationName,
        st.Description AS SpaceTypeName
    FROM dbo.WN_Spaces s
    LEFT JOIN dbo.WN_Locations  l  ON l.IdGUID  = s.LocationId
    LEFT JOIN dbo.WN_SpaceTypes st ON st.IdGUID = s.SpaceTypeId
    WHERE s.Status = 1
      AND NOT EXISTS (
          SELECT 1 FROM dbo.WN_Bookings b
          WHERE b.SpaceGuid = s.IdGUID
            AND b.BookingStatus = 1
            AND b.StartDateTime <= GETDATE()
            AND b.EndDateTime   >= GETDATE()
      )
    ORDER BY TRY_CAST(s.Code AS INT);
END

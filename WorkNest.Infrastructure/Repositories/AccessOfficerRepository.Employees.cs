using Microsoft.Data.SqlClient;
using WorkNest.Application.DTOs.AccessOfficer;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>The Employees tab of the Access page: every person on a confirmed, running booking of the caller's locations.</summary>
    public partial class AccessOfficerRepository
    {
        // Same people, bookings, location and customer rules as dbo.WN_AccessOfficer_Lookup (AO_01), for everyone at once.
        // Added on / by: the customer's request (WN_BookingEmployeeRequests) for this booking and person, if any.
        private const string EmployeesSql = @"
SET NOCOUNT ON;
DECLARE @Today DATE = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Pakistan Standard Time' AS DATE);
DECLARE @All BIT = CASE WHEN NULLIF(LTRIM(RTRIM(@LocationIds)), N'') IS NULL THEN 1 ELSE 0 END;
DECLARE @Scope TABLE (Id INT PRIMARY KEY);
INSERT INTO @Scope (Id)
SELECT DISTINCT TRY_CAST(value AS INT) FROM STRING_SPLIT(ISNULL(@LocationIds, N''), N',') WHERE TRY_CAST(value AS INT) > 0;
DECLARE @M TABLE (BookingDetailId INT NOT NULL, PersonId INT NOT NULL, OnRoom BIT NOT NULL, PRIMARY KEY (BookingDetailId, PersonId));
INSERT INTO @M (BookingDetailId, PersonId, OnRoom)
SELECT ba.BookingDetailId, ba.PersonId, 1
  FROM dbo.WN_BookingAttendants ba WITH (NOLOCK)
 WHERE ba.AssignedTo IS NULL OR ba.AssignedTo >= @Today
 GROUP BY ba.BookingDetailId, ba.PersonId;
INSERT INTO @M (BookingDetailId, PersonId, OnRoom)
SELECT x.BookingDetailId, x.PersonId, 0
  FROM (SELECT COALESCE(r.BookingDetailId, rl.Id) AS BookingDetailId, r.PersonId
          FROM dbo.WN_BookingEmployeeRequests r WITH (NOLOCK)
          JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = r.BookingId
         OUTER APPLY (SELECT TOP 1 d.Id FROM dbo.WN_BookingDetails d WITH (NOLOCK)
                       WHERE d.BookingGuid = b.IdGUID AND ISNULL(d.IsDeleted, 0) = 0
                       ORDER BY CASE WHEN d.FeeType LIKE N'%Rent%' THEN 0 ELSE 1 END, d.Id DESC) rl
         WHERE r.Status = N'Pending') x
 WHERE x.BookingDetailId IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM @M m WHERE m.BookingDetailId = x.BookingDetailId AND m.PersonId = x.PersonId)
 GROUP BY x.BookingDetailId, x.PersonId;
SELECT m.PersonId, p.Name, p.IdType, p.IdNumber, p.Phone, p.Email,
       COALESCE(cu.Customer, NULLIF(LTRIM(RTRIM(bd.CustomerName)), N''), bd.CustomerEmail) AS Customer,
       b.Id AS BookingId, m.BookingDetailId,
       COALESCE(NULLIF(LTRIM(RTRIM(s.Name)), N''), NULLIF(LTRIM(RTRIM(bd.SpaceName)), N''), bd.SpaceCode) AS Room,
       bd.SpaceCode, bd.SpaceName, s.LocationId, l.Name AS Location,
       COALESCE(bd.StartDateTime, b.StartOn) AS StartOn,
       req.AddedAt, req.AddedByEmail, CAST(CASE WHEN req.Id IS NULL THEN 0 ELSE 1 END AS BIT) AS AddedByCustomer,
       m.OnRoom,
       CAST(CASE WHEN m.OnRoom = 1 THEN ISNULL(acc.IsEnabled, 1) ELSE 0 END AS BIT) AS AccessEnabled,
       hpm.MachineID AS MachineId
  FROM @M m
  JOIN dbo.WN_BookingDetails bd WITH (NOLOCK) ON bd.Id = m.BookingDetailId
  JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = bd.BookingGuid
  JOIN dbo.WN_Persons p WITH (NOLOCK) ON p.PersonId = m.PersonId
  LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
  LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
  LEFT JOIN dbo.WN_HIK_PersonMap hpm WITH (NOLOCK) ON hpm.PersonId = m.PersonId
 OUTER APPLY (SELECT TOP 1 a.IsEnabled FROM dbo.WN_AccessStatus a WITH (NOLOCK)
               WHERE a.BookingDetailId = m.BookingDetailId AND a.PersonId = m.PersonId ORDER BY a.Id DESC) acc
 OUTER APPLY (SELECT TOP 1 r.Id, r.AddedAt, u.Email AS AddedByEmail
                FROM dbo.WN_BookingEmployeeRequests r WITH (NOLOCK)
                LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = r.AddedByUserId
               WHERE r.BookingId = b.Id AND r.PersonId = m.PersonId
               ORDER BY r.Id DESC) req
 OUTER APPLY (SELECT TOP 1 COALESCE(NULLIF(LTRIM(RTRIM(c.Company)), N''),
                                    NULLIF(LTRIM(RTRIM(CONCAT(c.FirstName, N' ', c.LastName))), N''), c.Email) AS Customer
                FROM dbo.WN_Customers c WITH (NOLOCK)
               WHERE (NULLIF(b.CustomerCode, N'') IS NOT NULL AND c.Code = b.CustomerCode)
                  OR (NULLIF(b.CustomerCode, N'') IS NULL AND c.UserId = b.UserId)
               ORDER BY c.Id) cu
 WHERE b.BookingStatusId = 33 AND ISNULL(b.IsDeleted, 0) = 0 AND ISNULL(bd.IsDeleted, 0) = 0
   AND COALESCE(bd.EndDateTime, b.EndOn) >= @Today
   AND (@All = 1 OR s.LocationId IN (SELECT Id FROM @Scope))
 ORDER BY req.AddedAt DESC, COALESCE(bd.StartDateTime, b.StartOn) DESC, m.BookingDetailId DESC, m.PersonId;";

        public async Task<List<AccessOfficerEmployeeRow>> EmployeesAsync(string? locationIds)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand(EmployeesSql, c) { CommandTimeout = 60 };
                cmd.Parameters.Add("@LocationIds", System.Data.SqlDbType.NVarChar, -1).Value = (object?)locationIds ?? DBNull.Value;

                var list = new List<AccessOfficerEmployeeRow>();
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    list.Add(new AccessOfficerEmployeeRow
                    {
                        PersonId = Int(r, "PersonId") ?? 0,
                        Name = Str(r, "Name") ?? "",
                        IdType = Str(r, "IdType"),
                        IdNumber = Str(r, "IdNumber"),
                        Phone = Str(r, "Phone"),
                        Email = Str(r, "Email"),
                        Customer = Str(r, "Customer"),
                        BookingId = Int(r, "BookingId") ?? 0,
                        BookingDetailId = Int(r, "BookingDetailId") ?? 0,
                        Room = Str(r, "Room") ?? "",
                        SpaceCode = Str(r, "SpaceCode"),
                        SpaceName = Str(r, "SpaceName"),
                        LocationId = Int(r, "LocationId"),
                        Location = Str(r, "Location"),
                        StartOn = Date(r, "StartOn"),
                        AddedAtUtc = r["AddedAt"] is DBNull ? null : DateTime.SpecifyKind(Convert.ToDateTime(r["AddedAt"]), DateTimeKind.Utc),
                        AddedByEmail = Str(r, "AddedByEmail"),
                        AddedByCustomer = Convert.ToBoolean(r["AddedByCustomer"]),
                        OnRoom = Convert.ToBoolean(r["OnRoom"]),
                        AccessEnabled = Convert.ToBoolean(r["AccessEnabled"]),
                        MachineId = Str(r, "MachineId")
                    });
                }
                return list;
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208)
            {
                throw new AccessOfficerDatabaseUpdateMissingException(ex);
            }
        }
    }
}

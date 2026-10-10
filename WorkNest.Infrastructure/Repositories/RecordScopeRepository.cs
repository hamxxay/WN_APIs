using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Location of a single record (booking / payment / agreement / customer / user), read with NOLOCK. Every query
    /// returns one row per record match with the location (NULL when none), so "no rows" means "no such record".
    /// </summary>
    public class RecordScopeRepository : IRecordScopeRepository
    {
        private readonly string _connectionString;

        public RecordScopeRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private const string BookingSql = @"
            SELECT s.LocationId FROM dbo.WN_Bookings b WITH (NOLOCK)
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
             WHERE (@Id IS NOT NULL AND b.Id = @Id) OR (@Guid IS NOT NULL AND b.IdGUID = @Guid);";

        // A payment's location comes from its booking, or from the booking of the invoice it pays.
        private const string PaymentSql = @"
            SELECT COALESCE(s.LocationId, s2.LocationId) AS LocationId
              FROM dbo.WN_Payments p WITH (NOLOCK)
              LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = p.BookingIdInt
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
              LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = p.InvoiceId
              LEFT JOIN dbo.WN_Bookings b2 WITH (NOLOCK) ON b2.Id = i.BookingId
              LEFT JOIN dbo.WN_Spaces s2 WITH (NOLOCK) ON s2.Id = b2.SpaceId
             WHERE (@Id IS NOT NULL AND p.Id = @Id) OR (@Guid IS NOT NULL AND p.IdGUID = @Guid);";

        // An agreement's location: the quoted space, or the booking it was converted to.
        private const string AgreementSql = @"
            SELECT COALESCE(sb.LocationId, sq.LocationId) AS LocationId
              FROM dbo.WN_Agreements a WITH (NOLOCK)
              LEFT JOIN dbo.WN_Quotations q WITH (NOLOCK) ON q.Id = a.QuotationId
              LEFT JOIN dbo.WN_Spaces sq WITH (NOLOCK) ON sq.Id = q.SpaceId
              LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = a.BookingId
              LEFT JOIN dbo.WN_Spaces sb WITH (NOLOCK) ON sb.Id = b.SpaceId
             WHERE a.Id = @Id;";

        // A customer belongs to the locations of their bookings and of their user account (same rule as KYC).
        private const string CustomerSql = @"
            DECLARE @C TABLE (Id INT, Code NVARCHAR(50), UserId INT);
            INSERT INTO @C SELECT c.Id, c.Code, c.UserId FROM dbo.WN_Customers c WITH (NOLOCK)
             WHERE (@Id IS NOT NULL AND c.Id = @Id) OR (@Guid IS NOT NULL AND c.IdGUID = @Guid);
            SELECT CAST(NULL AS INT) AS LocationId FROM @C
            UNION ALL
            SELECT u.LocationId FROM @C c JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = c.UserId
            UNION ALL
            SELECT s.LocationId FROM @C c
              JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.CustomerCode = c.Code OR (c.UserId IS NOT NULL AND b.UserId = c.UserId)
              JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId;";

        // A user's main location plus their extra locations (WN_UserLocations).
        private const string UserSql = @"
            DECLARE @U TABLE (Id INT, LocationId INT NULL);
            INSERT INTO @U SELECT u.Id, u.LocationId FROM dbo.WN_Users u WITH (NOLOCK)
             WHERE (@Id IS NOT NULL AND u.Id = @Id) OR (@Guid IS NOT NULL AND u.IdGUID = @Guid) OR (@Email IS NOT NULL AND u.Email = @Email);
            SELECT LocationId FROM @U;
            IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
                SELECT ul.LocationId FROM dbo.WN_UserLocations ul WITH (NOLOCK) JOIN @U x ON x.Id = ul.UserId;";

        // A booking detail (one space of a booking) belongs to the location of its space.
        private const string BookingDetailSql = @"
            SELECT s.LocationId
              FROM dbo.WN_BookingDetails d WITH (NOLOCK)
              LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = d.BookingGuid
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
             WHERE d.Id = @Id;";

        public async Task<List<int>?> GetLocationIdsAsync(RecordKind kind, string key)
        {
            int? id = int.TryParse(key, out var n) ? n : null;
            Guid? guid = Guid.TryParse(key, out var g) ? g : null;
            string? email = kind == RecordKind.User && id == null && guid == null && key.Contains('@') ? key : null;
            if (id == null && guid == null && email == null) return null;
            if (kind is RecordKind.Agreement or RecordKind.BookingDetail && id == null) return null;

            var sql = kind switch
            {
                RecordKind.Booking => BookingSql,
                RecordKind.Payment => PaymentSql,
                RecordKind.Agreement => AgreementSql,
                RecordKind.Customer => CustomerSql,
                RecordKind.BookingDetail => BookingDetailSql,
                _ => UserSql
            };
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand(sql, c);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = (object?)guid ?? DBNull.Value;
            cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = (object?)email ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync();
            var found = false;
            var locations = new HashSet<int>();
            do
            {
                while (await r.ReadAsync())
                {
                    found = true;
                    if (!r.IsDBNull(0)) locations.Add(r.GetInt32(0));
                }
            } while (await r.NextResultAsync());
            return found ? locations.ToList() : null;
        }

        public async Task<int?> GetPaymentIdByPublicIdAsync(Guid publicId)
        {
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TOP 1 Id FROM dbo.WN_Payments WITH (NOLOCK) WHERE IdGUID = @Guid;", c);
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = publicId;
            return await cmd.ExecuteScalarAsync() is int id ? id : null;
        }

        public async Task<HashSet<int>> GetPaymentIdsInLocationsAsync(IEnumerable<int> locationIds)
        {
            var ids = locationIds.Distinct().ToList();
            var result = new HashSet<int>();
            if (ids.Count == 0) return result;
            await using var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            // Location ids are ints from the caller's claims, never user text.
            await using var cmd = new SqlCommand($@"
                SELECT p.Id
                  FROM dbo.WN_Payments p WITH (NOLOCK)
                  LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = p.BookingIdInt
                  LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                  LEFT JOIN dbo.WN_Invoices i WITH (NOLOCK) ON i.Id = p.InvoiceId
                  LEFT JOIN dbo.WN_Bookings b2 WITH (NOLOCK) ON b2.Id = i.BookingId
                  LEFT JOIN dbo.WN_Spaces s2 WITH (NOLOCK) ON s2.Id = b2.SpaceId
                 WHERE COALESCE(s.LocationId, s2.LocationId) IN ({string.Join(",", ids)});", c);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) result.Add(r.GetInt32(0));
            return result;
        }
    }
}

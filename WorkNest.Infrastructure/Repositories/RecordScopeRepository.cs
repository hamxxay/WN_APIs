using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Location of a single record (booking / payment / agreement / customer / user / quotation / invoice / space /
    /// location), read with NOLOCK. Every query
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
            SELECT COALESCE(s.LocationId, i.LocationId, s2.LocationId) AS LocationId
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

        // A user's main location plus their extra locations (WN_UserLocations). Customers (role "general") have
        // neither, so they also belong to the locations of the spaces of their bookings (same rule as CustomerSql:
        // bookings made by the user or under a customer code linked to the user). Deleted bookings don't count.
        // Two branches (by user, by customer code) instead of one OR join, so each can use its index.
        private const string UserLocationsTail = @"
            SELECT LocationId FROM @U;
            IF OBJECT_ID(N'dbo.WN_UserLocations', N'U') IS NOT NULL
                SELECT ul.LocationId FROM dbo.WN_UserLocations ul WITH (NOLOCK) JOIN @U x ON x.Id = ul.UserId;
            SELECT s.LocationId FROM @U x
              JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.UserId = x.Id
              JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
             WHERE x.IsGeneral = 1 AND ISNULL(b.IsDeleted, 0) = 0
            UNION ALL
            SELECT s.LocationId FROM @U x
              JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.UserId = x.Id
              JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.CustomerCode = c.Code
              JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
             WHERE x.IsGeneral = 1 AND ISNULL(b.IsDeleted, 0) = 0;";

        private const string UserSql = @"
            DECLARE @U TABLE (Id INT, LocationId INT NULL, IsGeneral BIT);
            INSERT INTO @U SELECT u.Id, u.LocationId, CASE WHEN r.RoleName = N'general' THEN 1 ELSE 0 END
              FROM dbo.WN_Users u WITH (NOLOCK) LEFT JOIN dbo.WN_Roles r WITH (NOLOCK) ON r.Id = u.RoleId
             WHERE (@Id IS NOT NULL AND u.Id = @Id) OR (@Guid IS NOT NULL AND u.IdGUID = @Guid) OR (@Email IS NOT NULL AND u.Email = @Email);"
            + UserLocationsTail;

        // A membership (no location of its own) belongs to its member's locations (same rule as UserSql). The table is
        // the one the WN_Memberships_* procedures use: dbo.WN_Memberships when it has StartOn, else
        // dbo.WN_MemberShips_Temp; when that table is missing there are no rows ("not found", like any unknown record).
        // The first SELECT keeps a membership whose user is gone "found, no location".
        private const string MembershipSql = @"
            DECLARE @MT NVARCHAR(128) =
                CASE WHEN COL_LENGTH(N'dbo.WN_Memberships', N'StartOn') IS NOT NULL THEN N'dbo.WN_Memberships'
                     WHEN OBJECT_ID(N'dbo.WN_MemberShips_Temp', N'U') IS NOT NULL THEN N'dbo.WN_MemberShips_Temp' END;
            DECLARE @M TABLE (UserId INT NULL);
            IF @MT IS NOT NULL
            BEGIN
                DECLARE @MSql NVARCHAR(400) = N'SELECT m.UserId FROM ' + @MT + N' m WITH (NOLOCK) WHERE m.Id = @Id;';
                INSERT INTO @M EXEC sp_executesql @MSql, N'@Id INT', @Id;
            END
            SELECT CAST(NULL AS INT) AS LocationId FROM @M;
            DECLARE @U TABLE (Id INT, LocationId INT NULL, IsGeneral BIT);
            INSERT INTO @U SELECT u.Id, u.LocationId, CASE WHEN r.RoleName = N'general' THEN 1 ELSE 0 END
              FROM dbo.WN_Users u WITH (NOLOCK) LEFT JOIN dbo.WN_Roles r WITH (NOLOCK) ON r.Id = u.RoleId
             WHERE u.Id IN (SELECT m.UserId FROM @M m);"
            + UserLocationsTail;

        // An access card belongs to its own location (WN_AccessCards.LocationId; 0 = none), else its space's location,
        // else the location of its booking's space.
        private const string AccessCardSql = @"
            SELECT COALESCE(NULLIF(a.LocationId, 0), s.LocationId, sb.LocationId) AS LocationId
              FROM dbo.WN_AccessCards a WITH (NOLOCK)
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = a.SpaceId
              LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = a.BookingId
              LEFT JOIN dbo.WN_Spaces sb WITH (NOLOCK) ON sb.Id = b.SpaceId
             WHERE (@Id IS NOT NULL AND a.Id = @Id) OR (@Guid IS NOT NULL AND a.IdGUID = @Guid);";

        // A quotation belongs to the location of its quoted space (same rule as the quotation list).
        private const string QuotationSql = @"
            SELECT s.LocationId FROM dbo.WN_Quotations q WITH (NOLOCK)
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = q.SpaceId
             WHERE q.Id = @Id;";

        // An invoice belongs to its booking's location, its user's location (same rule as WN_GetInvoicesList) and, for a
        // booking-less custom invoice, the location it was raised for (WN_CustomInvoices.LocationId).
        // A GUID is the invoice's PublicId or the PublicId of its sales-tax invoice (WN_CustomerSTInvoice).
        // The custom-invoice part runs only when WN_CustomInvoices.LocationId exists (dynamic SQL, so a database
        // without the column still compiles the batch); the invoice ids passed to it are INTs from @I, never user text.
        private const string InvoiceSql = @"
            DECLARE @I TABLE (Id INT, BookingId INT NULL, UserId INT NULL, LocationId INT NULL);
            INSERT INTO @I SELECT i.Id, i.BookingId, i.UserId, i.LocationId FROM dbo.WN_Invoices i WITH (NOLOCK)
             WHERE (@Id IS NOT NULL AND i.Id = @Id)
                OR (@Guid IS NOT NULL AND (i.PublicId = @Guid OR i.Id IN (
                       SELECT st.CustomerInvoiceId FROM dbo.WN_CustomerSTInvoice st WITH (NOLOCK) WHERE st.PublicId = @Guid)));
            SELECT COALESCE(x.LocationId, s.LocationId) AS LocationId FROM @I x
              LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = x.BookingId
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
            UNION ALL
            SELECT u.LocationId FROM @I x JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = x.UserId;
            DECLARE @Ids NVARCHAR(MAX) = STUFF((SELECT N',' + CAST(x.Id AS NVARCHAR(12)) FROM @I x FOR XML PATH('')), 1, 1, N'');
            IF @Ids IS NOT NULL AND COL_LENGTH(N'dbo.WN_CustomInvoices', N'LocationId') IS NOT NULL
            BEGIN
                DECLARE @CiSql NVARCHAR(MAX) = N'SELECT ci.LocationId FROM dbo.WN_CustomInvoices ci WITH (NOLOCK) WHERE ci.InvoiceId IN (' + @Ids + N');';
                EXEC sp_executesql @CiSql;
            END";

        // A booking detail (one space of a booking) belongs to the location of its space.
        private const string BookingDetailSql = @"
            SELECT s.LocationId
              FROM dbo.WN_BookingDetails d WITH (NOLOCK)
              LEFT JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.IdGUID = d.BookingGuid
              LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
             WHERE d.Id = @Id;";

        // A space belongs to its own location (WN_Spaces.LocationId).
        private const string SpaceSql = @"
            SELECT s.LocationId FROM dbo.WN_Spaces s WITH (NOLOCK)
             WHERE (@Id IS NOT NULL AND s.Id = @Id) OR (@Guid IS NOT NULL AND s.IdGUID = @Guid);";

        // A location is its own scope: location-bound staff reach only the locations they are assigned to.
        private const string LocationSql = @"
            SELECT l.Id AS LocationId FROM dbo.WN_Locations l WITH (NOLOCK)
             WHERE (@Id IS NOT NULL AND l.Id = @Id) OR (@Guid IS NOT NULL AND l.IdGUID = @Guid);";

        public async Task<List<int>?> GetLocationIdsAsync(RecordKind kind, string key)
        {
            int? id = int.TryParse(key, out var n) ? n : null;
            Guid? guid = Guid.TryParse(key, out var g) ? g : null;
            string? email = kind == RecordKind.User && id == null && guid == null && key.Contains('@') ? key : null;
            if (id == null && guid == null && email == null) return null;
            if (kind is RecordKind.Agreement or RecordKind.BookingDetail or RecordKind.Quotation or RecordKind.Membership && id == null) return null;

            var sql = kind switch
            {
                RecordKind.Booking => BookingSql,
                RecordKind.Payment => PaymentSql,
                RecordKind.Agreement => AgreementSql,
                RecordKind.Customer => CustomerSql,
                RecordKind.BookingDetail => BookingDetailSql,
                RecordKind.Quotation => QuotationSql,
                RecordKind.Invoice => InvoiceSql,
                RecordKind.Space => SpaceSql,
                RecordKind.Location => LocationSql,
                RecordKind.AccessCard => AccessCardSql,
                RecordKind.Membership => MembershipSql,
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
                 WHERE COALESCE(s.LocationId, i.LocationId, s2.LocationId) IN ({string.Join(",", ids)});", c);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) result.Add(r.GetInt32(0));
            return result;
        }
    }
}

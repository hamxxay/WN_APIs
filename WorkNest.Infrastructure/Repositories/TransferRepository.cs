using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Transfer;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    public class TransferRepository : ITransferRepository
    {
        private readonly string _connectionString;

        public TransferRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

        /// <summary>TRF_02 has run: both transfer procedures take @PerSeatPrice (checked once a minute until it is there).</summary>
        private static volatile bool _pricingReady;
        private static DateTime _pricingCheckedAt = DateTime.MinValue;

        public async Task<bool> IsPerSeatPricingReadyAsync()
        {
            if (_pricingReady) return true;
            if ((DateTime.UtcNow - _pricingCheckedAt).TotalSeconds < 60) return false;
            _pricingCheckedAt = DateTime.UtcNow;
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.parameters WHERE object_id = OBJECT_ID(N'dbo.WN_Booking_TransferSpace') AND name = N'@PerSeatPrice') THEN 1 ELSE 0 END", conn);
            _pricingReady = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            return _pricingReady;
        }

        /// <summary>The transfer redesign has run: @ScheduleJson on the procedure and dbo.WN_SpaceTransferRequests (checked once a minute until it is there).</summary>
        private static volatile bool _redesignReady;
        private static DateTime _redesignCheckedAt = DateTime.MinValue;

        public async Task<bool> IsRedesignReadyAsync()
        {
            if (_redesignReady) return true;
            if ((DateTime.UtcNow - _redesignCheckedAt).TotalSeconds < 60) return false;
            _redesignCheckedAt = DateTime.UtcNow;
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.parameters WHERE object_id = OBJECT_ID(N'dbo.WN_Booking_TransferSpace') AND name = N'@ScheduleJson')
                                                        AND OBJECT_ID(N'dbo.WN_SpaceTransferRequests', N'U') IS NOT NULL THEN 1 ELSE 0 END", conn);
            _redesignReady = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            return _redesignReady;
        }

        public async Task<int> GetSecurityDepositMonthsAsync(int bookingId)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT ISNULL(SecurityDepositMonths, 0) FROM dbo.WN_Bookings WITH (NOLOCK) WHERE Id = @id", conn);
            cmd.Parameters.AddWithValue("@id", bookingId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v is DBNull ? 0 : Convert.ToInt32(v);
        }

        private static bool HasColumn(SqlDataReader r, string name)
        {
            for (int i = 0; i < r.FieldCount; i++) if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static decimal? DecOrNull(SqlDataReader r, string name) =>
            HasColumn(r, name) && !r.IsDBNull(r.GetOrdinal(name)) ? Convert.ToDecimal(r[name]) : null;

        public async Task<TransferPreviewResponseDto> GetTransferPreviewAsync(int bookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, decimal? perSeatPrice = null)
        {
            var response = new TransferPreviewResponseDto();

            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_Booking_TransferPreview", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@NewSpaceId", newSpaceId);
            cmd.Parameters.AddWithValue("@EffectiveDate", effectiveDate.Date);
            cmd.Parameters.AddWithValue("@TransferFee", transferFee);
            if (perSeatPrice is > 0 && await IsPerSeatPricingReadyAsync()) cmd.Parameters.AddWithValue("@PerSeatPrice", perSeatPrice.Value);

            await using var reader = await cmd.ExecuteReaderAsync();

            // Result Set 1: Overall Status
            if (await reader.ReadAsync())
            {
                response.IsValid = reader.GetBoolean(reader.GetOrdinal("IsValid"));
                if (!reader.IsDBNull(reader.GetOrdinal("ErrorMessage")))
                {
                    var msg = reader.GetString(reader.GetOrdinal("ErrorMessage"));
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        response.ValidationErrors.Add(msg);
                    }
                }
            }

            // Result Set 2: Current Booking & Target Space Summary
            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                response.CurrentBooking = new ActiveBookingSummaryDto
                {
                    BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                    CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                    CustomerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? null : reader.GetString(reader.GetOrdinal("CustomerEmail")),
                    CustomerCode = reader.IsDBNull(reader.GetOrdinal("CustomerCode")) ? null : reader.GetString(reader.GetOrdinal("CustomerCode")),
                    SpaceId = reader.GetInt32(reader.GetOrdinal("CurrentSpaceId")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("CurrentSpaceName")) ? null : reader.GetString(reader.GetOrdinal("CurrentSpaceName")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("CurrentSpaceCode")) ? null : reader.GetString(reader.GetOrdinal("CurrentSpaceCode")),
                    LocationId = reader.GetInt32(reader.GetOrdinal("LocationId")),
                    LocationName = reader.IsDBNull(reader.GetOrdinal("LocationName")) ? null : reader.GetString(reader.GetOrdinal("LocationName")),
                    StartOn = reader.GetDateTime(reader.GetOrdinal("StartOn")),
                    EndOn = reader.GetDateTime(reader.GetOrdinal("EndOn")),
                    MonthlyRent = reader.GetDecimal(reader.GetOrdinal("MonthlyRent")),
                    DiscountPercentage = reader.GetDecimal(reader.GetOrdinal("DiscountPercentage")),
                    OfferingType = reader.IsDBNull(reader.GetOrdinal("OfferingType")) ? null : reader.GetString(reader.GetOrdinal("OfferingType")),
                    SecurityDeposit = reader.GetDecimal(reader.GetOrdinal("SecurityDepositPaid"))
                };

                response.TargetSpace = new TargetSpaceSummaryDto
                {
                    SpaceId = reader.GetInt32(reader.GetOrdinal("TargetSpaceId")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("TargetSpaceName")) ? null : reader.GetString(reader.GetOrdinal("TargetSpaceName")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("TargetSpaceCode")) ? null : reader.GetString(reader.GetOrdinal("TargetSpaceCode")),
                    LocationId = response.CurrentBooking.LocationId,
                    LocationName = response.CurrentBooking.LocationName,
                    Capacity = Convert.ToInt32(reader["TargetCapacity"]),
                    MonthlyRate = reader.GetDecimal(reader.GetOrdinal("TargetMonthlyRate")),
                    MaxDiscountPercent = reader.GetDecimal(reader.GetOrdinal("TargetMaxDiscountPct")),
                    OfferingType = response.CurrentBooking.OfferingType,
                    OfferingTypeDiscountCap = reader.GetDecimal(reader.GetOrdinal("OfferingTypeDiscountCap")),
                    CurrentPerSeatPrice = DecOrNull(reader, "CurrentPerSeatPrice"),   // TRF_02 (null before the script)
                    ListPerSeatPrice = DecOrNull(reader, "ListPerSeatPrice"),
                    PerSeatPrice = DecOrNull(reader, "PerSeatPrice")
                };

                response.DiscountValidation = new DiscountValidationDto
                {
                    DiscountPercentage = response.CurrentBooking.DiscountPercentage,
                    OfferingTypeDiscountCap = response.TargetSpace.OfferingTypeDiscountCap,
                    IsExceeded = reader.GetBoolean(reader.GetOrdinal("DiscountExceeded")),
                    Message = reader.GetBoolean(reader.GetOrdinal("DiscountExceeded"))
                        ? $"Discount ({response.CurrentBooking.DiscountPercentage}%) exceeds target cap ({response.TargetSpace.OfferingTypeDiscountCap}%)."
                        : "Discount is within allowed offering type cap."
                };
            }

            // Result Set 3: Blocking Invoices
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    response.BlockingInvoices.Add(new BlockingInvoiceDto
                    {
                        InvoiceId = reader.GetInt32(reader.GetOrdinal("InvoiceId")),
                        InvoiceNumber = reader.GetString(reader.GetOrdinal("InvoiceNumber")),
                        Amount = reader.GetDecimal(reader.GetOrdinal("Amount")),
                        DueOn = reader.IsDBNull(reader.GetOrdinal("DueOn")) ? null : reader.GetDateTime(reader.GetOrdinal("DueOn")),
                        PublicId = reader.IsDBNull(reader.GetOrdinal("PublicId")) ? null : reader.GetGuid(reader.GetOrdinal("PublicId"))
                    });
                }
            }

            // Result Set 4: Settlement Result
            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                response.Settlement = new TransferSettlementResult
                {
                    PeriodStart = reader.GetDateTime(reader.GetOrdinal("PeriodStart")),
                    PeriodEnd = reader.GetDateTime(reader.GetOrdinal("PeriodEnd")),
                    EffectiveDate = reader.GetDateTime(reader.GetOrdinal("EffectiveDate")),
                    OldMonthlyRate = reader.GetDecimal(reader.GetOrdinal("OldMonthlyRate")),
                    NewMonthlyRate = reader.GetDecimal(reader.GetOrdinal("NewMonthlyRate")),
                    DaysUsed = reader.GetInt32(reader.GetOrdinal("DaysUsed")),
                    RemainingDays = reader.GetInt32(reader.GetOrdinal("RemainingDays")),
                    CreditAtA = reader.GetDecimal(reader.GetOrdinal("CreditAtA")),
                    RepricedAtB = reader.GetDecimal(reader.GetOrdinal("RepricedAtB")),
                    Adjustment = reader.GetDecimal(reader.GetOrdinal("Adjustment")),
                    TransferFee = reader.GetDecimal(reader.GetOrdinal("TransferFee")),
                    TotalDue = reader.GetDecimal(reader.GetOrdinal("TotalDue")),
                    Outcome = reader.GetString(reader.GetOrdinal("Outcome")),
                    OutcomeDescription = reader.GetString(reader.GetOrdinal("Outcome")) switch
                    {
                        "Billed" => $"Settlement adjustment of PKR {reader.GetDecimal(reader.GetOrdinal("Adjustment")):N2} will be invoiced.",
                        "CreditStored" => $"Prorated credit of PKR {Math.Abs(reader.GetDecimal(reader.GetOrdinal("Adjustment"))):N2} will be credited to the new booking.",
                        _ => "No rate adjustment required."
                    }
                };
            }

            return response;
        }

        public async Task<TransferExecuteResponseDto> ExecuteTransferAsync(
            int oldBookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, string notes, int? updatedById, string? userEmail, TransferExecutionArgs? args = null)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_Booking_TransferSpace", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@OldBookingId", oldBookingId);
            cmd.Parameters.AddWithValue("@NewSpaceId", newSpaceId);
            cmd.Parameters.AddWithValue("@EffectiveDate", effectiveDate.Date);
            cmd.Parameters.AddWithValue("@TransferFee", transferFee);
            cmd.Parameters.AddWithValue("@Notes", notes);
            cmd.Parameters.AddWithValue("@UpdatedById", (object?)updatedById ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UserEmail", (object?)userEmail ?? DBNull.Value);
            if (args != null)
            {
                if (args.PerSeatPrice is > 0 && await IsPerSeatPricingReadyAsync()) cmd.Parameters.AddWithValue("@PerSeatPrice", args.PerSeatPrice.Value);
                // The redesign parameters are sent only when the redesign is installed (before it the procedure does not know them).
                if (await IsRedesignReadyAsync())
                {
                    void P(string name, object? v) { if (v != null) cmd.Parameters.AddWithValue(name, v); }
                    P("@NewDeposit", args.NewDeposit); P("@SettleRent", args.SettleRent); P("@SettleServiceCharge", args.SettleServiceCharge);
                    P("@SettleTax", args.SettleTax); P("@CreditAmount", args.CreditAmount);
                    P("@NextBillingDate", args.NextBillingDate?.Date); P("@ScheduleJson", args.ScheduleJson); P("@TransferRequestId", args.TransferRequestId);
                }
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int transferId = reader.GetInt32(reader.GetOrdinal("TransferId"));
                int oldId = reader.GetInt32(reader.GetOrdinal("OldBookingId"));
                int newId = reader.GetInt32(reader.GetOrdinal("NewBookingId"));
                int? invId = reader.IsDBNull(reader.GetOrdinal("SettlementInvoiceId")) ? null : reader.GetInt32(reader.GetOrdinal("SettlementInvoiceId"));
                string? invNum = reader.IsDBNull(reader.GetOrdinal("SettlementInvoiceNumber")) ? null : reader.GetString(reader.GetOrdinal("SettlementInvoiceNumber"));
                decimal credit = reader.GetDecimal(reader.GetOrdinal("CreditBalance"));

                return new TransferExecuteResponseDto
                {
                    Success = true,
                    Message = "Space change executed successfully.",
                    OldBookingId = oldId,
                    NewBookingId = newId,
                    TransferId = transferId,
                    SettlementInvoiceId = invId,
                    SettlementInvoiceNumber = invNum,
                    CreditBalance = credit
                };
            }

            return new TransferExecuteResponseDto
            {
                Success = false,
                Message = "Transfer operation returned no result."
            };
        }

        public async Task<ActiveBookingSummaryDto?> GetBookingSummaryAsync(int bookingId)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_Booking_GetTransferBookingSummary", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookingId", bookingId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ActiveBookingSummaryDto
                {
                    BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                    BookingPublicId = reader.GetGuid(reader.GetOrdinal("BookingPublicId")),
                    CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                    CustomerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? null : reader.GetString(reader.GetOrdinal("CustomerEmail")),
                    CustomerCode = reader.IsDBNull(reader.GetOrdinal("CustomerCode")) ? null : reader.GetString(reader.GetOrdinal("CustomerCode")),
                    SpaceId = reader.GetInt32(reader.GetOrdinal("SpaceId")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? null : reader.GetString(reader.GetOrdinal("SpaceName")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("SpaceCode")) ? null : reader.GetString(reader.GetOrdinal("SpaceCode")),
                    LocationId = reader.GetInt32(reader.GetOrdinal("LocationId")),
                    LocationName = reader.IsDBNull(reader.GetOrdinal("LocationName")) ? null : reader.GetString(reader.GetOrdinal("LocationName")),
                    StartOn = reader.GetDateTime(reader.GetOrdinal("StartOn")),
                    EndOn = reader.GetDateTime(reader.GetOrdinal("EndOn")),
                    MonthlyRent = reader.GetDecimal(reader.GetOrdinal("MonthlyRent")),
                    DiscountPercentage = reader.GetDecimal(reader.GetOrdinal("DiscountPercentage")),
                    OfferingType = reader.IsDBNull(reader.GetOrdinal("OfferingType")) ? null : reader.GetString(reader.GetOrdinal("OfferingType")),
                    SecurityDeposit = reader.GetDecimal(reader.GetOrdinal("SecurityDepositPaid"))
                };
            }

            return null;
        }

        public async Task<TargetSpaceSummaryDto?> GetTargetSpaceSummaryAsync(int spaceId)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_Booking_GetTargetSpaceSummary", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TargetSpaceSummaryDto
                {
                    SpaceId = reader.GetInt32(reader.GetOrdinal("SpaceId")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? null : reader.GetString(reader.GetOrdinal("SpaceName")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("SpaceCode")) ? null : reader.GetString(reader.GetOrdinal("SpaceCode")),
                    LocationId = reader.GetInt32(reader.GetOrdinal("LocationId")),
                    LocationName = reader.IsDBNull(reader.GetOrdinal("LocationName")) ? null : reader.GetString(reader.GetOrdinal("LocationName")),
                    Capacity = Convert.ToInt32(reader["Capacity"]),
                    MonthlyRate = reader.GetDecimal(reader.GetOrdinal("MonthlyRate")),
                    MaxDiscountPercent = reader.GetDecimal(reader.GetOrdinal("MaxDiscountPercent"))
                };
            }

            return null;
        }

        public async Task<List<TargetSpaceSummaryDto>> GetAvailableTargetSpacesAsync(
            int locationId, DateTime effectiveDate, DateTime endDate, int excludeBookingId)
        {
            var list = new List<TargetSpaceSummaryDto>();

            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_Booking_GetAvailableTargetSpaces", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@LocationId", locationId);
            cmd.Parameters.AddWithValue("@EffectiveDate", effectiveDate.Date);
            cmd.Parameters.AddWithValue("@EndDate", endDate.Date);
            cmd.Parameters.AddWithValue("@ExcludeBookingId", excludeBookingId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TargetSpaceSummaryDto
                {
                    SpaceId = reader.GetInt32(reader.GetOrdinal("SpaceId")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? null : reader.GetString(reader.GetOrdinal("SpaceName")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("SpaceCode")) ? null : reader.GetString(reader.GetOrdinal("SpaceCode")),
                    LocationId = reader.GetInt32(reader.GetOrdinal("LocationId")),
                    LocationName = reader.IsDBNull(reader.GetOrdinal("LocationName")) ? null : reader.GetString(reader.GetOrdinal("LocationName")),
                    Capacity = Convert.ToInt32(reader["Capacity"]),
                    MonthlyRate = reader.GetDecimal(reader.GetOrdinal("MonthlyRate")),
                    MaxDiscountPercent = reader.GetDecimal(reader.GetOrdinal("MaxDiscountPercent"))
                });
            }
            await reader.CloseAsync();

            // Meeting / conference rooms are booked by the hour: never a target for a monthly booking.
            var meeting = await MeetingRoomIdsAsync(conn, list.Select(x => x.SpaceId));
            list.RemoveAll(x => meeting.Contains(x.SpaceId));
            return list;
        }

        private static async Task<HashSet<int>> MeetingRoomIdsAsync(SqlConnection conn, IEnumerable<int> spaceIds)
        {
            var ids = spaceIds.Distinct().ToList();
            var set = new HashSet<int>();
            if (ids.Count == 0) return set;
            await using var cmd = new SqlCommand(@"SELECT s.Id FROM dbo.WN_Spaces s WITH (NOLOCK)
                LEFT JOIN dbo.WN_SpaceTypes st WITH (NOLOCK) ON st.Id = s.SpaceTypeId
                WHERE s.Id IN (" + string.Join(",", ids) + @")
                  AND (ISNULL(st.Name, '') LIKE '%Meeting%' OR ISNULL(st.Name, '') LIKE '%Conference%'
                       OR ISNULL(s.Name, '') LIKE '%Meeting%' OR ISNULL(s.Name, '') LIKE '%Conference%')", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) set.Add(r.GetInt32(0));
            return set;
        }

        /// <summary>True when the space is a meeting / conference room (booked by the hour, not a monthly transfer target).</summary>
        public async Task<bool> IsMeetingRoomAsync(int spaceId)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            return (await MeetingRoomIdsAsync(conn, new[] { spaceId })).Contains(spaceId);
        }
    
        private static volatile bool _requestTableThere;
        private static DateTime _requestTableCheckedAt = DateTime.MinValue;

        /// <summary>dbo.WN_SpaceTransferRequests exists (checked once a minute until it is there).</summary>
        private async Task<bool> RequestTableExistsAsync(SqlConnection conn)
        {
            if (_requestTableThere) return true;
            if ((DateTime.UtcNow - _requestTableCheckedAt).TotalSeconds < 60) return false;
            _requestTableCheckedAt = DateTime.UtcNow;
            await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.WN_SpaceTransferRequests', N'U') IS NOT NULL THEN 1 ELSE 0 END", conn);
            _requestTableThere = Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
            return _requestTableThere;
        }

        public async Task<TransferHistoryPageDto> ListTransferHistoryAsync(string? search, DateTime? from, DateTime? to, int page, int pageSize, IReadOnlyCollection<int>? locationIds)
        {
            var res = new TransferHistoryPageDto { Page = page, PageSize = pageSize };
            if (locationIds != null && locationIds.Count == 0) return res;
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            bool hasReq = await RequestTableExistsAsync(conn);

            var where = new List<string>();
            if (locationIds != null) where.Add("t.LocationId IN (" + string.Join(",", locationIds.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ")");
            if (from != null) where.Add("t.EffectiveDate >= @from");
            if (to != null) where.Add("t.EffectiveDate <= @to");
            string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            if (term != null)
                where.Add(@"(cust.CustomerName LIKE @like OR ob.CustomerCode LIKE @like OR os.Name LIKE @like OR ns.Name LIKE @like
                             OR CAST(t.OldBookingId AS varchar(20)) = @term OR CAST(t.NewBookingId AS varchar(20)) = @term)");
            string whereSql = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";

            string reqCols = hasReq
                ? "r.Id AS RequestId, ru.Name AS RequestedByName, du.Name AS ApprovedByName, r.DecidedAt, r.OldPerSeatPrice, r.PerSeatPrice AS NewPerSeatPrice, r.OldDeposit, r.NewDeposit, r.PrepaidMode,"
                : "CAST(NULL AS int) AS RequestId, CAST(NULL AS nvarchar(200)) AS RequestedByName, CAST(NULL AS nvarchar(200)) AS ApprovedByName, CAST(NULL AS datetime2) AS DecidedAt, CAST(NULL AS decimal(18,2)) AS OldPerSeatPrice, CAST(NULL AS decimal(18,2)) AS NewPerSeatPrice, CAST(NULL AS decimal(18,2)) AS OldDeposit, CAST(NULL AS decimal(18,2)) AS NewDeposit, CAST(NULL AS varchar(20)) AS PrepaidMode,";
            string reqJoin = hasReq
                ? @"LEFT JOIN dbo.WN_SpaceTransferRequests r WITH (NOLOCK) ON r.NewBookingId = t.NewBookingId
                    LEFT JOIN dbo.WN_Users ru WITH (NOLOCK) ON ru.Id = r.RequestedById
                    LEFT JOIN dbo.WN_Users du WITH (NOLOCK) ON du.Id = r.DecidedById"
                : "";

            string fromSql = @"
                FROM dbo.WN_BookingTransfers t WITH (NOLOCK)
                LEFT JOIN dbo.WN_Bookings ob WITH (NOLOCK) ON ob.Id = t.OldBookingId
                LEFT JOIN dbo.WN_Bookings nb WITH (NOLOCK) ON nb.Id = t.NewBookingId
                LEFT JOIN dbo.WN_Spaces os WITH (NOLOCK) ON os.Id = t.OldSpaceId
                LEFT JOIN dbo.WN_Spaces ns WITH (NOLOCK) ON ns.Id = t.NewSpaceId
                LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = t.LocationId
                LEFT JOIN dbo.WN_Invoices inv WITH (NOLOCK) ON inv.Id = t.SettlementInvoiceId
                LEFT JOIN dbo.WN_Users iu WITH (NOLOCK) ON iu.Id = t.InitiatedById
                " + reqJoin + @"
                OUTER APPLY (
                    SELECT TOP 1 ISNULL(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(ISNULL(cu.FirstName, '') + ' ' + ISNULL(cu.LastName, ''))), '')) AS CustomerName
                    FROM dbo.WN_Customers cu WITH (NOLOCK)
                    WHERE (ob.CustomerCode IS NOT NULL AND ob.CustomerCode <> '' AND cu.Code = ob.CustomerCode) OR cu.UserId = ob.UserId
                    ORDER BY CASE WHEN ob.CustomerCode IS NOT NULL AND cu.Code = ob.CustomerCode THEN 0 ELSE 1 END, cu.Id
                ) cust" + whereSql;

            void Bind(SqlCommand c)
            {
                if (from != null) c.Parameters.AddWithValue("@from", from.Value.Date);
                if (to != null) c.Parameters.AddWithValue("@to", to.Value.Date);
                if (term != null)
                {
                    c.Parameters.AddWithValue("@like", "%" + term.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%");
                    c.Parameters.AddWithValue("@term", term.TrimStart('#'));
                }
            }

            await using (var cnt = new SqlCommand("SELECT COUNT(*) " + fromSql, conn))
            {
                Bind(cnt);
                res.Total = Convert.ToInt32(await cnt.ExecuteScalarAsync());
            }

            string sql = @"SELECT t.Id, t.EffectiveDate, t.CreatedOn, t.OldBookingId, t.NewBookingId, cust.CustomerName, ob.CustomerCode,
                       t.LocationId, l.Name AS LocationName, os.Name AS OldSpaceName, ns.Name AS NewSpaceName,
                       t.OldMonthlyRate, t.NewMonthlyRate, t.Adjustment, t.SettlementOutcome, t.SettlementInvoiceId,
                       inv.InvoiceNumber AS SettlementInvoiceNumber, inv.GrandTotal AS SettlementInvoiceAmount,
                       t.TransferFee, t.Notes, iu.Name AS InitiatedByName, t.InitiatedByEmail, nb.TransferCreditBalance AS CreditBalance,
                       " + reqCols + @" 0 AS _x " + fromSql + @"
                ORDER BY t.EffectiveDate DESC, t.Id DESC
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
            await using var cmd = new SqlCommand(sql, conn);
            Bind(cmd);
            cmd.Parameters.AddWithValue("@skip", (page - 1) * pageSize);
            cmd.Parameters.AddWithValue("@take", pageSize);
            await using var r = await cmd.ExecuteReaderAsync();
            string? S(string c) => r[c] is DBNull ? null : Convert.ToString(r[c]);
            decimal? D(string c) => r[c] is DBNull ? null : Convert.ToDecimal(r[c]);
            int? I(string c) => r[c] is DBNull ? null : Convert.ToInt32(r[c]);
            DateTime? T(string c) => r[c] is DBNull ? null : Convert.ToDateTime(r[c]);
            while (await r.ReadAsync())
            {
                res.Items.Add(new TransferHistoryItemDto
                {
                    Id = Convert.ToInt32(r["Id"]),
                    EffectiveDate = Convert.ToDateTime(r["EffectiveDate"]),
                    CreatedOn = Convert.ToDateTime(r["CreatedOn"]),
                    OldBookingId = Convert.ToInt32(r["OldBookingId"]),
                    NewBookingId = Convert.ToInt32(r["NewBookingId"]),
                    CustomerName = S("CustomerName"),
                    CustomerCode = S("CustomerCode"),
                    LocationId = Convert.ToInt32(r["LocationId"]),
                    LocationName = S("LocationName"),
                    OldSpaceName = S("OldSpaceName"),
                    NewSpaceName = S("NewSpaceName"),
                    OldMonthlyRate = Dec0(r, "OldMonthlyRate"),
                    NewMonthlyRate = Dec0(r, "NewMonthlyRate"),
                    Adjustment = Dec0(r, "Adjustment"),
                    SettlementOutcome = S("SettlementOutcome"),
                    SettlementInvoiceId = I("SettlementInvoiceId"),
                    SettlementInvoiceNumber = S("SettlementInvoiceNumber"),
                    SettlementInvoiceAmount = D("SettlementInvoiceAmount"),
                    TransferFee = Dec0(r, "TransferFee"),
                    Notes = S("Notes"),
                    InitiatedByName = S("InitiatedByName"),
                    InitiatedByEmail = S("InitiatedByEmail"),
                    CreditBalance = D("CreditBalance"),
                    RequestId = I("RequestId"),
                    RequestedByName = S("RequestedByName"),
                    ApprovedByName = S("ApprovedByName"),
                    DecidedAt = T("DecidedAt"),
                    OldPerSeatPrice = D("OldPerSeatPrice"),
                    NewPerSeatPrice = D("NewPerSeatPrice"),
                    OldDeposit = D("OldDeposit"),
                    NewDeposit = D("NewDeposit"),
                    PrepaidMode = S("PrepaidMode")
                });
            }
            return res;
        }

        public async Task<List<ActiveBookingSummaryDto>> GetActiveBookingsAsync(int? locationId, string? search)
        {
            var list = new List<ActiveBookingSummaryDto>();
            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_Booking_GetActiveForTransfer", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ActiveBookingSummaryDto
                {
                    BookingId = reader.GetInt32(reader.GetOrdinal("BookingId")),
                    BookingPublicId = reader.GetGuid(reader.GetOrdinal("BookingPublicId")),
                    CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                    CustomerEmail = reader.IsDBNull(reader.GetOrdinal("CustomerEmail")) ? null : reader.GetString(reader.GetOrdinal("CustomerEmail")),
                    CustomerCode = reader.IsDBNull(reader.GetOrdinal("CustomerCode")) ? null : reader.GetString(reader.GetOrdinal("CustomerCode")),
                    SpaceId = reader.GetInt32(reader.GetOrdinal("SpaceId")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? null : reader.GetString(reader.GetOrdinal("SpaceName")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("SpaceCode")) ? null : reader.GetString(reader.GetOrdinal("SpaceCode")),
                    LocationId = reader.GetInt32(reader.GetOrdinal("LocationId")),
                    LocationName = reader.IsDBNull(reader.GetOrdinal("LocationName")) ? null : reader.GetString(reader.GetOrdinal("LocationName")),
                    StartOn = reader.GetDateTime(reader.GetOrdinal("StartOn")),
                    EndOn = reader.GetDateTime(reader.GetOrdinal("EndOn")),
                    MonthlyRent = reader.GetDecimal(reader.GetOrdinal("MonthlyRent")),
                    DiscountPercentage = reader.GetDecimal(reader.GetOrdinal("DiscountPercentage")),
                    OfferingType = reader.IsDBNull(reader.GetOrdinal("OfferingType")) ? null : reader.GetString(reader.GetOrdinal("OfferingType")),
                    SecurityDeposit = reader.GetDecimal(reader.GetOrdinal("SecurityDepositPaid"))
                });
            }

            await reader.CloseAsync();
            var offers = await OfferSnapshotStore.ReadAsync(conn, WorkNest.Application.DTOs.Offers.OfferEntity.Booking, list.Select(b => b.BookingId));
            foreach (var b in list)
                if (offers.TryGetValue(b.BookingId, out var o)) { b.OfferId = o.OfferId; b.OfferName = o.OfferName; }

            return list;
        }
    
        // =========================================================================================
        // Phase 2: Branch Transfers & Pending Payments
        // =========================================================================================

        public async Task<int> CreateBranchTransferRequestAsync(BranchTransferCreateRequestDto dto, int actorId, string actorEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_Create", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookingId", dto.BookingId);
            cmd.Parameters.AddWithValue("@ToLocationId", dto.ToLocationId);
            cmd.Parameters.AddWithValue("@RequestedCapacity", dto.RequestedCapacity);
            cmd.Parameters.AddWithValue("@EffectiveDate", (object?)dto.EffectiveDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)dto.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TransferFee", dto.TransferFee);
            cmd.Parameters.AddWithValue("@CreatedById", actorId);
            cmd.Parameters.AddWithValue("@CreatedByEmail", actorEmail);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return reader.GetInt32(reader.GetOrdinal("RequestId"));
            }
            throw new InvalidOperationException("Failed to create transfer request.");
        }

        public async Task<bool> CancelBranchTransferRequestAsync(int requestId, string? reason, int actorId, string actorEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_Cancel", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@RequestId", requestId);
            cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ActorId", actorId);
            cmd.Parameters.AddWithValue("@ActorEmail", actorEmail);

            await cmd.ExecuteNonQueryAsync();
            return true;
        }

        public async Task<bool> DeclineBranchTransferRequestAsync(int requestId, string? reason, int actorId, string actorEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_Decline", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@RequestId", requestId);
            cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ActorId", actorId);
            cmd.Parameters.AddWithValue("@ActorEmail", actorEmail);

            await cmd.ExecuteNonQueryAsync();
            return true;
        }

        public async Task<int> ExpireOldTransferRequestsAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequests_ExpireOld", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        public async Task<List<TransferRequestSendingSummaryDto>> ListSendingRequestsAsync(int? companyId, int? locationId, string? userRole)
        {
            var list = new List<TransferRequestSendingSummaryDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_ListSending", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UserRole", (object?)userRole ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TransferRequestSendingSummaryDto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    OldBookingId = reader.GetInt32(reader.GetOrdinal("OldBookingId")),
                    FromLocationId = reader.GetInt32(reader.GetOrdinal("FromLocationId")),
                    FromLocationName = reader.GetString(reader.GetOrdinal("FromLocationName")),
                    FromCompanyId = reader.GetInt32(reader.GetOrdinal("FromCompanyId")),
                    ToLocationId = reader.GetInt32(reader.GetOrdinal("ToLocationId")),
                    ToLocationName = reader.GetString(reader.GetOrdinal("ToLocationName")),
                    ToCompanyId = reader.GetInt32(reader.GetOrdinal("ToCompanyId")),
                    RequestedCapacity = reader.GetInt32(reader.GetOrdinal("RequestedCapacity")),
                    EffectiveDate = reader.GetDateTime(reader.GetOrdinal("EffectiveDate")),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                    TransferFee = reader.GetDecimal(reader.GetOrdinal("TransferFee")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    DeclineCancelReason = reader.IsDBNull(reader.GetOrdinal("DeclineCancelReason")) ? null : reader.GetString(reader.GetOrdinal("DeclineCancelReason")),
                    CreatedById = reader.GetInt32(reader.GetOrdinal("CreatedById")),
                    CreatedByEmail = reader.GetString(reader.GetOrdinal("CreatedByEmail")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    ExpiresAt = reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
                    TargetSpaceId = reader.IsDBNull(reader.GetOrdinal("TargetSpaceId")) ? null : reader.GetInt32(reader.GetOrdinal("TargetSpaceId")),
                    TargetSpaceName = reader.IsDBNull(reader.GetOrdinal("TargetSpaceName")) ? null : reader.GetString(reader.GetOrdinal("TargetSpaceName")),
                    AcceptedByEmail = reader.IsDBNull(reader.GetOrdinal("AcceptedByEmail")) ? null : reader.GetString(reader.GetOrdinal("AcceptedByEmail")),
                    AcceptedAt = reader.IsDBNull(reader.GetOrdinal("AcceptedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("AcceptedAt")),
                    MatchedCustomerId = reader.IsDBNull(reader.GetOrdinal("MatchedCustomerId")) ? null : reader.GetInt32(reader.GetOrdinal("MatchedCustomerId")),
                    NewBookingId = reader.IsDBNull(reader.GetOrdinal("NewBookingId")) ? null : reader.GetInt32(reader.GetOrdinal("NewBookingId")),
                    FinalizedAt = reader.IsDBNull(reader.GetOrdinal("FinalizedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("FinalizedAt")),
                    CustomerCode = reader.IsDBNull(reader.GetOrdinal("CustomerCode")) ? null : reader.GetString(reader.GetOrdinal("CustomerCode")),
                    CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? null : reader.GetString(reader.GetOrdinal("CustomerName")),
                    OldSpaceName = reader.IsDBNull(reader.GetOrdinal("OldSpaceName")) ? null : reader.GetString(reader.GetOrdinal("OldSpaceName"))
                });
            }

            return list;
        }

        public async Task<List<TargetSpaceSummaryDto>> GetIncomingAvailableSpacesAsync(int requestId)
        {
            var list = new List<TargetSpaceSummaryDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_GetIncomingAvailableSpaces", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@RequestId", requestId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TargetSpaceSummaryDto
                {
                    SpaceId = reader.GetInt32(reader.GetOrdinal("SpaceId")),
                    SpaceCode = reader.IsDBNull(reader.GetOrdinal("SpaceCode")) ? null : reader.GetString(reader.GetOrdinal("SpaceCode")),
                    SpaceName = reader.IsDBNull(reader.GetOrdinal("SpaceName")) ? null : reader.GetString(reader.GetOrdinal("SpaceName")),
                    LocationId = reader.GetInt32(reader.GetOrdinal("LocationId")),
                    LocationName = reader.IsDBNull(reader.GetOrdinal("LocationName")) ? null : reader.GetString(reader.GetOrdinal("LocationName")),
                    Capacity = Convert.ToInt32(reader["Capacity"]),
                    MonthlyRate = reader.GetDecimal(reader.GetOrdinal("MonthlyRate")),
                    MaxDiscountPercent = reader.GetDecimal(reader.GetOrdinal("MaxDiscountPercent")),
                    OfferingType = reader.IsDBNull(reader.GetOrdinal("OfferingType")) ? null : reader.GetString(reader.GetOrdinal("OfferingType")),
                    OfferingTypeDiscountCap = reader.GetDecimal(reader.GetOrdinal("OfferingTypeDiscountCap"))
                });
            }

            return list;
        }

        public async Task<List<TransferRequestIncomingSummaryDto>> ListIncomingRequestsAsync(int? companyId, int? locationId, string? userRole)
        {
            var list = new List<TransferRequestIncomingSummaryDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_ListIncoming", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UserRole", (object?)userRole ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TransferRequestIncomingSummaryDto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    FromLocationId = reader.GetInt32(reader.GetOrdinal("FromLocationId")),
                    FromLocationName = reader.GetString(reader.GetOrdinal("FromLocationName")),
                    ToLocationId = reader.GetInt32(reader.GetOrdinal("ToLocationId")),
                    ToLocationName = reader.GetString(reader.GetOrdinal("ToLocationName")),
                    RequestedCapacity = reader.GetInt32(reader.GetOrdinal("RequestedCapacity")),
                    EffectiveDate = reader.GetDateTime(reader.GetOrdinal("EffectiveDate")),
                    Notes = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    ExpiresAt = reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
                    TargetSpaceId = reader.IsDBNull(reader.GetOrdinal("TargetSpaceId")) ? null : reader.GetInt32(reader.GetOrdinal("TargetSpaceId")),
                    TargetSpaceName = reader.IsDBNull(reader.GetOrdinal("TargetSpaceName")) ? null : reader.GetString(reader.GetOrdinal("TargetSpaceName")),
                    MatchResolutionNote = reader.IsDBNull(reader.GetOrdinal("MatchResolutionNote")) ? null : reader.GetString(reader.GetOrdinal("MatchResolutionNote")),
                    NewBookingId = reader.IsDBNull(reader.GetOrdinal("NewBookingId")) ? null : reader.GetInt32(reader.GetOrdinal("NewBookingId"))
                });
            }

            return list;
        }

        public async Task<MatchResolutionDetailsDto?> GetMatchResolutionAsync(int requestId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_GetMatchResolution", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@RequestId", requestId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var dto = new MatchResolutionDetailsDto
            {
                RequestId = reader.GetInt32(reader.GetOrdinal("RequestId")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                EffectiveDate = reader.GetDateTime(reader.GetOrdinal("EffectiveDate")),
                RequestedCapacity = reader.GetInt32(reader.GetOrdinal("RequestedCapacity")),
                TargetSpaceId = reader.IsDBNull(reader.GetOrdinal("TargetSpaceId")) ? null : reader.GetInt32(reader.GetOrdinal("TargetSpaceId")),
                TargetSpaceName = reader.IsDBNull(reader.GetOrdinal("TargetSpaceName")) ? null : reader.GetString(reader.GetOrdinal("TargetSpaceName")),
                FromLocationName = reader.GetString(reader.GetOrdinal("FromLocationName")),
                ToLocationName = reader.GetString(reader.GetOrdinal("ToLocationName")),
                OldCustomerCode = reader.IsDBNull(reader.GetOrdinal("OldCustomerCode")) ? null : reader.GetString(reader.GetOrdinal("OldCustomerCode")),
                OldCustomerName = reader.IsDBNull(reader.GetOrdinal("OldCustomerName")) ? null : reader.GetString(reader.GetOrdinal("OldCustomerName")),
                OldCNIC = reader.IsDBNull(reader.GetOrdinal("OldCNIC")) ? null : reader.GetString(reader.GetOrdinal("OldCNIC")),
                OldPhone = reader.IsDBNull(reader.GetOrdinal("OldPhone")) ? null : reader.GetString(reader.GetOrdinal("OldPhone")),
                OldEmail = reader.IsDBNull(reader.GetOrdinal("OldEmail")) ? null : reader.GetString(reader.GetOrdinal("OldEmail")),
                OldCustomerCompany = reader.IsDBNull(reader.GetOrdinal("OldCustomerCompany")) ? null : reader.GetString(reader.GetOrdinal("OldCustomerCompany"))
            };

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    dto.Candidates.Add(new MatchCandidateCustomerDto
                    {
                        CustomerVendorId = reader.GetInt32(reader.GetOrdinal("CustomerVendorId")),
                        Code = reader.IsDBNull(reader.GetOrdinal("Code")) ? null : reader.GetString(reader.GetOrdinal("Code")),
                        Name = reader.IsDBNull(reader.GetOrdinal("Name")) ? null : reader.GetString(reader.GetOrdinal("Name")),
                        CNIC = reader.IsDBNull(reader.GetOrdinal("CNIC")) ? null : reader.GetString(reader.GetOrdinal("CNIC")),
                        Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
                        Email = reader.IsDBNull(reader.GetOrdinal("Email")) ? null : reader.GetString(reader.GetOrdinal("Email")),
                        CompanyId = reader.GetInt32(reader.GetOrdinal("CompanyId")),
                        MatchReason = reader.GetString(reader.GetOrdinal("MatchReason"))
                    });
                }
            }

            return dto;
        }

        public async Task<(string Status, string Message, int? NewBookingId)> AcceptWithSpaceAsync(int requestId, int spaceId, int actorId, string actorEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_AcceptWithSpace", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@RequestId", requestId);
            cmd.Parameters.AddWithValue("@SpaceId", spaceId);
            cmd.Parameters.AddWithValue("@ActorId", actorId);
            cmd.Parameters.AddWithValue("@ActorEmail", actorEmail);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var status = reader.GetString(reader.GetOrdinal("Status"));
                var message = reader.HasColumn("Message") && !reader.IsDBNull(reader.GetOrdinal("Message"))
                    ? reader.GetString(reader.GetOrdinal("Message"))
                    : (status == "completed" ? "Transfer finalized successfully." : "Awaiting match resolution.");
                var newBookingId = reader.HasColumn("NewBookingId") && !reader.IsDBNull(reader.GetOrdinal("NewBookingId"))
                    ? reader.GetInt32(reader.GetOrdinal("NewBookingId"))
                    : (int?)null;

                return (status, message, newBookingId);
            }

            throw new InvalidOperationException("Failed to accept transfer request.");
        }

        public async Task<(string Status, string Message, int? NewBookingId)> ResolveMatchAsync(int requestId, string resolutionType, int? selectedCustomerId, string? note, int actorId, string actorEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferRequest_ResolveMatch", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@RequestId", requestId);
            cmd.Parameters.AddWithValue("@ResolutionType", resolutionType);
            cmd.Parameters.AddWithValue("@SelectedCustomerId", (object?)selectedCustomerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ActorId", actorId);
            cmd.Parameters.AddWithValue("@ActorEmail", actorEmail);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var status = reader.GetString(reader.GetOrdinal("Status"));
                var message = reader.HasColumn("Message") && !reader.IsDBNull(reader.GetOrdinal("Message"))
                    ? reader.GetString(reader.GetOrdinal("Message"))
                    : "Transfer match resolved and finalized successfully.";
                var newBookingId = reader.HasColumn("NewBookingId") && !reader.IsDBNull(reader.GetOrdinal("NewBookingId"))
                    ? reader.GetInt32(reader.GetOrdinal("NewBookingId"))
                    : (int?)null;

                return (status, message, newBookingId);
            }

            throw new InvalidOperationException("Failed to resolve transfer match.");
        }

        public async Task<List<PendingPaymentSummaryDto>> ListPendingPaymentsAsync(int? companyId, int? locationId, string? userRole)
        {
            var list = new List<PendingPaymentSummaryDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferPendingPayments_List", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@CompanyId", (object?)companyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UserRole", (object?)userRole ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PendingPaymentSummaryDto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    TransferRequestId = reader.GetInt32(reader.GetOrdinal("TransferRequestId")),
                    FromCompanyId = reader.GetInt32(reader.GetOrdinal("FromCompanyId")),
                    FromLocationId = reader.GetInt32(reader.GetOrdinal("FromLocationId")),
                    FromBranchName = reader.GetString(reader.GetOrdinal("FromBranchName")),
                    ToCompanyId = reader.GetInt32(reader.GetOrdinal("ToCompanyId")),
                    ToLocationId = reader.GetInt32(reader.GetOrdinal("ToLocationId")),
                    ToBranchName = reader.GetString(reader.GetOrdinal("ToBranchName")),
                    OldBookingId = reader.GetInt32(reader.GetOrdinal("OldBookingId")),
                    NewBookingId = reader.GetInt32(reader.GetOrdinal("NewBookingId")),
                    RentAmount = reader.GetDecimal(reader.GetOrdinal("RentAmount")),
                    DepositAmount = reader.GetDecimal(reader.GetOrdinal("DepositAmount")),
                    TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                    ReceiptId = reader.IsDBNull(reader.GetOrdinal("ReceiptId")) ? null : reader.GetInt32(reader.GetOrdinal("ReceiptId")),
                    IsConfirmed = reader.GetBoolean(reader.GetOrdinal("IsConfirmed")),
                    ConfirmedAt = reader.IsDBNull(reader.GetOrdinal("ConfirmedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ConfirmedAt")),
                    ConfirmedByEmail = reader.IsDBNull(reader.GetOrdinal("ConfirmedByEmail")) ? null : reader.GetString(reader.GetOrdinal("ConfirmedByEmail")),
                    BankAccount = reader.IsDBNull(reader.GetOrdinal("BankAccount")) ? null : reader.GetString(reader.GetOrdinal("BankAccount")),
                    PaymentReference = reader.IsDBNull(reader.GetOrdinal("PaymentReference")) ? null : reader.GetString(reader.GetOrdinal("PaymentReference")),
                    PaymentDate = reader.IsDBNull(reader.GetOrdinal("PaymentDate")) ? null : reader.GetDateTime(reader.GetOrdinal("PaymentDate")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    AgeInDays = reader.GetInt32(reader.GetOrdinal("AgeInDays"))
                });
            }

            return list;
        }

        public async Task<bool> ConfirmPendingPaymentAsync(int paymentId, string bankAccount, string paymentReference, DateTime? paymentDate, int actorId, string actorEmail)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_TransferPendingPayments_Confirm", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@PendingPaymentId", paymentId);
            cmd.Parameters.AddWithValue("@BankAccount", bankAccount);
            cmd.Parameters.AddWithValue("@PaymentReference", paymentReference);
            cmd.Parameters.AddWithValue("@PaymentDate", (object?)paymentDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ActorId", actorId);
            cmd.Parameters.AddWithValue("@ActorEmail", actorEmail);

            await cmd.ExecuteNonQueryAsync();
            return true;
        }
    
        // ---- Space transfer requests (dbo.WN_SpaceTransferRequests) ---------------------------------------------------------

        private const string RequestSelect = @"
            SELECT a.Id, a.BookingId, a.NewSpaceId, a.EffectiveDate, a.LocationId AS ReqLocationId, a.PlanJson, a.PerSeatPrice, a.OldPerSeatPrice,
                   a.NewDeposit, a.OldDeposit, a.PrepaidMode, a.DepositDecreaseMode, a.TransferFee, a.Note, a.Status,
                   a.RequestedById, ru.Name AS RequestedByName, a.RequestedAt, du.Name AS DecidedByName, a.DecidedAt, a.DecisionNote, a.NewBookingId,
                   os.Name AS OldSpaceName, os.LocationId AS SpaceLocationId, ns.Name AS NewSpaceName, l.Name AS LocationName, cust.CustomerName
            FROM dbo.WN_SpaceTransferRequests a WITH (NOLOCK)
            JOIN dbo.WN_Bookings b WITH (NOLOCK) ON b.Id = a.BookingId
            LEFT JOIN dbo.WN_Spaces os WITH (NOLOCK) ON os.Id = b.SpaceId
            LEFT JOIN dbo.WN_Spaces ns WITH (NOLOCK) ON ns.Id = a.NewSpaceId
            LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = ISNULL(a.LocationId, os.LocationId)
            LEFT JOIN dbo.WN_Users ru WITH (NOLOCK) ON ru.Id = a.RequestedById
            LEFT JOIN dbo.WN_Users du WITH (NOLOCK) ON du.Id = a.DecidedById
            OUTER APPLY (
                SELECT TOP 1 ISNULL(NULLIF(cu.Company, ''), NULLIF(LTRIM(RTRIM(ISNULL(cu.FirstName, '') + ' ' + ISNULL(cu.LastName, ''))), '')) AS CustomerName
                FROM dbo.WN_Customers cu WITH (NOLOCK)
                WHERE (b.CustomerCode IS NOT NULL AND b.CustomerCode <> '' AND cu.Code = b.CustomerCode) OR cu.UserId = b.UserId
                ORDER BY CASE WHEN b.CustomerCode IS NOT NULL AND cu.Code = b.CustomerCode THEN 0 ELSE 1 END, cu.Id
            ) cust";

        private static decimal Dec0(SqlDataReader r, string c) => r[c] is DBNull ? 0m : Convert.ToDecimal(r[c]);

        private static TransferRequestRecord MapRequest(SqlDataReader r) => new()
        {
            PlanJson = Convert.ToString(r["PlanJson"]) ?? "",
            LocationId = r["ReqLocationId"] is not DBNull ? Convert.ToInt32(r["ReqLocationId"]) : r["SpaceLocationId"] is DBNull ? null : Convert.ToInt32(r["SpaceLocationId"]),
            RequestedById = r["RequestedById"] is DBNull ? null : Convert.ToInt32(r["RequestedById"]),
            TransferFee = Dec0(r, "TransferFee"),
            Dto = new TransferRequestDto
            {
                Id = Convert.ToInt32(r["Id"]), BookingId = Convert.ToInt32(r["BookingId"]), NewSpaceId = Convert.ToInt32(r["NewSpaceId"]),
                CustomerName = r["CustomerName"] as string, OldSpaceName = r["OldSpaceName"] as string, NewSpaceName = r["NewSpaceName"] as string,
                LocationName = r["LocationName"] as string, EffectiveDate = Convert.ToDateTime(r["EffectiveDate"]),
                Status = Convert.ToString(r["Status"]) ?? "Pending", RequestedByName = r["RequestedByName"] as string,
                RequestedAt = DateTime.SpecifyKind(Convert.ToDateTime(r["RequestedAt"]), DateTimeKind.Utc),
                DecidedByName = r["DecidedByName"] as string,
                DecidedAt = r["DecidedAt"] is DBNull ? null : DateTime.SpecifyKind(Convert.ToDateTime(r["DecidedAt"]), DateTimeKind.Utc),
                DecisionNote = r["DecisionNote"] as string, Note = r["Note"] as string,
                NewBookingId = r["NewBookingId"] is DBNull ? null : Convert.ToInt32(r["NewBookingId"]),
                OldPerSeatPrice = Dec0(r, "OldPerSeatPrice"), PerSeatPrice = Dec0(r, "PerSeatPrice"),
                OldDeposit = Dec0(r, "OldDeposit"), NewDeposit = Dec0(r, "NewDeposit"),
                PrepaidMode = r["PrepaidMode"] as string, DepositDecreaseMode = r["DepositDecreaseMode"] as string
            }
        };

        /// <summary>Adds a Pending request; returns null when the booking already has one waiting.</summary>
        public async Task<int?> InsertTransferRequestAsync(int bookingId, int newSpaceId, DateTime effectiveDate, int? locationId, string planJson,
            TransferPlanDto plan, string? note, int? requestedById)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                IF EXISTS (SELECT 1 FROM dbo.WN_SpaceTransferRequests WITH (UPDLOCK, HOLDLOCK) WHERE BookingId = @b AND Status = 'Pending')
                    SELECT CAST(NULL AS INT);
                ELSE
                BEGIN
                    INSERT INTO dbo.WN_SpaceTransferRequests (BookingId, NewSpaceId, EffectiveDate, LocationId, PlanJson, PerSeatPrice, OldPerSeatPrice,
                        NewDeposit, OldDeposit, PrepaidMode, DepositDecreaseMode, TransferFee, Note, Status, RequestedById)
                    VALUES (@b, @s, @d, @loc, @json, @p, @op, @nd, @od, @pm, @dm, @fee, @note, 'Pending', @by);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                END", conn);
            cmd.Parameters.AddWithValue("@b", bookingId);
            cmd.Parameters.AddWithValue("@s", newSpaceId);
            cmd.Parameters.AddWithValue("@d", effectiveDate.Date);
            cmd.Parameters.AddWithValue("@loc", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@json", planJson);
            cmd.Parameters.AddWithValue("@p", plan.PerSeatPrice);
            cmd.Parameters.AddWithValue("@op", plan.OldPerSeatPrice);
            cmd.Parameters.AddWithValue("@nd", plan.NewDeposit);
            cmd.Parameters.AddWithValue("@od", plan.OldDeposit);
            cmd.Parameters.AddWithValue("@pm", plan.PrepaidMode);
            cmd.Parameters.AddWithValue("@dm", (object?)plan.DepositDecreaseMode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fee", plan.TransferFee);
            cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@by", (object?)requestedById ?? 0);
            var v = await cmd.ExecuteScalarAsync();
            return v is int i ? i : null;
        }

        public async Task<TransferRequestRecord?> GetTransferRequestAsync(int id)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(RequestSelect + " WHERE a.Id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? MapRequest(r) : null;
        }

        /// <summary>locationIds null = all, empty = nothing; requestedById set = only that user's requests.</summary>
        public async Task<List<TransferRequestDto>> ListTransferRequestsAsync(bool pendingOnly, IReadOnlyCollection<int>? locationIds, int? requestedById)
        {
            var list = new List<TransferRequestDto>();
            if (locationIds != null && locationIds.Count == 0) return list;
            var where = new List<string>();
            if (pendingOnly) where.Add("a.Status = 'Pending'");
            if (requestedById != null) where.Add("a.RequestedById = @req");
            if (locationIds != null) where.Add("ISNULL(a.LocationId, os.LocationId) IN (" + string.Join(",", locationIds.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ")");
            string sql = RequestSelect + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                + " ORDER BY CASE WHEN a.Status = 'Pending' THEN 0 ELSE 1 END, a.RequestedAt DESC";
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            if (requestedById != null) cmd.Parameters.AddWithValue("@req", requestedById.Value);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(MapRequest(r).Dto);
            return list;
        }

        public async Task<bool> SendBackTransferRequestAsync(int id, int? decidedById, string note)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_SpaceTransferRequests
                SET Status = 'SentBack', DecidedById = @by, DecidedAt = SYSUTCDATETIME(), DecisionNote = @note
                WHERE Id = @id AND Status = 'Pending'", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@by", (object?)decidedById ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@note", note);
            return await cmd.ExecuteNonQueryAsync() == 1;
        }

        public async Task FinalizeApprovalAsync(int id, int? decidedById, string? note, string planJson, TransferPlanDto plan)
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"UPDATE dbo.WN_SpaceTransferRequests
                SET PlanJson = @json, PerSeatPrice = @p, NewDeposit = @nd, PrepaidMode = @pm, DepositDecreaseMode = @dm,
                    DecidedById = ISNULL(DecidedById, @by), DecidedAt = ISNULL(DecidedAt, SYSUTCDATETIME()), DecisionNote = ISNULL(@note, DecisionNote)
                WHERE Id = @id AND Status = 'Approved'", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@json", planJson);
            cmd.Parameters.AddWithValue("@p", plan.PerSeatPrice);
            cmd.Parameters.AddWithValue("@nd", plan.NewDeposit);
            cmd.Parameters.AddWithValue("@pm", plan.PrepaidMode);
            cmd.Parameters.AddWithValue("@dm", (object?)plan.DepositDecreaseMode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@by", (object?)decidedById ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

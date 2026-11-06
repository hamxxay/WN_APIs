using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.NoticePeriod;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    public class NoticePeriodRepository : INoticePeriodRepository
    {
        private readonly string _connectionString;

        public NoticePeriodRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        }

        private static T? Val<T>(object? val) => val == null || val is DBNull ? default : (T)Convert.ChangeType(val, typeof(T));

        private static NoticePeriodItemDto MapItem(SqlDataReader r)
        {
            return new NoticePeriodItemDto
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                PublicId = r.GetGuid(r.GetOrdinal("PublicId")),
                BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                CustomerId = r.GetInt32(r.GetOrdinal("CustomerId")),
                UserId = r.GetInt32(r.GetOrdinal("UserId")),
                CustomerCode = r.IsDBNull(r.GetOrdinal("CustomerCode")) ? null : r.GetString(r.GetOrdinal("CustomerCode")),
                CustomerName = r.HasColumn("CustomerName") && !r.IsDBNull(r.GetOrdinal("CustomerName")) ? r.GetString(r.GetOrdinal("CustomerName")) : null,
                CustomerEmail = r.HasColumn("CustomerEmail") && !r.IsDBNull(r.GetOrdinal("CustomerEmail")) ? r.GetString(r.GetOrdinal("CustomerEmail")) : null,
                CustomerPhone = r.HasColumn("CustomerPhone") && !r.IsDBNull(r.GetOrdinal("CustomerPhone")) ? r.GetString(r.GetOrdinal("CustomerPhone")) : null,
                Source = r.GetString(r.GetOrdinal("Source")),
                RequestedOn = r.GetDateTime(r.GetOrdinal("RequestedOn")),
                NoticeDate = r.IsDBNull(r.GetOrdinal("NoticeDate")) ? null : r.GetDateTime(r.GetOrdinal("NoticeDate")),
                NoticeDays = r.GetInt32(r.GetOrdinal("NoticeDays")),
                ExitDate = r.IsDBNull(r.GetOrdinal("ExitDate")) ? null : r.GetDateTime(r.GetOrdinal("ExitDate")),
                Status = r.GetString(r.GetOrdinal("Status")),
                StaffNote = r.IsDBNull(r.GetOrdinal("StaffNote")) ? null : r.GetString(r.GetOrdinal("StaffNote")),
                DeclineReason = r.IsDBNull(r.GetOrdinal("DeclineReason")) ? null : r.GetString(r.GetOrdinal("DeclineReason")),
                IssuedBy = r.IsDBNull(r.GetOrdinal("IssuedBy")) ? null : r.GetInt32(r.GetOrdinal("IssuedBy")),
                IssuedByName = r.HasColumn("IssuedByName") && !r.IsDBNull(r.GetOrdinal("IssuedByName")) ? r.GetString(r.GetOrdinal("IssuedByName")) : null,
                IssuedOn = r.IsDBNull(r.GetOrdinal("IssuedOn")) ? null : r.GetDateTime(r.GetOrdinal("IssuedOn")),
                WithdrawnOn = r.IsDBNull(r.GetOrdinal("WithdrawnOn")) ? null : r.GetDateTime(r.GetOrdinal("WithdrawnOn")),
                CreatedOn = r.GetDateTime(r.GetOrdinal("CreatedOn")),
                SpaceId = r.HasColumn("SpaceId") && !r.IsDBNull(r.GetOrdinal("SpaceId")) ? r.GetInt32(r.GetOrdinal("SpaceId")) : null,
                SpaceName = r.HasColumn("SpaceName") && !r.IsDBNull(r.GetOrdinal("SpaceName")) ? r.GetString(r.GetOrdinal("SpaceName")) : null,
                LocationId = r.HasColumn("LocationId") && !r.IsDBNull(r.GetOrdinal("LocationId")) ? r.GetInt32(r.GetOrdinal("LocationId")) : null,
                LocationName = r.HasColumn("LocationName") && !r.IsDBNull(r.GetOrdinal("LocationName")) ? r.GetString(r.GetOrdinal("LocationName")) : null,
                BookingStartOn = r.HasColumn("BookingStartOn") && !r.IsDBNull(r.GetOrdinal("BookingStartOn")) ? r.GetDateTime(r.GetOrdinal("BookingStartOn")) : null,
                BookingEndOn = r.HasColumn("BookingEndOn") && !r.IsDBNull(r.GetOrdinal("BookingEndOn")) ? r.GetDateTime(r.GetOrdinal("BookingEndOn")) : null,
                MonthlyRent = r.HasColumn("MonthlyRent") && !r.IsDBNull(r.GetOrdinal("MonthlyRent")) ? r.GetDecimal(r.GetOrdinal("MonthlyRent")) : null,
                SecurityDepositHeld = r.HasColumn("SecurityDepositHeld") && !r.IsDBNull(r.GetOrdinal("SecurityDepositHeld")) ? r.GetDecimal(r.GetOrdinal("SecurityDepositHeld")) : 0m,
                SpaceTypeId = r.HasColumn("SpaceTypeId") && !r.IsDBNull(r.GetOrdinal("SpaceTypeId")) ? r.GetInt32(r.GetOrdinal("SpaceTypeId")) : null,
                SpaceTypeName = r.HasColumn("SpaceTypeName") && !r.IsDBNull(r.GetOrdinal("SpaceTypeName")) ? r.GetString(r.GetOrdinal("SpaceTypeName")) : null,
                SettlementId = r.HasColumn("SettlementId") && !r.IsDBNull(r.GetOrdinal("SettlementId")) ? r.GetInt32(r.GetOrdinal("SettlementId")) : null,
                SettledOn = r.HasColumn("SettledOn") && !r.IsDBNull(r.GetOrdinal("SettledOn")) ? r.GetDateTime(r.GetOrdinal("SettledOn")) : null
            };
        }

        public async Task<NoticePeriodItemDto?> SubmitNoticeDbAsync(int bookingId, int userId, string source, string? note)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_Submit", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@BookingId", bookingId);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Source", source);
            cmd.Parameters.AddWithValue("@StaffNote", (object?)note ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return MapItem(r);
            }
            return null;
        }

        public async Task<NoticePeriodItemDto?> WithdrawNoticeDbAsync(int noticeId, int userId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_Withdraw", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@NoticeId", noticeId);
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new NoticePeriodItemDto
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                    Status = r.GetString(r.GetOrdinal("Status")),
                    WithdrawnOn = r.GetDateTime(r.GetOrdinal("WithdrawnOn"))
                };
            }
            return null;
        }

        public async Task<List<NoticePeriodItemDto>> GetCustomerNoticesDbAsync(int userId)
        {
            var list = new List<NoticePeriodItemDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_GetCustomerList", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new NoticePeriodItemDto
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    PublicId = r.GetGuid(r.GetOrdinal("PublicId")),
                    BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                    CustomerId = r.GetInt32(r.GetOrdinal("CustomerId")),
                    CustomerCode = r.IsDBNull(r.GetOrdinal("CustomerCode")) ? null : r.GetString(r.GetOrdinal("CustomerCode")),
                    Source = r.GetString(r.GetOrdinal("Source")),
                    RequestedOn = r.GetDateTime(r.GetOrdinal("RequestedOn")),
                    NoticeDate = r.IsDBNull(r.GetOrdinal("NoticeDate")) ? null : r.GetDateTime(r.GetOrdinal("NoticeDate")),
                    NoticeDays = r.GetInt32(r.GetOrdinal("NoticeDays")),
                    ExitDate = r.IsDBNull(r.GetOrdinal("ExitDate")) ? null : r.GetDateTime(r.GetOrdinal("ExitDate")),
                    Status = r.GetString(r.GetOrdinal("Status")),
                    StaffNote = r.IsDBNull(r.GetOrdinal("StaffNote")) ? null : r.GetString(r.GetOrdinal("StaffNote")),
                    DeclineReason = r.IsDBNull(r.GetOrdinal("DeclineReason")) ? null : r.GetString(r.GetOrdinal("DeclineReason")),
                    IssuedOn = r.IsDBNull(r.GetOrdinal("IssuedOn")) ? null : r.GetDateTime(r.GetOrdinal("IssuedOn")),
                    WithdrawnOn = r.IsDBNull(r.GetOrdinal("WithdrawnOn")) ? null : r.GetDateTime(r.GetOrdinal("WithdrawnOn")),
                    SpaceName = r.IsDBNull(r.GetOrdinal("SpaceName")) ? null : r.GetString(r.GetOrdinal("SpaceName")),
                    LocationName = r.IsDBNull(r.GetOrdinal("LocationName")) ? null : r.GetString(r.GetOrdinal("LocationName")),
                    BookingStartOn = r.IsDBNull(r.GetOrdinal("BookingStartOn")) ? null : r.GetDateTime(r.GetOrdinal("BookingStartOn")),
                    BookingEndOn = r.IsDBNull(r.GetOrdinal("BookingEndOn")) ? null : r.GetDateTime(r.GetOrdinal("BookingEndOn"))
                });
            }
            return list;
        }

        public async Task<List<NoticePeriodItemDto>> GetStaffNoticesDbAsync(int? locationId, string? status, string? allowedLocationIds)
        {
            var list = new List<NoticePeriodItemDto>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_GetStaffList", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@LocationId", (object?)locationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AllowedLocationIds", (object?)allowedLocationIds ?? DBNull.Value);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(MapItem(r));
            }
            return list;
        }

        public async Task<NoticePeriodItemDto?> IssueNoticeDbAsync(int? noticeId, int? bookingId, DateTime noticeDate, int? noticeDays, string? staffNote, int issuedById)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_Issue", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@NoticeId", (object?)noticeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BookingId", (object?)bookingId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NoticeDate", noticeDate.Date);
            cmd.Parameters.AddWithValue("@NoticeDays", (object?)noticeDays ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StaffNote", (object?)staffNote ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IssuedById", issuedById);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return MapItem(r);
            }
            return null;
        }

        public async Task<NoticePeriodItemDto?> DeclineNoticeDbAsync(int noticeId, string reason, int declinedById)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_Decline", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@NoticeId", noticeId);
            cmd.Parameters.AddWithValue("@DeclineReason", reason);
            cmd.Parameters.AddWithValue("@DeclinedById", declinedById);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new NoticePeriodItemDto
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                    Status = r.GetString(r.GetOrdinal("Status")),
                    DeclineReason = r.GetString(r.GetOrdinal("DeclineReason"))
                };
            }
            return null;
        }

        public async Task<NoticePeriodItemDto?> UpdateNoticeDateDbAsync(int noticeId, DateTime noticeDate, string? staffNote, int updatedById)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticePeriods_UpdateNoticeDate", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@NoticeId", noticeId);
            cmd.Parameters.AddWithValue("@NoticeDate", noticeDate.Date);
            cmd.Parameters.AddWithValue("@StaffNote", (object?)staffNote ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedById", updatedById);

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new NoticePeriodItemDto
                {
                    Id = r.GetInt32(r.GetOrdinal("Id")),
                    BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                    NoticeDate = r.GetDateTime(r.GetOrdinal("NoticeDate")),
                    NoticeDays = r.GetInt32(r.GetOrdinal("NoticeDays")),
                    ExitDate = r.GetDateTime(r.GetOrdinal("ExitDate")),
                    Status = r.GetString(r.GetOrdinal("Status")),
                    StaffNote = r.IsDBNull(r.GetOrdinal("StaffNote")) ? null : r.GetString(r.GetOrdinal("StaffNote"))
                };
            }
            return null;
        }

        public async Task<SettlementPreCheckDto?> GetSettlementPreCheckDbAsync(int noticeId)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticeSettlement_GetPreCheck", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@NoticeId", noticeId);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var dto = new SettlementPreCheckDto
            {
                NoticeId = r.GetInt32(r.GetOrdinal("NoticeId")),
                BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                CustomerId = r.GetInt32(r.GetOrdinal("CustomerId")),
                UserId = r.GetInt32(r.GetOrdinal("UserId")),
                CustomerCode = r.IsDBNull(r.GetOrdinal("CustomerCode")) ? null : r.GetString(r.GetOrdinal("CustomerCode")),
                NoticeDate = r.IsDBNull(r.GetOrdinal("NoticeDate")) ? null : r.GetDateTime(r.GetOrdinal("NoticeDate")),
                NoticeDays = r.GetInt32(r.GetOrdinal("NoticeDays")),
                ExitDate = r.IsDBNull(r.GetOrdinal("ExitDate")) ? null : r.GetDateTime(r.GetOrdinal("ExitDate")),
                NoticeStatus = r.GetString(r.GetOrdinal("NoticeStatus")),
                CurrentDate = r.GetDateTime(r.GetOrdinal("CurrentDate")),
                AllowedToSettle = r.GetBoolean(r.GetOrdinal("AllowedToSettle")),
                SpaceId = r.IsDBNull(r.GetOrdinal("SpaceId")) ? null : r.GetInt32(r.GetOrdinal("SpaceId")),
                SpaceName = r.IsDBNull(r.GetOrdinal("SpaceName")) ? null : r.GetString(r.GetOrdinal("SpaceName")),
                SpaceTypeId = r.IsDBNull(r.GetOrdinal("SpaceTypeId")) ? null : r.GetInt32(r.GetOrdinal("SpaceTypeId")),
                SpaceTypeName = r.IsDBNull(r.GetOrdinal("SpaceTypeName")) ? null : r.GetString(r.GetOrdinal("SpaceTypeName")),
                LocationId = r.IsDBNull(r.GetOrdinal("LocationId")) ? null : r.GetInt32(r.GetOrdinal("LocationId")),
                LocationName = r.IsDBNull(r.GetOrdinal("LocationName")) ? null : r.GetString(r.GetOrdinal("LocationName")),
                MonthlyRent = r.IsDBNull(r.GetOrdinal("MonthlyRent")) ? 0m : r.GetDecimal(r.GetOrdinal("MonthlyRent")),
                DepositHeld = r.GetDecimal(r.GetOrdinal("DepositHeld"))
            };

            // Result set 2: Unpaid Invoices
            if (await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                {
                    dto.UnpaidInvoices.Add(new UnpaidInvoiceItemDto
                    {
                        InvoiceId = r.GetInt32(r.GetOrdinal("InvoiceId")),
                        InvoiceNumber = r.GetString(r.GetOrdinal("InvoiceNumber")),
                        IssuedOn = r.IsDBNull(r.GetOrdinal("IssuedOn")) ? null : r.GetDateTime(r.GetOrdinal("IssuedOn")),
                        DueOn = r.IsDBNull(r.GetOrdinal("DueOn")) ? null : r.GetDateTime(r.GetOrdinal("DueOn")),
                        BillingPeriodStart = r.IsDBNull(r.GetOrdinal("BillingPeriodStart")) ? null : r.GetDateTime(r.GetOrdinal("BillingPeriodStart")),
                        BillingPeriodEnd = r.IsDBNull(r.GetOrdinal("BillingPeriodEnd")) ? null : r.GetDateTime(r.GetOrdinal("BillingPeriodEnd")),
                        GrandTotal = r.GetDecimal(r.GetOrdinal("GrandTotal")),
                        PaidTotal = r.GetDecimal(r.GetOrdinal("PaidTotal")),
                        BalanceDue = r.GetDecimal(r.GetOrdinal("BalanceDue")),
                        StatusId = r.GetInt32(r.GetOrdinal("StatusId")),
                        StatusDescription = r.IsDBNull(r.GetOrdinal("StatusDescription")) ? null : r.GetString(r.GetOrdinal("StatusDescription"))
                    });
                }
            }

            // Result set 3: Advance Paid Rent
            if (await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                {
                    dto.AdvancePaidRents.Add(new AdvancePaidRentItemDto
                    {
                        InvoiceId = r.GetInt32(r.GetOrdinal("InvoiceId")),
                        InvoiceNumber = r.GetString(r.GetOrdinal("InvoiceNumber")),
                        BillingPeriodStart = r.IsDBNull(r.GetOrdinal("BillingPeriodStart")) ? null : r.GetDateTime(r.GetOrdinal("BillingPeriodStart")),
                        BillingPeriodEnd = r.IsDBNull(r.GetOrdinal("BillingPeriodEnd")) ? null : r.GetDateTime(r.GetOrdinal("BillingPeriodEnd")),
                        GrandTotal = r.GetDecimal(r.GetOrdinal("GrandTotal")),
                        PaidTotal = r.GetDecimal(r.GetOrdinal("PaidTotal")),
                        DaysAfterExitDate = r.GetInt32(r.GetOrdinal("DaysAfterExitDate")),
                        UnearnedAdvanceAmount = r.GetDecimal(r.GetOrdinal("UnearnedAdvanceAmount"))
                    });
                }
            }

            // Result set 4: Payout Accounts
            if (await r.NextResultAsync())
            {
                while (await r.ReadAsync())
                {
                    dto.PayoutAccounts.Add(new PayoutAccountItemDto
                    {
                        Id = r.GetInt32(r.GetOrdinal("Id")),
                        AccountCode = r.IsDBNull(r.GetOrdinal("AccountCode")) ? null : r.GetString(r.GetOrdinal("AccountCode")),
                        AccountName = r.GetString(r.GetOrdinal("AccountName")),
                        AccountNature = r.IsDBNull(r.GetOrdinal("AccountNature")) ? null : r.GetString(r.GetOrdinal("AccountNature")),
                        IsCashInHand = r.GetBoolean(r.GetOrdinal("IsCashInHand"))
                    });
                }
            }

            return dto;
        }

        public async Task<NoticeSettlementResultDto?> SaveSettlementDbAsync(SaveNoticeSettlementRequest request, int settledById)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_NoticeSettlement_Save", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@NoticeId", request.NoticeId);
            cmd.Parameters.AddWithValue("@DepositHeld", request.DepositHeld);
            cmd.Parameters.AddWithValue("@SettledById", settledById);
            cmd.Parameters.AddWithValue("@PayoutAccountId", request.PayoutAccountId);
            cmd.Parameters.AddWithValue("@SettledOn", request.SettledOn.Date);

            // Lines TVP
            var table = new DataTable();
            table.Columns.Add("LineType", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("Quantity", typeof(decimal));
            table.Columns.Add("Rate", typeof(decimal));
            table.Columns.Add("Amount", typeof(decimal));
            table.Columns.Add("SortOrder", typeof(int));

            int sort = 1;
            foreach (var line in request.Lines)
            {
                table.Rows.Add(line.LineType, line.Description, line.Quantity, line.Rate, line.Amount, sort++);
            }

            var tvp = cmd.Parameters.AddWithValue("@Lines", table);
            tvp.SqlDbType = SqlDbType.Structured;
            tvp.TypeName = "dbo.WN_NoticeSettlementLine";

            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new NoticeSettlementResultDto
                {
                    SettlementId = r.GetInt32(r.GetOrdinal("SettlementId")),
                    NoticeId = r.GetInt32(r.GetOrdinal("NoticeId")),
                    BookingId = r.GetInt32(r.GetOrdinal("BookingId")),
                    DepositHeld = r.GetDecimal(r.GetOrdinal("DepositHeld")),
                    TotalDeduction = r.GetDecimal(r.GetOrdinal("TotalDeduction")),
                    RefundAmount = r.GetDecimal(r.GetOrdinal("RefundAmount")),
                    ShortfallAmount = r.GetDecimal(r.GetOrdinal("ShortfallAmount")),
                    VoucherId = r.IsDBNull(r.GetOrdinal("VoucherId")) ? null : r.GetInt32(r.GetOrdinal("VoucherId")),
                    ShortfallInvoiceId = r.IsDBNull(r.GetOrdinal("ShortfallInvoiceId")) ? null : r.GetInt32(r.GetOrdinal("ShortfallInvoiceId")),
                    ShortfallInvoiceNumber = r.IsDBNull(r.GetOrdinal("ShortfallInvoiceNumber")) ? null : r.GetString(r.GetOrdinal("ShortfallInvoiceNumber")),
                    SettledOn = r.GetDateTime(r.GetOrdinal("SettledOn"))
                };
            }
            return null;
        }
    }

    internal static class SqlDataReaderExtensions
    {
        public static bool HasColumn(this SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}


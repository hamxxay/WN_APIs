using System;
using System.Collections.Generic;
using System.Data;
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

        public async Task<TransferPreviewResponseDto> GetTransferPreviewAsync(int bookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee)
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
                    OfferingTypeDiscountCap = reader.GetDecimal(reader.GetOrdinal("OfferingTypeDiscountCap"))
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
            int oldBookingId, int newSpaceId, DateTime effectiveDate, decimal transferFee, string notes, int? updatedById, string? userEmail)
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

            return list;
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
    }


}

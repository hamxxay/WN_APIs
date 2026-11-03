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
                    Capacity = reader.GetInt32(reader.GetOrdinal("TargetCapacity")),
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

            const string sql = @"
                SELECT 
                    b.Id AS BookingId,
                    b.IdGUID AS BookingPublicId,
                    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(c.FirstName, N' ', c.LastName))), N''), u.Name, u.Email) AS CustomerName,
                    COALESCE(c.Email, u.Email) AS CustomerEmail,
                    b.CustomerCode,
                    b.SpaceId,
                    s.Name AS SpaceName,
                    s.Code AS SpaceCode,
                    ISNULL(s.LocationId, 0) AS LocationId,
                    l.Name AS LocationName,
                    b.StartOn,
                    b.EndOn,
                    COALESCE(NULLIF(b.MonthlyRent, 0), NULLIF(b.MonthlyBasePrice, 0), 0.00) AS MonthlyRent,
                    ISNULL(b.DiscountPercentage, 0.00) AS DiscountPercentage,
                    b.OfferingType,
                    ISNULL(b.SecurityDepositPaid, 0.00) AS SecurityDepositPaid
                FROM dbo.WN_Bookings b WITH (NOLOCK)
                LEFT JOIN dbo.WN_Spaces s WITH (NOLOCK) ON s.Id = b.SpaceId
                LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
                LEFT JOIN dbo.WN_Customers c WITH (NOLOCK) ON c.Code = b.CustomerCode OR c.UserId = b.UserId
                LEFT JOIN dbo.WN_Users u WITH (NOLOCK) ON u.Id = b.UserId
                WHERE b.Id = @BookingId AND ISNULL(b.IsDeleted, 0) = 0;";

            await using var cmd = new SqlCommand(sql, conn);
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

            const string sql = @"
                SELECT 
                    s.Id AS SpaceId,
                    s.Name AS SpaceName,
                    s.Code AS SpaceCode,
                    ISNULL(s.LocationId, 0) AS LocationId,
                    l.Name AS LocationName,
                    ISNULL(s.Capacity, 1) AS Capacity,
                    ISNULL(s.Price, 0.00) AS MonthlyRate,
                    ISNULL(s.MaxDiscountPercent, 10.00) AS MaxDiscountPercent
                FROM dbo.WN_Spaces s WITH (NOLOCK)
                LEFT JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
                WHERE s.Id = @SpaceId;";

            await using var cmd = new SqlCommand(sql, conn);
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
                    Capacity = reader.GetInt32(reader.GetOrdinal("Capacity")),
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

            const string sql = @"
                SELECT 
                    s.Id AS SpaceId,
                    s.Name AS SpaceName,
                    s.Code AS SpaceCode,
                    s.LocationId,
                    l.Name AS LocationName,
                    ISNULL(s.Capacity, 1) AS Capacity,
                    ISNULL(s.Price, 0.00) AS MonthlyRate,
                    ISNULL(s.MaxDiscountPercent, 10.00) AS MaxDiscountPercent
                FROM dbo.WN_Spaces s WITH (NOLOCK)
                JOIN dbo.WN_Locations l WITH (NOLOCK) ON l.Id = s.LocationId
                WHERE s.LocationId = @LocationId
                  AND ISNULL(s.IsActive, 1) = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.WN_Bookings bk WITH (NOLOCK)
                      WHERE bk.SpaceId = s.Id
                        AND ISNULL(bk.IsDeleted, 0) = 0
                        AND bk.BookingStatusId IN (1, 2, 5, 33)
                        AND bk.Id <> @ExcludeBookingId
                        AND CAST(@EffectiveDate AS DATETIME2) < bk.EndOn
                        AND CAST(@EndDate AS DATETIME2) > bk.StartOn
                  )
                ORDER BY s.Name ASC;";

            await using var cmd = new SqlCommand(sql, conn);
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
                    Capacity = reader.GetInt32(reader.GetOrdinal("Capacity")),
                    MonthlyRate = reader.GetDecimal(reader.GetOrdinal("MonthlyRate")),
                    MaxDiscountPercent = reader.GetDecimal(reader.GetOrdinal("MaxDiscountPercent"))
                });
            }

            return list;
        }
    }
}


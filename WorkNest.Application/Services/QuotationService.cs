using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Quotation;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public class QuotationService : IQuotationService
    {
        private readonly IDbRepository _db;

        public QuotationService(IDbRepository db)
        {
            _db = db;
        }

        private static DateTime? ParseDateSafely(object? value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt;
            if (value is DateOnly dOnly) return dOnly.ToDateTime(TimeOnly.MinValue);
            if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
            return null;
        }

        private async Task<(decimal Hourly, decimal Daily, decimal Monthly, decimal SecurityDeposit, string Category, string SpaceName, string SpaceCode, int Capacity)> ResolveSpaceDetailsAsync(int spaceId)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var spaceRow = rows.FirstOrDefault(r => Convert.ToInt32(r["Id"]) == spaceId);
            if (spaceRow == null) throw new InvalidOperationException("Space not found.");

            string spaceName = spaceRow["Name"]?.ToString() ?? "";
            string spaceCode = spaceRow["Code"]?.ToString() ?? "";
            int capacity = spaceRow.ContainsKey("Capacity") && spaceRow["Capacity"] != null ? Convert.ToInt32(spaceRow["Capacity"]) : 1;
            string category = spaceRow["SpaceTypeName"]?.ToString() ?? "";

            // Try active pricing plan first
            var pricing = await _db.GetActivePricingForSpaceAsync(spaceId);
            decimal seatPrice = pricing != null && pricing.ContainsKey("SeatPrice") ? Convert.ToDecimal(pricing["SeatPrice"]) : 0;
            decimal secDeposit = pricing != null && pricing.ContainsKey("SecurityDeposit") ? Convert.ToDecimal(pricing["SecurityDeposit"]) : 0;

            decimal hourly = spaceRow.ContainsKey("PricePerHour") && spaceRow["PricePerHour"] != null ? Convert.ToDecimal(spaceRow["PricePerHour"]) : 0;
            decimal daily = spaceRow.ContainsKey("PricePerDay") && spaceRow["PricePerDay"] != null ? Convert.ToDecimal(spaceRow["PricePerDay"]) : 0;
            decimal monthly = spaceRow.ContainsKey("PricePerMonth") && spaceRow["PricePerMonth"] != null ? Convert.ToDecimal(spaceRow["PricePerMonth"]) : 0;

            if (seatPrice > 0)
            {
                monthly = seatPrice;
            }

            // Fallback values matching frontend manage.ts logic
            if (category.Contains("Private") || category.Contains("private"))
            {
                category = "PrivateOffice";
                if (monthly == 0) monthly = 35000;
                if (secDeposit == 0) secDeposit = monthly * capacity;
            }
            else if (category.Contains("Shared") || category.Contains("shared") || category.Contains("Coworking") || category.Contains("coworking"))
            {
                category = "SharedSpace";
                if (monthly == 0) monthly = 30000;
            }
            else if (category.Contains("Meeting") || category.Contains("meeting") || category.Contains("Conference") || category.Contains("conference"))
            {
                category = "MeetingRoom";
                if (hourly == 0) hourly = 6000;
            }

            return (hourly, daily, monthly, secDeposit, category, spaceName, spaceCode, capacity);
        }

        public async Task<QuotationResponse> CreateQuotationAsync(QuotationRequest request, int? createdById)
        {
            if (request.DiscountPercentage < 0 || request.DiscountPercentage > 100)
                throw new ArgumentException("Discount percentage must be between 0% and 100%.");

            var spaceDetails = await ResolveSpaceDetailsAsync(request.SpaceId);

            decimal subtotal = 0;
            var details = new List<QuotationDetailDto>();

            if (spaceDetails.Category == "MeetingRoom")
            {
                var diff = request.EndDateTime - request.StartDateTime;
                double hours = Math.Ceiling(diff.TotalHours);
                if (hours <= 0) hours = 1;

                decimal amount = spaceDetails.Hourly * (decimal)hours;
                subtotal = amount;

                details.Add(new QuotationDetailDto
                {
                    FeeType = "RoomRent",
                    Description = $"Meeting Room Rent ({hours} hours @ PKR {spaceDetails.Hourly}/hour)",
                    Quantity = (decimal)hours,
                    UnitPrice = spaceDetails.Hourly,
                    Amount = amount
                });
            }
            else if (spaceDetails.Category == "PrivateOffice")
            {
                // Calculate months
                int months = ((request.EndDateTime.Year - request.StartDateTime.Year) * 12) + request.EndDateTime.Month - request.StartDateTime.Month;
                if (months <= 0) months = 1;

                decimal monthlyRentOfRoom = spaceDetails.Monthly * spaceDetails.Capacity;
                decimal rentAmount = monthlyRentOfRoom * months;
                subtotal = rentAmount;

                details.Add(new QuotationDetailDto
                {
                    FeeType = "RoomRent",
                    Description = $"Private Office Rent ({months} months for Capacity {spaceDetails.Capacity} @ PKR {spaceDetails.Monthly}/seat/month)",
                    Quantity = months,
                    UnitPrice = monthlyRentOfRoom,
                    Amount = rentAmount
                });

                // Security deposit (usually equal to 1 month room rent)
                decimal secDeposit = spaceDetails.SecurityDeposit > 0 ? spaceDetails.SecurityDeposit : monthlyRentOfRoom;
                details.Add(new QuotationDetailDto
                {
                    FeeType = "SecurityDeposit",
                    Description = "Security Deposit (Refundable)",
                    Quantity = 1,
                    UnitPrice = secDeposit,
                    Amount = secDeposit
                });
            }
            else // SharedSpace
            {
                int months = ((request.EndDateTime.Year - request.StartDateTime.Year) * 12) + request.EndDateTime.Month - request.StartDateTime.Month;
                if (months <= 0) months = 1;

                decimal rentAmount = spaceDetails.Monthly * months;
                subtotal = rentAmount;

                details.Add(new QuotationDetailDto
                {
                    FeeType = "RoomRent",
                    Description = $"Shared Space Rent ({months} months @ PKR {spaceDetails.Monthly}/month)",
                    Quantity = months,
                    UnitPrice = spaceDetails.Monthly,
                    Amount = rentAmount
                });
            }

            // Generate unique Quotation Number (e.g. WN-QT-YYYYMMDD-XXXXXX)
            string todayStr = DateTime.UtcNow.ToString("yyyyMMdd");
            string randomStr = Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
            string quotationNumber = $"WN-QT-{todayStr}-{randomStr}";

            // Insert quotation into DB
            var result = await _db.InsertQuotationAsync(
                quotationNumber,
                request.ValidUntil,
                request.CustomerId,
                request.SpaceId,
                request.StartDateTime,
                request.EndDateTime,
                subtotal,
                request.DiscountPercentage,
                request.Remarks,
                createdById
            );

            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                throw new InvalidOperationException(err.ToString());

            int quotationId = Convert.ToInt32(result["Id"]);

            // Insert quotation details
            foreach (var det in details)
            {
                string sql = $@"
INSERT INTO dbo.WN_QuotationDetails (QuotationId, FeeType, Description, Quantity, UnitPrice, Amount, CreatedById) " +
                             $"VALUES ({quotationId}, '{det.FeeType}', '{det.Description.Replace("'", "''")}', {det.Quantity}, {det.UnitPrice}, {det.Amount}, {(createdById.HasValue ? createdById.Value.ToString() : "NULL")})";
                await _db.ExecuteRawSqlAsync(sql);
            }

            var response = await GetQuotationByIdAsync(quotationId);
            return response ?? throw new InvalidOperationException("Failed to retrieve generated quotation.");
        }

        public async Task<QuotationResponse?> GetQuotationByIdAsync(int id)
        {
            var header = await _db.GetQuotationByIdAsync(id);
            if (header == null) return null;

            var detailsRows = await _db.GetQuotationDetailsAsync(id);

            var dto = new QuotationResponse
            {
                Id = Convert.ToInt32(header["Id"]),
                Guid = header["Guid"]?.ToString(),
                QuotationNumber = header["QuotationNumber"]?.ToString() ?? "",
                QuotationDate = Convert.ToDateTime(header["QuotationDate"]),
                ValidUntil = Convert.ToDateTime(header["ValidUntil"]),
                CustomerId = Convert.ToInt32(header["CustomerId"]),
                CustomerName = header["CustomerName"]?.ToString(),
                CustomerEmail = header["CustomerEmail"]?.ToString(),
                SpaceId = Convert.ToInt32(header["SpaceId"]),
                SpaceName = header["SpaceName"]?.ToString(),
                SpaceCode = header["SpaceCode"]?.ToString(),
                LocationName = header["LocationName"]?.ToString(),
                SpaceTypeName = header["SpaceTypeName"]?.ToString(),
                StartDateTime = Convert.ToDateTime(header["StartDateTime"]),
                EndDateTime = Convert.ToDateTime(header["EndDateTime"]),
                SubtotalAmount = Convert.ToDecimal(header["SubtotalAmount"]),
                DiscountPercentage = Convert.ToDecimal(header["DiscountPercentage"]),
                DiscountAmount = Convert.ToDecimal(header["DiscountAmount"]),
                TotalAmount = Convert.ToDecimal(header["TotalAmount"]),
                Remarks = header["Remarks"]?.ToString(),
                Status = header["Status"]?.ToString(),
                Version = Convert.ToInt32(header["Version"]),
                IsActive = Convert.ToBoolean(header["IsActive"])
            };

            foreach (var row in detailsRows)
            {
                dto.Details.Add(new QuotationDetailDto
                {
                    FeeType = row["FeeType"]?.ToString() ?? "",
                    Description = row["Description"]?.ToString() ?? "",
                    Quantity = Convert.ToDecimal(row["Quantity"]),
                    UnitPrice = Convert.ToDecimal(row["UnitPrice"]),
                    Amount = Convert.ToDecimal(row["Amount"])
                });
            }

            return dto;
        }

        public async Task<(IEnumerable<QuotationResponse> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search)
        {
            var (rows, total) = await _db.GetQuotationsAsync(page, limit, search);
            var list = new List<QuotationResponse>();

            foreach (var r in rows)
            {
                list.Add(new QuotationResponse
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Guid = r["Guid"]?.ToString(),
                    QuotationNumber = r["QuotationNumber"]?.ToString() ?? "",
                    QuotationDate = Convert.ToDateTime(r["QuotationDate"]),
                    ValidUntil = Convert.ToDateTime(r["ValidUntil"]),
                    CustomerId = Convert.ToInt32(r["CustomerId"]),
                    CustomerName = r["CustomerName"]?.ToString(),
                    CustomerEmail = r["CustomerEmail"]?.ToString(),
                    SpaceId = Convert.ToInt32(r["SpaceId"]),
                    SpaceName = r["SpaceName"]?.ToString(),
                    SpaceCode = r["SpaceCode"]?.ToString(),
                    LocationName = r["LocationName"]?.ToString(),
                    SpaceTypeName = r["SpaceTypeName"]?.ToString(),
                    StartDateTime = Convert.ToDateTime(r["StartDateTime"]),
                    EndDateTime = Convert.ToDateTime(r["EndDateTime"]),
                    SubtotalAmount = Convert.ToDecimal(r["SubtotalAmount"]),
                    DiscountPercentage = Convert.ToDecimal(r["DiscountPercentage"]),
                    DiscountAmount = Convert.ToDecimal(r["DiscountAmount"]),
                    TotalAmount = Convert.ToDecimal(r["TotalAmount"]),
                    Remarks = r["Remarks"]?.ToString(),
                    Status = r["Status"]?.ToString(),
                    Version = Convert.ToInt32(r["Version"]),
                    IsActive = Convert.ToBoolean(r["IsActive"])
                });
            }

            return (list, total);
        }

        public async Task<IEnumerable<QuotationResponse>> GetQuotationHistoryAsync(int customerId, int spaceId)
        {
            var rows = await _db.GetQuotationHistoryAsync(customerId, spaceId);
            var list = new List<QuotationResponse>();

            foreach (var r in rows)
            {
                list.Add(new QuotationResponse
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Guid = r["Guid"]?.ToString(),
                    QuotationNumber = r["QuotationNumber"]?.ToString() ?? "",
                    QuotationDate = Convert.ToDateTime(r["QuotationDate"]),
                    ValidUntil = Convert.ToDateTime(r["ValidUntil"]),
                    CustomerId = Convert.ToInt32(r["CustomerId"]),
                    SpaceId = Convert.ToInt32(r["SpaceId"]),
                    StartDateTime = Convert.ToDateTime(r["StartDateTime"]),
                    EndDateTime = Convert.ToDateTime(r["EndDateTime"]),
                    SubtotalAmount = Convert.ToDecimal(r["SubtotalAmount"]),
                    DiscountPercentage = Convert.ToDecimal(r["DiscountPercentage"]),
                    DiscountAmount = Convert.ToDecimal(r["DiscountAmount"]),
                    TotalAmount = Convert.ToDecimal(r["TotalAmount"]),
                    Remarks = r["Remarks"]?.ToString(),
                    Status = r["Status"]?.ToString(),
                    Version = Convert.ToInt32(r["Version"]),
                    IsActive = Convert.ToBoolean(r["IsActive"])
                });
            }

            return list;
        }

        public async Task<IDictionary<string, object?>> ConvertQuotationToBookingAsync(int quotationId, int? createdById)
        {
            var result = await _db.ConvertQuotationToBookingAsync(quotationId, createdById);
            if (result.TryGetValue("ErrorMessage", out var err) && err is not null && !string.IsNullOrWhiteSpace(err.ToString()))
                throw new InvalidOperationException(err.ToString());

            if (result.TryGetValue("BookingId", out var bid) && bid is not null)
            {
                try
                {
                    int bookingId = Convert.ToInt32(bid);
                    string sql = $@"

                        DECLARE @BId INT = {bookingId};
                        DECLARE @UId INT = NULL;
                        DECLARE @Start DATETIME2 = NULL;
                        DECLARE @End DATETIME2 = NULL;
                        DECLARE @IsPrivate BIT = 0;

                        SELECT 
                            @UId = b.UserId,
                            @Start = b.StartOn,
                            @End = b.EndOn,
                            @IsPrivate = CASE 
                                WHEN st.CategoryId = 1 
                                  OR LOWER(ISNULL(st.Name, '')) LIKE '%private%'
                                  OR LOWER(ISNULL(st.DisplayName, '')) LIKE '%private%'
                                  OR LOWER(ISNULL(s.CategoryCode, '')) LIKE '%private%'
                                  OR LOWER(ISNULL(s.Name, '')) LIKE '%private%'
                                THEN 1 ELSE 0 END
                        FROM dbo.WN_Bookings b
                        JOIN dbo.WN_Spaces s ON s.Id = b.SpaceId
                        LEFT JOIN dbo.WN_SpaceTypes st ON st.Id = s.SpaceTypeIdInt OR st.IdGUID = s.SpaceTypeId
                        WHERE b.Id = @BId;

                        IF @IsPrivate = 1 AND @UId IS NOT NULL AND @UId > 0 AND @Start IS NOT NULL AND @End IS NOT NULL
                        BEGIN
                            EXEC dbo.WN_BookingMeetingRoomEntitlements_Generate 
                                @BookingId = @BId, 
                                @UserId = @UId, 
                                @StartOn = @Start, 
                                @EndOn = @End;
                        END";

                    await _db.ExecuteRawSqlAsync(sql);
                }
                catch
                {
                    // Continue even if entitlement generation encounters non-critical error
                }
            }

            return result;
        }
    }
}

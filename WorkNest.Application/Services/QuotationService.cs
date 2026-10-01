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
        private readonly IEmailService _email;
        private readonly IPdfService _pdf;

        public QuotationService(IDbRepository db, IEmailService email, IPdfService pdf)
        {
            _db = db;
            _email = email;
            _pdf = pdf;
        }

        private static DateTime? ParseDateSafely(object? value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt;
            if (value is DateOnly dOnly) return dOnly.ToDateTime(TimeOnly.MinValue);
            if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
            return null;
        }

        private async Task<(decimal Hourly, decimal Daily, decimal Monthly, decimal SecurityDeposit, string Category, string SpaceName, string SpaceCode, int Capacity, decimal MaxDiscountPercent)> ResolveSpaceDetailsAsync(int spaceId)
        {
            var (rows, _) = await _db.GetSpacesAsync(1, 10000, null);
            var spaceRow = rows.FirstOrDefault(r => Convert.ToInt32(r["Id"]) == spaceId);
            if (spaceRow == null) throw new InvalidOperationException("Space not found.");

            string spaceName = spaceRow["Name"]?.ToString() ?? "";
            string spaceCode = spaceRow["Code"]?.ToString() ?? "";
            int capacity = spaceRow.ContainsKey("Capacity") && spaceRow["Capacity"] != null ? Convert.ToInt32(spaceRow["Capacity"]) : 1;
            decimal maxDiscountPercent = spaceRow.ContainsKey("MaxDiscountPercent") && spaceRow["MaxDiscountPercent"] != null ? Convert.ToDecimal(spaceRow["MaxDiscountPercent"]) : 20.00m;
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

            return (hourly, daily, monthly, secDeposit, category, spaceName, spaceCode, capacity, maxDiscountPercent);
        }

        public async Task<QuotationResponse> CreateQuotationAsync(QuotationRequest request, int? createdById)
        {
            var spaceDetails = await ResolveSpaceDetailsAsync(request.SpaceId);

            // Base price validation: can only be increased, NOT decreased below standard space rate
            if (request.PerSeatBasePrice.HasValue && request.PerSeatBasePrice.Value > 0)
            {
                if (request.PerSeatBasePrice.Value < spaceDetails.Monthly)
                {
                    throw new ArgumentException($"Base price for {spaceDetails.SpaceName} is locked and can only be increased. Minimum allowed base price is PKR {spaceDetails.Monthly:N2}.");
                }
            }

            // PerSeatBasePrice, Capacity, MonthlyBasePrice calculations
            decimal perSeatBasePrice = request.PerSeatBasePrice.HasValue && request.PerSeatBasePrice.Value > 0
                ? request.PerSeatBasePrice.Value
                : spaceDetails.Monthly;
            int capacity = spaceDetails.Capacity;
            decimal monthlyBasePrice = perSeatBasePrice * capacity;

            // Validate discount
            var discountType = string.IsNullOrWhiteSpace(request.DiscountType) ? "Percentage" : request.DiscountType;
            var discountValue = request.DiscountValue > 0 ? request.DiscountValue : request.DiscountPercentage;

            // Lookup offering type dynamically from dbo.WN_OfferingType
            var offeringTypes = (await _db.GetOfferingTypesAsync()).ToList();
            IDictionary<string, object?>? matchedOt = null;
            if (request.OfferingTypeId.HasValue && request.OfferingTypeId.Value > 0)
            {
                matchedOt = offeringTypes.FirstOrDefault(o => Convert.ToInt32(o["Id"]) == request.OfferingTypeId.Value);
            }
            if (matchedOt == null && !string.IsNullOrWhiteSpace(request.OfferingType))
            {
                if (int.TryParse(request.OfferingType, out int parsedOtId))
                {
                    matchedOt = offeringTypes.FirstOrDefault(o => Convert.ToInt32(o["Id"]) == parsedOtId);
                }
                if (matchedOt == null)
                {
                    matchedOt = offeringTypes.FirstOrDefault(o => string.Equals(o["Description"]?.ToString(), request.OfferingType, StringComparison.OrdinalIgnoreCase));
                }
                if (matchedOt == null)
                {
                    matchedOt = offeringTypes.FirstOrDefault(o => (o["Description"]?.ToString() ?? "").StartsWith(request.OfferingType, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (matchedOt == null && offeringTypes.Any())
            {
                matchedOt = offeringTypes.First();
            }

            int resolvedOfferingTypeId = matchedOt != null ? Convert.ToInt32(matchedOt["Id"]) : 1;
            decimal maxDiscountCap = matchedOt != null ? Convert.ToDecimal(matchedOt["DiscountCap"]) : 10.00m;
            string offeringTypeDbValue = resolvedOfferingTypeId.ToString();

            if (discountType == "Percentage" || discountType == "Percent")
            {
                if (discountValue > maxDiscountCap)
                    discountValue = maxDiscountCap;
            }
            else if (discountType == "Fixed" || discountType == "Amount")
            {
                decimal maxAllowedFixed = Math.Round(monthlyBasePrice * (maxDiscountCap / 100m), 2);
                if (discountValue > maxAllowedFixed)
                    discountValue = maxAllowedFixed;
            }

            // For backward compat: if DiscountType is Percentage, keep discountPercentage
            decimal discountPercentage = (discountType == "Percentage" || discountType == "Percent") ? discountValue : 0;

            decimal subtotal = 0;
            var details = new List<QuotationDetailDto>();

            // Calculate effective rent discount percentage for mirroring to deposit (Task 1)
            decimal rentDiscountPct = (discountType == "Amount" || discountType == "Fixed")
                ? (monthlyBasePrice > 0 ? (discountValue / monthlyBasePrice) * 100m : 0m)
                : discountPercentage;

            if (spaceDetails.Category == "MeetingRoom")
            {
                var diff = request.EndDateTime - request.StartDateTime;
                double totalHours = Math.Ceiling(diff.TotalHours);
                if (totalHours <= 0) totalHours = 1;

                bool isDaily = spaceDetails.Daily > 0 && (totalHours >= 24 || spaceDetails.Hourly == 0);

                if (isDaily)
                {
                    int days = (int)Math.Max(1, Math.Ceiling(totalHours / 24.0));
                    decimal amount = spaceDetails.Daily * days;
                    subtotal = amount;

                    details.Add(new QuotationDetailDto
                    {
                        FeeType = "RoomRent",
                        Description = $"Meeting Room Rent ",
                        Quantity = days,
                        UnitPrice = spaceDetails.Daily,
                        Amount = amount
                    });
                }
                else
                {
                    decimal rate = spaceDetails.Hourly > 0 ? spaceDetails.Hourly : (spaceDetails.Daily > 0 ? spaceDetails.Daily : 1000m);
                    decimal amount = rate * (decimal)totalHours;
                    subtotal = amount;

                    details.Add(new QuotationDetailDto
                    {
                        FeeType = "RoomRent",
                        Description = $"Meeting Room Rent",
                        Quantity = (decimal)totalHours,
                        UnitPrice = rate,
                        Amount = amount
                    });
                }
            }
            else if (spaceDetails.Category == "PrivateOffice")
            {
                // Calculate months
                int months = ((request.EndDateTime.Year - request.StartDateTime.Year) * 12) + request.EndDateTime.Month - request.StartDateTime.Month;
                if (months <= 0) months = 1;

                decimal monthlyRentOfRoom = monthlyBasePrice;
                decimal rentAmount = monthlyRentOfRoom * months;
                subtotal = rentAmount;

                details.Add(new QuotationDetailDto
                {
                    FeeType = "RoomRent",
                    Description = $"Private Office",
                    Quantity = months,
                    UnitPrice = monthlyRentOfRoom,
                    Amount = rentAmount
                });

                // Security deposit (discount applied once)
                int secMonths = request.SecurityDepositMonths.HasValue
                    ? request.SecurityDepositMonths.Value
                    : (request.SecurityDepositOverride.HasValue && request.SecurityDepositOverride.Value == 0 ? 0 : 1);
                decimal baseSecDeposit = request.SecurityDepositOverride.HasValue
                    ? request.SecurityDepositOverride.Value
                    : (monthlyRentOfRoom * secMonths);

                decimal discountedSecDeposit = request.SecurityDepositOverride.HasValue
                    ? request.SecurityDepositOverride.Value
                    : (baseSecDeposit > 0 ? Math.Max(0, Math.Round(baseSecDeposit * (1 - (rentDiscountPct / 100m)), 2)) : 0m);

                if (discountedSecDeposit > 0 && secMonths > 0)
                {
                    details.Add(new QuotationDetailDto
                    {
                        FeeType = "SecurityDeposit",
                        Description = $"Security Deposit ({secMonths} Month{(secMonths > 1 ? "s" : "")}) (Refundable)",
                        Quantity = secMonths,
                        UnitPrice = secMonths > 0 ? Math.Round(discountedSecDeposit / secMonths, 2) : discountedSecDeposit,
                        Amount = discountedSecDeposit
                    });
                }
            }
            else // SharedSpace
            {
                int months = ((request.EndDateTime.Year - request.StartDateTime.Year) * 12) + request.EndDateTime.Month - request.StartDateTime.Month;
                if (months <= 0) months = 1;

                decimal rentAmount = monthlyBasePrice * months;
                subtotal = rentAmount;

                details.Add(new QuotationDetailDto
                {
                    FeeType = "RoomRent",
                    Description = $"Shared Space",
                    Quantity = months,
                    UnitPrice = monthlyBasePrice,
                    Amount = rentAmount
                });
            }

            // Generate unique Quotation Number (e.g. WN-QT-YYYYMMDD-XXXXXX)
            string todayStr = DateTime.UtcNow.ToString("yyyyMMdd");
            string randomStr = Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
            string quotationNumber = $"WN-QT-{todayStr}-{randomStr}";

            // Calculate discount and total amounts against room rent subtotal
            decimal discountAmount = (discountType == "Amount" || discountType == "Fixed")
                ? Math.Min(discountValue, subtotal)
                : Math.Round(subtotal * (discountValue / 100m), 2);
            decimal totalAmount = Math.Max(0, subtotal - discountAmount);

            // Insert quotation into DB
            int billingPeriodMonths = request.BillingPeriodMonths.HasValue && request.BillingPeriodMonths.Value > 0
                ? request.BillingPeriodMonths.Value
                : (spaceDetails.Category == "MeetingRoom" ? 1 : 3);

            int securityDepositMonths = request.SecurityDepositMonths.HasValue
                ? request.SecurityDepositMonths.Value
                : (request.SecurityDepositOverride.HasValue && request.SecurityDepositOverride.Value == 0 ? 0 : 1);

            decimal baseDepositForInsert = request.SecurityDepositOverride.HasValue
                ? request.SecurityDepositOverride.Value
                : (spaceDetails.Category == "PrivateOffice" ? monthlyBasePrice * securityDepositMonths : 0);

            decimal calculatedSecDeposit = request.SecurityDepositOverride.HasValue
                ? request.SecurityDepositOverride.Value
                : (baseDepositForInsert > 0 ? Math.Max(0, Math.Round(baseDepositForInsert * (1 - (rentDiscountPct / 100m)), 2)) : 0m);

            var result = await _db.InsertQuotationAsync(
                quotationNumber,
                request.ValidUntil,
                request.CustomerId,
                request.SpaceId,
                request.StartDateTime,
                request.EndDateTime,
                subtotal,
                discountPercentage,
                request.Remarks,
                createdById,
                discountType,
                discountValue,
                request.SecurityDepositOverride,
                request.FloorId,
                billingPeriodMonths,
                perSeatBasePrice,
                capacity,
                monthlyBasePrice,
                maxDiscountCap,
                securityDepositMonths,
                calculatedSecDeposit,
                offeringTypeDbValue,
                request.WithholdingTaxRate ?? 15.00m
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
                OfferingTypeId = header.TryGetValue("OfferingTypeId", out var otid) && otid is not null ? Convert.ToInt32(otid) : (header.TryGetValue("OfferingType", out var otv) && int.TryParse(otv?.ToString(), out var pOtId) ? pOtId : 1),
                OfferingType = header.TryGetValue("OfferingType", out var otStr) && otStr is not null ? otStr.ToString() : "1",
                OfferingTypeDescription = header.TryGetValue("OfferingTypeDescription", out var otd) && otd is not null ? otd.ToString() : (header.TryGetValue("OfferingType", out var otStr2) ? otStr2?.ToString() : "24-by-7"),
                OfferingTypeDiscountCap = header.TryGetValue("OfferingTypeDiscountCap", out var otdc) && otdc is not null ? Convert.ToDecimal(otdc) : 10.00m,
                QuotationNumber = header["QuotationNumber"]?.ToString() ?? "",
                QuotationDate = Convert.ToDateTime(header["QuotationDate"]),
                ValidUntil = Convert.ToDateTime(header["ValidUntil"]),
                CustomerId = Convert.ToInt32(header["CustomerId"]),
                CustomerName = header["CustomerName"]?.ToString(),
                CustomerCompany = header.TryGetValue("CustomerCompany", out var cc) && cc is not null ? cc.ToString() : null,
                CustomerEmail = header["CustomerEmail"]?.ToString(),
                CustomerAddress = header.TryGetValue("CustomerAddress", out var ca) && ca is not null ? ca.ToString() : null,
                SpaceId = Convert.ToInt32(header["SpaceId"]),
                SpaceName = header["SpaceName"]?.ToString(),
                SpaceCode = header["SpaceCode"]?.ToString(),
                LocationName = header["LocationName"]?.ToString(),
                LocationAddress = header.TryGetValue("LocationAddress", out var la) && la is not null ? la.ToString() : null,
                CityName = header.TryGetValue("CityName", out var cn) && cn is not null ? cn.ToString() : null,
                SpaceTypeName = header["SpaceTypeName"]?.ToString(),
                StartDateTime = Convert.ToDateTime(header["StartDateTime"]),
                EndDateTime = Convert.ToDateTime(header["EndDateTime"]),
                SubtotalAmount = Convert.ToDecimal(header["SubtotalAmount"]),
                PerSeatBasePrice = header.TryGetValue("PerSeatBasePrice", out var psbp) && psbp is not null ? Convert.ToDecimal(psbp) : null,
                Capacity = header.TryGetValue("Capacity", out var cap) && cap is not null ? Convert.ToInt32(cap) : null,
                MonthlyBasePrice = header.TryGetValue("MonthlyBasePrice", out var mbp) && mbp is not null ? Convert.ToDecimal(mbp) : null,
                MaxDiscountPercent = header.TryGetValue("OfferingTypeDiscountCap", out var otdc2) && otdc2 is not null && Convert.ToDecimal(otdc2) > 0 ? Convert.ToDecimal(otdc2) : (header.TryGetValue("MaxDiscountPercent", out var mdp) && mdp is not null ? Convert.ToDecimal(mdp) : 10.00m),
                DiscountType = header.TryGetValue("DiscountType", out var dt) && dt is not null ? dt.ToString()! : "Percentage",
                DiscountPercentage = Convert.ToDecimal(header["DiscountPercentage"]),
                DiscountValue = header.TryGetValue("DiscountType", out var dt2) && dt2?.ToString() == "Amount"
                    ? Convert.ToDecimal(header["DiscountAmount"])
                    : Convert.ToDecimal(header["DiscountPercentage"]),
                DiscountAmount = Convert.ToDecimal(header["DiscountAmount"]),
                SecurityDeposit = header.TryGetValue("SecurityDeposit", out var sd) && sd is not null ? Convert.ToDecimal(sd) : 0,
                TotalAmount = Convert.ToDecimal(header["TotalAmount"]),
                SupportChargesId = header.TryGetValue("SupportChargesId", out var scid) && scid is not null ? Convert.ToByte(scid) : (byte)4,
                AppliedChargePercentage = header.TryGetValue("AppliedChargePercentage", out var acp) && acp is not null ? Convert.ToDecimal(acp) : 10.00m,
                AppliedTaxPercentage = header.TryGetValue("AppliedTaxPercentage", out var atp) && atp is not null ? Convert.ToDecimal(atp) : 16.00m,
                SupportChargeAmount = header.TryGetValue("SupportChargeAmount", out var sca) && sca is not null ? Convert.ToDecimal(sca) : Math.Round((Convert.ToDecimal(header["SubtotalAmount"]) - Convert.ToDecimal(header["DiscountAmount"])) * 0.10m, 2),
                TaxAmount = header.TryGetValue("TaxAmount", out var ta) && ta is not null ? Convert.ToDecimal(ta) : Math.Round(Math.Round((Convert.ToDecimal(header["SubtotalAmount"]) - Convert.ToDecimal(header["DiscountAmount"])) * 0.10m, 2) * 0.16m, 2),
                WithholdingTaxRate = header.TryGetValue("WithholdingTaxRate", out var wtr) && wtr is not null && Convert.ToDecimal(wtr) > 0 ? Convert.ToDecimal(wtr) : 15.00m,
                Remarks = header["Remarks"]?.ToString(),
                Status = header["Status"]?.ToString(),
                Version = Convert.ToInt32(header["Version"]),
                IsActive = Convert.ToBoolean(header["IsActive"]),
                BillingPeriodMonths = header.TryGetValue("BillingPeriodMonths", out var bpm) && bpm is not null ? Convert.ToInt32(bpm) : 3,
                SecurityDepositMonths = header.TryGetValue("SecurityDepositMonths", out var sdm) && sdm is not null && sdm != DBNull.Value ? Convert.ToInt32(sdm) : 0,
                BillingPeriod = header.TryGetValue("BillingPeriod", out var bp) && bp is not null ? bp.ToString() : "3 Months (Quarterly)",
                BillingPeriodLabel = header.TryGetValue("BillingPeriodLabel", out var bpl) && bpl is not null ? bpl.ToString() : "3 Months (Quarterly)",
                MonthlyRent = header.TryGetValue("MonthlyRent", out var mr) && mr is not null ? Convert.ToDecimal(mr) : 0,
                CurrentCycleAmount = header.TryGetValue("CurrentCycleAmount", out var cca) && cca is not null ? Convert.ToDecimal(cca) : 0,
                TotalContractAmount = header.TryGetValue("TotalContractAmount", out var tca) && tca is not null ? Convert.ToDecimal(tca) : Convert.ToDecimal(header["TotalAmount"])
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

            ChallanCalculationService.ApplyToQuotation(dto);

            dto.CanRespond = string.Equals(dto.Status, "Sent", StringComparison.OrdinalIgnoreCase);

            dto.Contract = new WorkNest.Application.DTOs.Booking.ContractDetailsDto
            {
                SpaceNumber = dto.SpaceCode ?? dto.SpaceName,
                BillingPeriod = dto.BillingPeriod,
                ContractStartDate = dto.StartDateTime,
                ContractEndDate = dto.EndDateTime,
                NumberOfMonths = ((dto.EndDateTime.Year - dto.StartDateTime.Year) * 12) + dto.EndDateTime.Month - dto.StartDateTime.Month > 0
                    ? ((dto.EndDateTime.Year - dto.StartDateTime.Year) * 12) + dto.EndDateTime.Month - dto.StartDateTime.Month
                    : 1,
                MonthlyRent = dto.MonthlyRent > 0 ? dto.MonthlyRent : dto.SubtotalAmount,
                CurrentCycleAmount = dto.CurrentCycleAmount,
                TotalContractAmount = dto.TotalContractAmount,
                SecurityDeposit = dto.SecurityDeposit,
                BalanceLeft = dto.TotalContractAmount,
                NextBillDueDate = dto.StartDateTime,
                AppliedTaxPercentage = dto.AppliedTaxPercentage,
                TaxAmount = dto.TaxAmountOnAdvanceRent,
                TaxAmountOnAdvanceRent = dto.TaxAmountOnAdvanceRent,
                TaxAmountOnContract = dto.TaxAmountOnContract
            };

            try
            {
                var actRows = await _db.GetQuotationActivitiesAsync(id, 20);
                foreach (var a in actRows)
                {
                    dto.Activities.Add(new QuotationActivityDto
                    {
                        Id = Convert.ToInt32(a["Id"]),
                        QuotationId = Convert.ToInt32(a["QuotationId"]),
                        Version = Convert.ToInt32(a["Version"]),
                        ActivityType = a["ActivityType"]?.ToString() ?? "",
                        Message = a["Message"]?.ToString() ?? "",
                        CustomerNote = a["CustomerNote"]?.ToString(),
                        CreatedDate = Convert.ToDateTime(a["CreatedDate"])
                    });
                }
                var lastResp = dto.Activities.FirstOrDefault(a => a.ActivityType == "Accepted" || a.ActivityType == "Declined");
                if (lastResp != null)
                {
                    dto.CustomerNote = lastResp.CustomerNote;
                    dto.ResponseDate = lastResp.CreatedDate;
                }
            }
            catch { }

            return dto;
        }

        public async Task<(IEnumerable<QuotationResponse> Rows, int Total)> GetQuotationsAsync(int page, int limit, string? search, int? locationId = null)
        {
            var (rows, total) = await _db.GetQuotationsAsync(page, limit, search, locationId);
            var list = new List<QuotationResponse>();

            foreach (var r in rows)
            {
                int id = Convert.ToInt32(r["Id"]);
                var fullQuotation = await GetQuotationByIdAsync(id);
                if (fullQuotation != null)
                {
                    list.Add(fullQuotation);
                }
            }

            return (list, total);
        }

        public async Task<IEnumerable<QuotationResponse>> GetQuotationHistoryAsync(int customerId, int spaceId)
        {
            // Get all quotations and filter by customerId and spaceId
            var (rows, _) = await _db.GetQuotationsAsync(1, 10000, null);
            var filteredRows = rows.Where(r => 
                Convert.ToInt32(r["CustomerId"]) == customerId &&
                Convert.ToInt32(r["SpaceId"]) == spaceId
            ).ToList();
            var filteredRowsCollection = (IEnumerable<IDictionary<string, object?>>)filteredRows;
            var list = new List<QuotationResponse>();

            foreach (var r in filteredRowsCollection)
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
                    IsActive = Convert.ToBoolean(r["IsActive"]),
                    BillingPeriodMonths = r.TryGetValue("BillingPeriodMonths", out var bpm) && bpm is not null ? Convert.ToInt32(bpm) : 3,
                    BillingPeriod = r.TryGetValue("BillingPeriod", out var bp) && bp is not null ? bp.ToString() : "3 Months (Quarterly)",
                    BillingPeriodLabel = r.TryGetValue("BillingPeriodLabel", out var bpl) && bpl is not null ? bpl.ToString() : "3 Months (Quarterly)"
                });
            }

            return list;
        }

        public async Task<IEnumerable<QuotationResponse>> GetQuotationsByCustomerAsync(int customerId)
        {
            var rows = await _db.GetQuotationsByCustomerAsync(customerId);
            var list = new List<QuotationResponse>();

            foreach (var r in rows)
            {
                int id = r.TryGetValue("Id", out var idVal) && idVal is not null ? Convert.ToInt32(idVal) : 0;
                if (id > 0)
                {
                    var fullQuotation = await GetQuotationByIdAsync(id);
                    if (fullQuotation != null)
                    {
                        list.Add(fullQuotation);
                    }
                }
            }

            return list;
        }

        public async Task SendQuotationEmailAsync(int quotationId, string? overrideEmail)
        {
            var q = await GetQuotationByIdAsync(quotationId)
                ?? throw new InvalidOperationException("Quotation not found.");

            var targetEmail = overrideEmail ?? q.CustomerEmail
                ?? throw new InvalidOperationException("Recipient email address is required.");

            var pdf = _pdf.GenerateQuotationPdf(q);
            await _email.SendQuotationEmailAsync(targetEmail, q.CustomerName ?? "Valued Customer", q.QuotationNumber ?? $"QTN-{quotationId}", pdf);
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

        public async Task<QuotationResponse> AcceptQuotationAsync(int quotationId, int version, int customerId, string? note, int? userId)
        {
            await _db.AcceptQuotationAsync(quotationId, version, customerId, note, userId);
            var res = await GetQuotationByIdAsync(quotationId);
            return res ?? throw new InvalidOperationException("Failed to load accepted quotation.");
        }

        public async Task<QuotationResponse> DeclineQuotationAsync(int quotationId, int version, int customerId, string note, int? userId)
        {
            if (string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("Decline reason note is mandatory.");

            await _db.DeclineQuotationAsync(quotationId, version, customerId, note, userId);
            var res = await GetQuotationByIdAsync(quotationId);
            return res ?? throw new InvalidOperationException("Failed to load declined quotation.");
        }

        public async Task<QuotationResponse> CreateNewVersionAsync(int quotationId, int? createdById)
        {
            var resDict = await _db.CreateQuotationNewVersionAsync(quotationId, createdById);
            int newQuotationId = Convert.ToInt32(resDict["NewQuotationId"]);
            var res = await GetQuotationByIdAsync(newQuotationId);
            return res ?? throw new InvalidOperationException("Failed to load newly created quotation version.");
        }

        public async Task<IEnumerable<QuotationResponse>> GetVersionsAsync(int quotationId)
        {
            var rows = await _db.GetQuotationVersionsAsync(quotationId);
            var list = new List<QuotationResponse>();
            foreach (var r in rows)
            {
                int qid = Convert.ToInt32(r["Id"]);
                var qRes = await GetQuotationByIdAsync(qid);
                if (qRes != null) list.Add(qRes);
            }
            return list;
        }

        public async Task<IEnumerable<QuotationActivityDto>> GetActivitiesAsync(int? quotationId, int limit)
        {
            var rows = await _db.GetQuotationActivitiesAsync(quotationId, limit);
            var list = new List<QuotationActivityDto>();
            foreach (var a in rows)
            {
                list.Add(new QuotationActivityDto
                {
                    Id = Convert.ToInt32(a["Id"]),
                    QuotationId = Convert.ToInt32(a["QuotationId"]),
                    Version = Convert.ToInt32(a["Version"]),
                    ActivityType = a["ActivityType"]?.ToString() ?? "",
                    Message = a["Message"]?.ToString() ?? "",
                    CustomerNote = a["CustomerNote"]?.ToString(),
                    CreatedDate = Convert.ToDateTime(a["CreatedDate"])
                });
            }
            return list;
        }

        public async Task SendQuotationAsync(int quotationId, int? userId)
        {
            await _db.SendQuotationStatusAsync(quotationId, "Sent", userId);
        }

        public async Task<IEnumerable<OfferingTypeDto>> GetOfferingTypesAsync(bool? activeOnly = null)
        {
            var rows = await _db.GetOfferingTypesAsync(activeOnly);
            return rows.Select(r => new OfferingTypeDto
            {
                Id = Convert.ToInt32(r["Id"]),
                Description = r["Description"]?.ToString() ?? "",
                DiscountCap = Convert.ToDecimal(r["DiscountCap"]),
                Status = r.ContainsKey("Status") && r["Status"] != null && r["Status"] != DBNull.Value
                    ? Convert.ToBoolean(r["Status"])
                    : true
            }).ToList();
        }
    }
}

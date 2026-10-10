using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    public class AttendantService : IAttendantService
    {
        private readonly IDbRepository _db;
        private readonly IEmailService _email;

        private readonly ILogger<AttendantService> _logger;

        public AttendantService(IDbRepository db, IEmailService email, ILogger<AttendantService> logger)
        {
            _logger = logger;
            _db = db;
            _email = email;
        }

        private static string CleanRawDigitsAndChars(string input) =>
            string.IsNullOrWhiteSpace(input) ? string.Empty : System.Text.RegularExpressions.Regex.Replace(input, @"[^a-zA-Z0-9]", "").Trim();

        private static string CleanRawPhone(string input) =>
            string.IsNullOrWhiteSpace(input) ? string.Empty : System.Text.RegularExpressions.Regex.Replace(input, @"[^\d]", "").Trim();

        private static string NormalizeName(string input) =>
            string.Join(' ', (input ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Last 10 digits, so 03001234567, +92 300 1234567 and 923001234567 compare equal.</summary>
        private static string PhoneKey(string input)
        {
            var digits = CleanRawPhone(input);
            return digits.Length > 10 ? digits[^10..] : digits;
        }

        /// <summary>
        /// Matches what an app user entered against the attendants added to bookings in Sales.
        /// At least two of name, email, CNIC/passport and phone must match the same person, and one of them
        /// must be CNIC or email (name and phone alone are too easy for a colleague to know).
        /// </summary>
        public async Task<MobileAccessVerifyResult> VerifyMobileAccessAsync(MobileAccessVerifyRequest request)
        {
            const string noMatch = "These details don't match any access user on a booking. Check them with your company admin or reception.";
            var name = NormalizeName(request.Name);
            var email = (request.Email ?? "").Trim().ToLowerInvariant();
            var cnic = CleanRawDigitsAndChars(request.Cnic).ToUpperInvariant();
            var phone = PhoneKey(request.Phone);
            if (new[] { name, email, cnic, phone }.Count(v => v.Length > 0) < 2 || (email.Length == 0 && cnic.Length == 0))
                return new MobileAccessVerifyResult { Message = "Enter at least two of name, email, CNIC and phone, including your CNIC or email." };

            var candidates = await _db.GetActiveAttendantAssignmentCandidatesDbAsync(email, cnic, phone, name);

            bool EmailMatches(IDictionary<string, object?> r) =>
                email.Length > 0 && (r["Email"]?.ToString() ?? "").Trim().ToLowerInvariant() == email;
            bool CnicMatches(IDictionary<string, object?> r) =>
                cnic.Length > 0 && CleanRawDigitsAndChars(r["IdNumber"]?.ToString() ?? "").ToUpperInvariant() == cnic;
            // 0 unless CNIC or email matches; otherwise the number of matching fields.
            int Score(IDictionary<string, object?> r)
            {
                bool strong = EmailMatches(r) || CnicMatches(r);
                if (!strong) return 0;
                return (EmailMatches(r) ? 1 : 0) + (CnicMatches(r) ? 1 : 0)
                     + (name.Length > 0 && NormalizeName(r["Name"]?.ToString() ?? "") == name ? 1 : 0)
                     + (phone.Length > 0 && PhoneKey(r["Phone"]?.ToString() ?? "") == phone ? 1 : 0);
            }

            var people = candidates
                .GroupBy(r => Convert.ToInt32(r["PersonId"]))
                .Select(g => new { Rows = g.ToList(), Score = Score(g.First()) })
                .Where(p => p.Score >= 2)
                .ToList();
            if (people.Count == 0)
                return new MobileAccessVerifyResult { Message = noMatch };

            int best = people.Max(p => p.Score);
            var top = people.Where(p => p.Score == best).ToList();
            if (top.Count > 1)
                return new MobileAccessVerifyResult { Message = "These details match more than one person. Enter all four: name, email, CNIC and phone." };

            var rows = top[0].Rows;
            var spaces = rows
                .GroupBy(r => Convert.ToInt32(r["BookingDetailId"]))
                .Select(g => new MobileAccessSpaceDto
                {
                    BookingDetailId = g.Key,
                    SpaceName = g.First()["SpaceName"]?.ToString() ?? "",
                    IsEnabled = g.Any(r => Convert.ToBoolean(r["IsEnabled"]))
                })
                .ToList();
            bool canOpen = spaces.Any(sp => sp.IsEnabled);

            return new MobileAccessVerifyResult
            {
                Matched = true,
                CanOpenDoor = canOpen,
                Message = canOpen ? "Access verified." : "Your access is currently disabled. Contact your company admin or reception.",
                PersonGuid = rows[0]["PersonGuid"] is Guid g ? g : (Guid.TryParse(rows[0]["PersonGuid"]?.ToString(), out var pg) ? pg : null),
                Name = rows[0]["Name"]?.ToString(),
                Spaces = spaces,
                PersonId = Convert.ToInt32(rows[0]["PersonId"]),
                PersonEmail = (rows[0]["Email"]?.ToString() ?? "").Trim()
            };
        }

        public async Task<(int PersonId, Guid PersonGuid)> AddAttendantAsync(CreateAttendantRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name is required.");
            if (string.IsNullOrWhiteSpace(request.Email)) throw new ArgumentException("Email is required.");
            if (string.IsNullOrWhiteSpace(request.Phone)) throw new ArgumentException("Phone is required.");
            if (string.IsNullOrWhiteSpace(request.IdNumber)) throw new ArgumentException("CNIC/Passport IdNumber is required.");

            request.IdType = request.IdType?.ToUpperInvariant() == "PASSPORT" ? "Passport" : "CNIC";

            var cleanName = request.Name.Trim();
            var cleanEmail = request.Email.Trim().ToLowerInvariant();
            var cleanPhone = CleanRawPhone(request.Phone);
            var cleanIdNumber = CleanRawDigitsAndChars(request.IdNumber);

            return await _db.AddAttendantSpAsync(
                cleanName,
                cleanEmail,
                cleanPhone,
                request.IdType,
                cleanIdNumber,
                request.CustomerId
            );
        }

        public async Task UpdateAttendantAsync(int personId, UpdateAttendantRequest request)
        {
            if (personId <= 0) throw new ArgumentException("Invalid PersonId.");
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name is required.");
            if (string.IsNullOrWhiteSpace(request.Email)) throw new ArgumentException("Email is required.");
            if (string.IsNullOrWhiteSpace(request.Phone)) throw new ArgumentException("Phone is required.");

            var cleanName = request.Name.Trim();
            var cleanEmail = request.Email.Trim().ToLowerInvariant();
            var cleanPhone = CleanRawPhone(request.Phone);

            await _db.UpdatePersonAsync(personId, cleanName, cleanEmail, cleanPhone);
        }

        public async Task<IEnumerable<IDictionary<string, object?>>> GetCustomerAttendantsAsync(int customerId)
        {
            return await _db.GetCustomerAttendantsDbAsync(customerId);
        }

        public async Task<IEnumerable<BookingAttendantDetailDto>> GetBookingAttendantsAsync(int bookingDetailId)
        {
            var rows = await _db.GetBookingAttendantsDbAsync(bookingDetailId);
            var list = new List<BookingAttendantDetailDto>();

            foreach (var r in rows)
            {
                list.Add(new BookingAttendantDetailDto
                {
                    Id = r.ContainsKey("Id") && r["Id"] != null ? Convert.ToInt32(r["Id"]) : 0,
                    BookingDetailId = r.ContainsKey("BookingDetailId") && r["BookingDetailId"] != null ? Convert.ToInt32(r["BookingDetailId"]) : 0,
                    PersonId = r.ContainsKey("PersonId") && r["PersonId"] != null ? Convert.ToInt32(r["PersonId"]) : 0,
                    PersonGuid = r.ContainsKey("PersonGuid") && r["PersonGuid"] is Guid g ? g : Guid.Empty,
                    CustomerId = r.ContainsKey("CustomerId") && r["CustomerId"] != null ? Convert.ToInt32(r["CustomerId"]) : 0,
                    Name = r.ContainsKey("Name") ? Convert.ToString(r["Name"]) ?? "" : "",
                    Email = r.ContainsKey("Email") ? Convert.ToString(r["Email"]) ?? "" : "",
                    Phone = r.ContainsKey("Phone") ? Convert.ToString(r["Phone"]) ?? "" : "",
                    IdType = r.ContainsKey("IdType") ? Convert.ToString(r["IdType"]) ?? "" : "",
                    IdNumber = r.ContainsKey("IdNumber") ? Convert.ToString(r["IdNumber"]) ?? "" : "",
                    AssignedFrom = r.ContainsKey("AssignedFrom") && r["AssignedFrom"] != null ? Convert.ToDateTime(r["AssignedFrom"]) : DateTime.MinValue,
                    AssignedTo = r.ContainsKey("AssignedTo") && r["AssignedTo"] != null ? Convert.ToDateTime(r["AssignedTo"]) : null,
                    IsEnabled = r.ContainsKey("IsEnabled") && r["IsEnabled"] != null ? Convert.ToBoolean(r["IsEnabled"]) : true,
                    IsOverCapacity = r.ContainsKey("IsOverCapacity") && r["IsOverCapacity"] != null ? Convert.ToBoolean(r["IsOverCapacity"]) : false,
                    ExcessSeatCount = r.ContainsKey("ExcessSeatCount") && r["ExcessSeatCount"] != null ? Convert.ToInt32(r["ExcessSeatCount"]) : 0,
                    SurchargeApplied = r.ContainsKey("SurchargeApplied") && r["SurchargeApplied"] != null ? Convert.ToDecimal(r["SurchargeApplied"]) : null,
                    MachineId = r.ContainsKey("MachineId") ? Convert.ToString(r["MachineId"]) : null,
                    HikPendingOps = r.ContainsKey("HikPendingOps") && r["HikPendingOps"] != null ? Convert.ToInt32(r["HikPendingOps"]) : 0
                });
            }

            return list;
        }

        public async Task<AttendantCapacityCheckDto> CheckCapacityBeforeAssignAsync(int bookingDetailId)
        {
            var summary = await _db.GetBookingDetailSummaryDbAsync(bookingDetailId);
            if (summary == null)
            {
                throw new KeyNotFoundException($"Booking detail #{bookingDetailId} not found.");
            }

            var spaceName = summary.ContainsKey("SpaceName") ? Convert.ToString(summary["SpaceName"]) ?? "Space" : "Space";
            var spaceCategory = summary.ContainsKey("SpaceCategory") ? Convert.ToString(summary["SpaceCategory"]) ?? "" : "";
            var capacity = summary.ContainsKey("Capacity") && summary["Capacity"] != null ? Convert.ToInt32(summary["Capacity"]) : 1;
            if (capacity <= 0) capacity = 1;

            var currentActive = summary.ContainsKey("CurrentActiveAttendants") && summary["CurrentActiveAttendants"] != null ? Convert.ToInt32(summary["CurrentActiveAttendants"]) : 0;
            
            var spacePrice = summary.ContainsKey("SpacePrice") && summary["SpacePrice"] != null ? Convert.ToDecimal(summary["SpacePrice"]) : (decimal?)null;
            
            decimal seatPrice;
            if (spacePrice.HasValue && spacePrice.Value > 0)
            {
                seatPrice = spacePrice.Value;
            }
            else
            {
                var rentAmount = summary.ContainsKey("RentAmount") && summary["RentAmount"] != null ? Convert.ToDecimal(summary["RentAmount"]) : 
                                 summary.ContainsKey("Amount") && summary["Amount"] != null ? Convert.ToDecimal(summary["Amount"]) : 0m;
                seatPrice = capacity > 0 ? (rentAmount / capacity) : rentAmount;
            }

            bool allowsOverCapacity = !(spaceCategory.IndexOf("Meeting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        spaceCategory.IndexOf("Shared", StringComparison.OrdinalIgnoreCase) >= 0);

            bool wouldExceed = (currentActive + 1) > capacity;
            int excess = wouldExceed ? ((currentActive + 1) - capacity) : 0;
            decimal estimatedSurcharge = excess * 0.5m * seatPrice;
            decimal subTotal = Math.Round(estimatedSurcharge, 2);
            decimal supportRate = 10.00m;
            decimal supportChargeAmount = Math.Round(subTotal * (supportRate / 100.0m), 2);
            decimal taxRate = 16.00m;
            decimal taxAmount = Math.Round(supportChargeAmount * (taxRate / 100.0m), 2);
            decimal grandTotal = subTotal + taxAmount;

            return new AttendantCapacityCheckDto
            {
                BookingDetailId = bookingDetailId,
                SpaceName = spaceName,
                SpaceCategory = spaceCategory,
                RoomCapacity = capacity,
                CurrentActiveAttendants = currentActive,
                WouldExceedCapacity = wouldExceed,
                AllowsOverCapacity = allowsOverCapacity,
                ExcessSeatCount = excess,
                SeatPrice = Math.Round(seatPrice, 2),
                EstimatedSurcharge = subTotal,
                SubTotal = subTotal,
                SupportChargePercentage = supportRate,
                SupportChargeAmount = supportChargeAmount,
                TaxPercentage = taxRate,
                TaxAmount = taxAmount,
                GrandTotal = grandTotal
            };
        }

        public async Task<IDictionary<string, object?>> AssignAttendantToBookingAsync(AssignBookingAttendantRequest request)
        {
            var capCheck = await CheckCapacityBeforeAssignAsync(request.BookingDetailId);
            if (capCheck.WouldExceedCapacity && !capCheck.AllowsOverCapacity)
            {
                throw new InvalidOperationException($"Over-capacity assignment is strictly not allowed for {capCheck.SpaceCategory} ({capCheck.SpaceName}). Capacity limit is {capCheck.RoomCapacity}.");
            }

            var result = await _db.AssignAttendantToBookingSpAsync(
                request.BookingDetailId,
                request.PersonId,
                request.CustomerId,
                request.AssignedFrom
            );

            // Automatically create Custom Invoice & send Email if assignment is over-capacity
            if (result != null && result.ContainsKey("IsOverCapacity") && Convert.ToBoolean(result["IsOverCapacity"]))
            {
                var surcharge = result.ContainsKey("SurchargeApplied") && result["SurchargeApplied"] != null ? Convert.ToDecimal(result["SurchargeApplied"]) : 0m;
                var excessSeats = result.ContainsKey("ExcessSeatCount") && result["ExcessSeatCount"] != null ? Convert.ToInt32(result["ExcessSeatCount"]) : 1;

                if (surcharge > 0)
                {
                    try
                    {
                        var invResult = await _db.CreateSurchargeInvoiceSpAsync(request.BookingDetailId, request.PersonId, request.CustomerId, surcharge, excessSeats);
                        
                        if (invResult != null && invResult.ContainsKey("TargetEmail"))
                        {
                            var targetEmail = Convert.ToString(invResult["TargetEmail"]) ?? "";
                            var customerName = Convert.ToString(invResult["CustomerName"]) ?? "Valued Customer";
                            var invoiceNum = Convert.ToString(invResult["InvoiceNumber"]) ?? "INV-SUR-000";
                            var attName = Convert.ToString(invResult["AttendantName"]) ?? "";
                            var attIdNum = Convert.ToString(invResult["AttendantIdNumber"]) ?? "";
                            var spaceName = Convert.ToString(invResult["SpaceName"]) ?? "Workspace";
                            var customerAddress = invResult.ContainsKey("CustomerAddress") ? Convert.ToString(invResult["CustomerAddress"]) : "";
                            var subTotalVal = invResult.ContainsKey("SubTotal") && invResult["SubTotal"] != null ? Convert.ToDecimal(invResult["SubTotal"]) : surcharge;
                            var taxRateVal = invResult.ContainsKey("TaxRate") && invResult["TaxRate"] != null ? Convert.ToDecimal(invResult["TaxRate"]) : 16.00m;
                            decimal fallbackSupport = excessSeats > 0 ? (2000.00m * excessSeats) : Math.Round(subTotalVal * 0.10m, 2);
                            var taxTotalVal = invResult.ContainsKey("TaxTotal") && invResult["TaxTotal"] != null ? Convert.ToDecimal(invResult["TaxTotal"]) : Math.Round(fallbackSupport * (taxRateVal / 100m), 2);
                            var grandTotalVal = invResult.ContainsKey("GrandTotal") && invResult["GrandTotal"] != null ? Convert.ToDecimal(invResult["GrandTotal"]) : (subTotalVal + taxTotalVal);
                            DateTime? billingStart = invResult.ContainsKey("BillingPeriodStart") && invResult["BillingPeriodStart"] != null ? Convert.ToDateTime(invResult["BillingPeriodStart"]) : (DateTime?)null;
                            DateTime? billingEnd = invResult.ContainsKey("BillingPeriodEnd") && invResult["BillingPeriodEnd"] != null ? Convert.ToDateTime(invResult["BillingPeriodEnd"]) : (DateTime?)null;

                            if (!string.IsNullOrWhiteSpace(targetEmail))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                    await _email.SendAttendantSurchargeInvoiceEmailAsync(
                                        targetEmail,
                                        customerName,
                                        invoiceNum,
                                        attName,
                                        attIdNum,
                                        spaceName,
                                        excessSeats,
                                        subTotalVal,
                                        taxTotalVal,
                                        grandTotalVal,
                                        customerAddress ?? "",
                                        billingStart,
                                        billingEnd
                                    );
                                    }
                                    catch (Exception ex)
                                    {
                                        // The invoice exists; only the email failed. It can be re-sent from Invoices.
                                        _logger.LogError(ex, "Surcharge invoice {InvoiceNumber} was created but its email to {Email} failed", invoiceNum, targetEmail);
                                    }
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // The attendant stays assigned, but the surcharge invoice is missing: log it and tell the screen.
                        _logger.LogError(ex, "Surcharge invoice failed for booking detail {BookingDetailId}, person {PersonId}, amount {Surcharge}",
                            request.BookingDetailId, request.PersonId, surcharge);
                        if (result != null)
                            result["SurchargeInvoiceError"] = "The attendant was added, but the surcharge invoice could not be created. Please create it from Invoices.";
                    }
                }
            }

            return result ?? new Dictionary<string, object?>();
        }

        public async Task SoftRemoveAttendantFromBookingAsync(int bookingDetailId, int personId)
        {
            await _db.SoftRemoveAttendantFromBookingDbAsync(bookingDetailId, personId);
        }

        public async Task<int> ToggleAccessStatusAsync(ToggleAccessStatusRequest request)
        {
            return await _db.ToggleAccessStatusSpAsync(
                request.BookingDetailId,
                request.CustomerId,
                request.PersonId,
                request.IsEnabled
            );
        }

        public async Task<IEnumerable<AccessStatusExportDto>> GetAccessStatusExportAsync()
        {
            var rows = await _db.GetAccessStatusExportDbAsync();
            return rows.Select(r => new AccessStatusExportDto
            {
                PersonGuid = r.ContainsKey("PersonGuid") && r["PersonGuid"] is Guid g ? g : Guid.Empty,
                Name = r.ContainsKey("Name") ? Convert.ToString(r["Name"]) ?? "" : "",
                Phone = r.ContainsKey("Phone") ? Convert.ToString(r["Phone"]) ?? "" : "",
                Email = r.ContainsKey("Email") ? Convert.ToString(r["Email"]) ?? "" : "",
                IsEnabled = r.ContainsKey("IsEnabled") && r["IsEnabled"] != null && Convert.ToBoolean(r["IsEnabled"]),
                BookingDetailId = r.ContainsKey("BookingDetailId") && r["BookingDetailId"] != null ? Convert.ToInt32(r["BookingDetailId"]) : 0,
                CustomerId = r.ContainsKey("CustomerId") && r["CustomerId"] != null ? Convert.ToInt32(r["CustomerId"]) : 0
            });
        }

        public async Task<IEnumerable<CustomerActiveSpaceDto>> GetCustomerActiveSpacesAsync(int customerId)
        {
            var rows = await _db.GetCustomerActiveSpacesDbAsync(customerId);
            return rows.Select(r => new CustomerActiveSpaceDto
            {
                BookingDetailId = r.ContainsKey("BookingDetailId") && r["BookingDetailId"] != null ? Convert.ToInt32(r["BookingDetailId"]) : 0,
                BookingGuid = r.ContainsKey("BookingGuid") ? Convert.ToString(r["BookingGuid"]) ?? "" : "",
                SpaceName = r.ContainsKey("SpaceName") ? Convert.ToString(r["SpaceName"]) ?? "" : "",
                SpaceCode = r.ContainsKey("SpaceCode") ? Convert.ToString(r["SpaceCode"]) ?? "" : "",
                SpaceCategory = r.ContainsKey("SpaceCategory") ? Convert.ToString(r["SpaceCategory"]) ?? "" : "",
                StartDateTime = r.ContainsKey("StartDateTime") && r["StartDateTime"] != null ? Convert.ToDateTime(r["StartDateTime"]) : null,
                EndDateTime = r.ContainsKey("EndDateTime") && r["EndDateTime"] != null ? Convert.ToDateTime(r["EndDateTime"]) : null,
                Capacity = r.ContainsKey("Capacity") && r["Capacity"] != null ? Convert.ToInt32(r["Capacity"]) : 1,
                ActiveAttendantsCount = r.ContainsKey("ActiveAttendantsCount") && r["ActiveAttendantsCount"] != null ? Convert.ToInt32(r["ActiveAttendantsCount"]) : 0
            });
        }
    }
}

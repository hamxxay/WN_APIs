using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkNest.API.Extensions;
using WorkNest.API.Filters;
using WorkNest.Application.DTOs.Offers;
using WorkNest.Application.Interfaces;
using WorkNest.Application.Services;
using WorkNest.Common.Responses;

namespace WorkNest.API.Controllers
{
    /// <summary>
    /// Offers (CMS Administration), super admin only: list, create, edit and deactivate (script OFFER_01_offers; before it every
    /// call answers 503 "Offers are not set up on this server yet."). Offers are never deleted. The rules that need the table
    /// (overlap, running offers locked, one transaction with a lock) are in the procedures; the checks here only give plain
    /// errors early and never let a bad value reach the database.
    /// </summary>
    [ApiController]
    [Route("api/admin/offers")]
    [Authorize(Roles = ServiceSwitchController.SuperAdminRoles)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class OffersController : ControllerBase
    {
        private static readonly string[] DateFormats =
        {
            "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
        };

        private readonly IOffersRepository _offers;

        public OffersController(IOffersRepository offers) => _offers = offers;

        /// <summary>Every offer, newest start first: offerId, name, discountType, discountValue, startDate, endDate, locationId, locationName, applyTo, applyToDeposit, isActive, status, usedCount ...</summary>
        [HttpGet]
        public Task<IActionResult> List(CancellationToken ct = default) => Run(async () =>
            Ok(ApiResponse.Ok(await _offers.GetListAsync(ct))));

        [HttpPost]
        public Task<IActionResult> Create([FromBody] OfferSaveRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            var (command, error) = Validate(request);
            if (command == null) return BadRequest(ApiResponse.Fail(error!));
            var id = await _offers.SaveAsync(null, command, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { offerId = id }, "Offer saved."));
        });

        /// <summary>A running offer accepts only a new name (the procedure refuses everything else); ended / deactivated offers only a name too.</summary>
        [HttpPut("{id:int}")]
        public Task<IActionResult> Update(int id, [FromBody] OfferSaveRequest? request, CancellationToken ct = default) => Run(async () =>
        {
            if (!(await _offers.GetListAsync(ct)).Any(o => o.OfferId == id)) return NotFound(ApiResponse.Fail("Offer not found."));
            var (command, error) = Validate(request);
            if (command == null) return BadRequest(ApiResponse.Fail(error!));
            await _offers.SaveAsync(id, command, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { offerId = id }, "Offer saved."));
        });

        /// <summary>Turns an offer off for good (no re-activation). Quotations, bookings and invoices already made keep their offer.</summary>
        [HttpPatch("{id:int}/deactivate")]
        public Task<IActionResult> Deactivate(int id, CancellationToken ct = default) => Run(async () =>
        {
            if (!(await _offers.GetListAsync(ct)).Any(o => o.OfferId == id)) return NotFound(ApiResponse.Fail("Offer not found."));
            await _offers.DeactivateAsync(id, User.GetEmail(), ct);
            return Ok(ApiResponse.Ok(new { offerId = id }, "Offer deactivated."));
        });

        /// <summary>Plain-English checks; the values returned are what the procedure receives. Null command = the message.</summary>
        public static (OfferSaveCommand? Command, string? Error) Validate(OfferSaveRequest? r)
        {
            if (r == null) return (null, "Request body is required.");
            var name = r.Name?.Trim() ?? "";
            if (name.Length == 0) return (null, "Enter a name for the offer.");
            if (name.Length > 100) return (null, "The offer name can be at most 100 characters.");

            string type = (r.DiscountType ?? "").Trim().ToUpperInvariant();
            if (type != OfferPricing.Percent && type != OfferPricing.Fixed) return (null, "Choose a discount type: Percent or Fixed amount.");
            if (r.DiscountValue <= 0m) return (null, "The discount must be more than 0.");
            if (type == OfferPricing.Percent && r.DiscountValue > 100m) return (null, "A percent discount cannot be more than 100.");
            if (decimal.Round(r.DiscountValue, 2) != r.DiscountValue) return (null, "The discount can have at most 2 decimals.");

            if (!TryParseLocal(r.StartDate, out var start)) return (null, "Enter the start date and time (local Pakistan time, like 2026-11-10T09:00).");
            if (!TryParseLocal(r.EndDate, out var end)) return (null, "Enter the end date and time (local Pakistan time, like 2026-11-30T23:59).");
            if (end <= start) return (null, "The end must be after the start.");

            string applyTo = (r.ApplyTo ?? OfferPricing.FirstPeriod).Trim().ToUpperInvariant();
            if (applyTo != OfferPricing.FirstPeriod && applyTo != OfferPricing.WholeTerm) return (null, "Choose where the offer applies: First billing period or Whole booking term.");
            if (r.LocationId is <= 0) return (null, "Choose a location or leave it empty for all locations.");

            return (new OfferSaveCommand(name, type, r.DiscountValue, start, end, r.LocationId, applyTo, r.ApplyToDeposit, r.IsActive), null);
        }

        /// <summary>"yyyy-MM-ddTHH:mm" (no Z, no offset): kept exactly as sent (Pakistan wall-clock).</summary>
        public static bool TryParseLocal(string? text, out DateTime value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
            value = DateTime.SpecifyKind(d, DateTimeKind.Unspecified);
            return true;
        }

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (OffersMissingException) { return StatusCode(503, ApiResponse.Fail(OffersMissingException.UserMessage)); }
            catch (OfferRuleException ex) { return BadRequest(ApiResponse.Fail(ex.Message)); }
        }
    }

    /// <summary>
    /// GET api/offers/running?locationId= : the offer running now for that location, for the "Offer applied" panel of Create Quotation /
    /// Create Booking. Display only: the server looks the offer up again at submit and ignores any offer values the client sends.
    /// </summary>
    [ApiController]
    [Route("api/offers")]
    [Authorize(Roles = RunningRoles)]
    [ValidateLocationScope]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class RunningOffersController : ControllerBase
    {
        public const string RunningRoles =
            "super_admin,SuperAdmin,admin,Admin,sales_executive,SalesExecutive,sales_manager,SalesManager,receptionist,Receptionist";

        private readonly IOffersRepository _offers;
        private readonly IBusinessClock _clock;

        public RunningOffersController(IOffersRepository offers, IBusinessClock clock) { _offers = offers; _clock = clock; }

        /// <summary>{ data: null } when no offer runs (or Offers are not set up on this server yet).</summary>
        [HttpGet("running")]
        public async Task<IActionResult> Running([FromQuery] int? locationId, CancellationToken ct = default)
        {
            var offer = await _offers.GetRunningAsync(locationId, _clock.Now, ct);
            return Ok(ApiResponse.Ok(offer == null ? null : new
            {
                offerId = offer.OfferId, name = offer.Name, discountType = offer.DiscountType, discountValue = offer.DiscountValue,
                startDate = offer.StartDate, endDate = offer.EndDate, locationId = offer.LocationId, locationName = offer.LocationName,
                applyTo = offer.ApplyTo, applyToDeposit = offer.ApplyToDeposit
            }));
        }
    }
}

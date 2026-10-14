namespace WorkNest.Application.DTOs.Dashboard
{
    // Sales Manager dashboard (api/sales-manager/*): read-only, the manager's locations (super admins: all, with a
    // location filter). Shapes follow the shared Sales Manager contract used by the CMS and the mobile app; serialized
    // camelCase. No money at all: figures are counts and desks (whole numbers), months "yyyy-MM", dates "yyyy-MM-dd".
    // Desks of a booking / quotation = its Capacity, else the Capacity of its space; meeting rooms count 0 desks.
    // Level 2 (breakdown) reuses the CEO dashboard shape (CeoBreakdownDto); level 3 (records) the CEO records envelope
    // (CeoRecordsDto) with the Sales Manager rows below (desks instead of amounts).

    /// <summary>GET api/sales-manager/dashboard</summary>
    public class SalesManagerDashboardDto
    {
        /// <summary>month | quarter | year</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        /// <summary>The manager's locations (for the location picker; super admins: all).</summary>
        public List<CeoLocationDto> Locations { get; set; } = new();
        public SalesManagerFunnelDto Funnel { get; set; } = new();
        public SalesManagerTeamDto Team { get; set; } = new();
        /// <summary>All tour inquiries (they have no location).</summary>
        public SalesManagerInquiriesDto Inquiries { get; set; } = new();
        public SalesManagerRenewalsDto Renewals { get; set; } = new();
    }

    public class SalesManagerFunnelDto
    {
        /// <summary>inquiries, quotations, accepted, bookings: created in the period.</summary>
        public List<CeoFunnelStageDto> Stages { get; set; } = new();
        /// <summary>Bookings / quotations, percent (one decimal).</summary>
        public decimal ConversionRate { get; set; }
        /// <summary>The same for the previous period (same number of days).</summary>
        public decimal ConversionRatePrev { get; set; }
        /// <summary>Desks on the quotations created in the period.</summary>
        public int QuotedDesks { get; set; }
        /// <summary>Desks on the bookings created in the period.</summary>
        public int BookedDesks { get; set; }
        /// <summary>The same for the previous period.</summary>
        public int BookedDesksPrev { get; set; }
        /// <summary>Last 12 months, oldest first.</summary>
        public List<SalesManagerFunnelMonthDto> Months { get; set; } = new();
    }

    public class SalesManagerFunnelMonthDto
    {
        public string Month { get; set; } = "";
        public int Quotations { get; set; }
        public int Accepted { get; set; }
        public int Bookings { get; set; }
        /// <summary>Desks on the bookings created in the month.</summary>
        public int BookedDesks { get; set; }
    }

    public class SalesManagerTeamDto
    {
        /// <summary>Admins and sales executives of the locations, ranked by desks sold, converted, quotations, name.</summary>
        public List<SalesManagerPersonDto> People { get; set; } = new();
        public List<SalesManagerTeamLocationDto> PerLocation { get; set; } = new();
    }

    public class SalesManagerPersonDto
    {
        public int UserId { get; set; }
        /// <summary>WN_Users.Name.</summary>
        public string Name { get; set; } = "";
        /// <summary>WN_Roles.DisplayName of the person's role (e.g. "Sales Executive", "Admin").</summary>
        public string Role { get; set; } = "";
        public string? Locations { get; set; }
        /// <summary>Tour inquiries the person gave feedback on in the period.</summary>
        public int InquiriesHandled { get; set; }
        public int Quotations { get; set; }
        public int Accepted { get; set; }
        public int Converted { get; set; }
        /// <summary>Desks on the person's Converted quotations created in the period.</summary>
        public int DesksSold { get; set; }
        /// <summary>Converted / quotations, percent (one decimal).</summary>
        public decimal ConversionRate { get; set; }
        /// <summary>Right now: their future prospects whose follow-up date has come (inquiry still open).</summary>
        public int FollowUpsDue { get; set; }
    }

    public class SalesManagerTeamLocationDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int SalesExecutives { get; set; }
        public int Quotations { get; set; }
        public int Converted { get; set; }
        /// <summary>Desks on the Converted quotations created in the period on the location's spaces.</summary>
        public int DesksSold { get; set; }
    }

    public class SalesManagerInquiriesDto
    {
        /// <summary>Created in the period.</summary>
        public int Total { get; set; }
        public int TotalPrev { get; set; }
        /// <summary>Of the period's inquiries, latest feedback: future prospect (follow-up planned).</summary>
        public int FollowedUp { get; set; }
        /// <summary>Of the period's inquiries, latest feedback: not interested.</summary>
        public int NotInterested { get; set; }
        /// <summary>Of the period's inquiries, latest feedback: converted into a quotation.</summary>
        public int Converted { get; set; }
        /// <summary>Right now (any period): future prospects whose follow-up date has come, inquiry still open.</summary>
        public int FollowUpsDue { get; set; }
        /// <summary>Website, WhatsApp, WalkIn, Reference, NotSet (always all five).</summary>
        public List<CeoInquiryTypeCountDto> ByType { get; set; } = new();
        /// <summary>Top 6 reasons of the period's not-interested inquiries (case and spaces ignored).</summary>
        public List<SalesManagerReasonDto> NotInterestedReasons { get; set; } = new();
        /// <summary>Last 12 months, oldest first.</summary>
        public List<SalesManagerInquiryMonthDto> Months { get; set; } = new();
    }

    public class SalesManagerReasonDto
    {
        public string Reason { get; set; } = "";
        public int Count { get; set; }
    }

    public class SalesManagerInquiryMonthDto
    {
        public string Month { get; set; } = "";
        public int Website { get; set; }
        public int WhatsApp { get; set; }
        public int WalkIn { get; set; }
        public int Reference { get; set; }
        public int NotSet { get; set; }
    }

    public class SalesManagerRenewalsDto
    {
        /// <summary>Confirmed leases ending in the next 90 days, soonest first (first 50).</summary>
        public List<SalesManagerLeaseDto> Expiring { get; set; } = new();
        /// <summary>Confirmed leases ending in the next 90 days (all of them).</summary>
        public int ExpiringCount { get; set; }
        /// <summary>Desks on the leases ending in the next 90 days.</summary>
        public int ExpiringDesks { get; set; }
        /// <summary>Confirmed leases ending in the next 30 days.</summary>
        public int Expiring30Count { get; set; }
        /// <summary>Desks on the leases ending in the next 30 days.</summary>
        public int Expiring30Desks { get; set; }
        /// <summary>Desks on the confirmed leases that ended in the period (end date in the period, before now).</summary>
        public int EndedDesks { get; set; }
        /// <summary>funnel.bookedDesks - endedDesks.</summary>
        public int NetDesks { get; set; }
        /// <summary>The same for the previous period (desks booked - desks ended).</summary>
        public int NetDesksPrev { get; set; }
        /// <summary>Last 12 months and the next 3, oldest first.</summary>
        public List<SalesManagerRenewalMonthDto> Months { get; set; } = new();
    }

    /// <summary>A confirmed lease (dashboard expiring list and records/leases rows).</summary>
    public class SalesManagerLeaseDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public string? EndOn { get; set; }
        /// <summary>Desks of the lease (0 for a meeting room).</summary>
        public int Desks { get; set; }
        /// <summary>Days from today to the end date (negative = already ended).</summary>
        public int DaysLeft { get; set; }
    }

    public class SalesManagerRenewalMonthDto
    {
        public string Month { get; set; } = "";
        /// <summary>Desks on the bookings created in the month (0 for future months).</summary>
        public int BookedDesks { get; set; }
        /// <summary>Desks on the confirmed leases ending in the month.</summary>
        public int EndingDesks { get; set; }
        /// <summary>Confirmed leases ending in the month.</summary>
        public int Expiring { get; set; }
    }

    /// <summary>records/quotations rows (no money: the desks quoted).</summary>
    public class SalesManagerQuotationRecordDto
    {
        public int QuotationId { get; set; }
        public string? QuotationNumber { get; set; }
        public string? Customer { get; set; }
        public string? Location { get; set; }
        public string? CreatedBy { get; set; }
        public string? Status { get; set; }
        /// <summary>Desks of the quotation (0 for a meeting room).</summary>
        public int Desks { get; set; }
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string? CreatedOn { get; set; }
    }

    /// <summary>records/bookings rows (no money: the desks booked).</summary>
    public class SalesManagerBookingRecordDto
    {
        public int BookingId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public string? Location { get; set; }
        public string? StartOn { get; set; }
        public string? EndOn { get; set; }
        public string? Status { get; set; }
        /// <summary>Desks of the booking (0 for a meeting room).</summary>
        public int Desks { get; set; }
    }

    /// <summary>records/inquiries rows: the CEO inquiry row plus the follow-up date.</summary>
    public class SalesManagerInquiryRecordDto : CeoInquiryRecordDto
    {
        /// <summary>Latest feedback's follow-up date (future prospects), yyyy-MM-dd.</summary>
        public string? FollowUpOn { get; set; }
    }

    /// <summary>Query of GET api/sales-manager/breakdown.</summary>
    public class SalesManagerBreakdownQuery
    {
        public string? Metric { get; set; }
        public string? Period { get; set; }
        public int? LocationId { get; set; }
        public string? Month { get; set; }
        public string? Type { get; set; }
        public int? UserId { get; set; }
    }

    /// <summary>Query of GET api/sales-manager/records/{kind} (each list uses the parameters it knows).</summary>
    public class SalesManagerRecordsQuery
    {
        public string? Period { get; set; }
        /// <summary>A location id (or "none" for a super admin's breakdown row not tied to a location).</summary>
        public string? LocationId { get; set; }
        public string? Month { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Status { get; set; }
        public string? Type { get; set; }
        public int? UserId { get; set; }
        public string? Stage { get; set; }
        /// <summary>records/inquiries: followup_due | not_interested | converted | open (also followed_up).</summary>
        public string? Outcome { get; set; }
        /// <summary>records/inquiries: one not-interested reason (case and spaces ignored).</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// What the caller may see: <see cref="MyLocations"/> = the manager's locations (null = all, super admin);
    /// <see cref="LocationId"/> = the one chosen (already checked to be one of them); <see cref="NoLocation"/> = only
    /// records not tied to a location (super admins with all locations only).
    /// </summary>
    public sealed record SalesManagerScope(IReadOnlyList<int>? MyLocations, int? LocationId, bool NoLocation = false)
    {
        /// <summary>The locations to cover, CSV; null = all. A manager without locations gets "0" (matches nothing).</summary>
        public string? LocationIdsCsv =>
            LocationId is > 0 ? LocationId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : PickerCsv;

        /// <summary>The location picker list, CSV; null = all. A manager without locations gets "0" (matches nothing).</summary>
        public string? PickerCsv
        {
            get
            {
                if (MyLocations == null) return null;
                var ids = MyLocations.Where(x => x > 0).Distinct().ToList();
                return ids.Count == 0 ? "0" : string.Join(",", ids);
            }
        }
    }
}

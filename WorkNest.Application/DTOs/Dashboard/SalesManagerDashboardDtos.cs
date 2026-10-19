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
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026", "1 Oct - 15 Oct 2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        /// <summary>
        /// False: the tour inquiry figures (funnel stage, inquiries section, team inquiries handled / follow-ups due, the
        /// inquiries-type breakdown and records/inquiries) follow the locations like the rest of the dashboard: the chosen
        /// location's inquiries, or with no location chosen the locations' inquiries plus those without a branch (super
        /// admins: every inquiry). True: the database update BR_02 has not run yet, every branch's inquiries count.
        /// </summary>
        public bool InquiriesAllBranches { get; set; }
        /// <summary>The manager's locations (for the location picker; super admins: all).</summary>
        public List<CeoLocationDto> Locations { get; set; } = new();
        public SalesManagerFunnelDto Funnel { get; set; } = new();
        public SalesManagerTeamDto Team { get; set; } = new();
        /// <summary>Tour inquiries of the locations (by branch, see <see cref="InquiriesAllBranches"/>).</summary>
        public SalesManagerInquiriesDto Inquiries { get; set; } = new();
        public SalesManagerRenewalsDto Renewals { get; set; } = new();
        /// <summary>Custom period (CS_01): the first and last day of the range, yyyy-MM-dd; null for month / quarter / year.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>Custom period: the previous range the *Prev figures cover (as many days, just before From); else null.</summary>
        public string? PreviousFrom { get; set; }
        public string? PreviousTo { get; set; }
    }

    public class SalesManagerFunnelDto
    {
        /// <summary>inquiries, quotations, accepted, bookings: created in the period.</summary>
        public List<CeoFunnelStageDto> Stages { get; set; } = new();
        /// <summary>Bookings / quotations, percent (one decimal).</summary>
        /// <summary>Quotations created in the period that were booked (converted); conversion = this / quotations.</summary>
        public int QuotationsBooked { get; set; }
        /// <summary>The same for the previous period.</summary>
        public int QuotationsBookedPrev { get; set; }
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
        /// <summary>Quotations created in the month that were booked (converted).</summary>
        public int QuotationsBooked { get; set; }
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
        /// <summary>period = custom (CS_01): the dashboard's range, yyyy-MM-dd (from / to narrow a list on their own).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
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
        /// <summary>period = custom (CS_01): the dashboard's range, yyyy-MM-dd (from / to narrow a list on their own).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
    }

    // ---- Complaints section (SM_03): GET api/sales-manager/complaints and api/sales-manager/records/complaints ----
    // dbo.WN_Complaints has no location: a complaint belongs to the location whose name is its Branch (case and spaces
    // ignored). Complaints without a branch (WhatsApp) count under "All my locations" only (and for super admins);
    // with a location chosen only that branch's complaints count. Status: open (new), in_progress, resolved (fixed,
    // waiting for the customer's reply), closed. Times are business wall-clock (Pakistan) times, without an offset.

    /// <summary>GET api/sales-manager/complaints</summary>
    public class SalesManagerComplaintsDto
    {
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026", "1 Oct - 15 Oct 2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>Right now: every complaint in scope by status (not bound to the period).</summary>
        public SalesManagerComplaintStatusDto Status { get; set; } = new();
        /// <summary>status.open + status.inProgress.</summary>
        public int ActiveTotal { get; set; }
        /// <summary>Complaints created in the period.</summary>
        public int NewInPeriod { get; set; }
        /// <summary>The same for the previous period (same number of days, as the rest of the dashboard).</summary>
        public int NewInPeriodPrev { get; set; }
        /// <summary>Resolved or closed complaints whose last update (UpdatedOn) is in the period.</summary>
        public int ResolvedInPeriod { get; set; }
        /// <summary>Follow-up complaints (the customer said the problem persists) created in the period.</summary>
        public int ReopenedInPeriod { get; set; }
        /// <summary>Average days from created to resolved of the resolvedInPeriod complaints, one decimal; null when none.</summary>
        public decimal? AvgResolutionDays { get; set; }
        /// <summary>Categories with complaints (no category = "Other"), most open + in progress first, then total.</summary>
        public List<SalesManagerComplaintGroupDto> ByCategory { get; set; } = new();
        /// <summary>Branches with complaints ("" = no branch, label "No branch"), most open + in progress first, then total.</summary>
        public List<SalesManagerComplaintBranchDto> ByBranch { get; set; } = new();
        /// <summary>Last 12 months, oldest first.</summary>
        public List<SalesManagerComplaintMonthDto> Months { get; set; } = new();
        /// <summary>The 8 oldest open / in progress complaints, oldest first.</summary>
        public List<SalesManagerComplaintDto> OldestActive { get; set; } = new();
        /// <summary>Custom period (CS_01): the first and last day of the range, yyyy-MM-dd; null for month / quarter / year.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>Custom period: the previous range the *Prev figures cover (as many days, just before From); else null.</summary>
        public string? PreviousFrom { get; set; }
        public string? PreviousTo { get; set; }
    }

    public class SalesManagerComplaintStatusDto
    {
        /// <summary>New, waiting for action.</summary>
        public int Open { get; set; }
        public int InProgress { get; set; }
        /// <summary>Fixed, waiting for the customer's reply.</summary>
        public int Resolved { get; set; }
        public int Closed { get; set; }
    }

    /// <summary>A byCategory row.</summary>
    public class SalesManagerComplaintGroupDto
    {
        public string Category { get; set; } = "";
        /// <summary>Open right now.</summary>
        public int Open { get; set; }
        /// <summary>In progress right now.</summary>
        public int InProgress { get; set; }
        /// <summary>Created in the period.</summary>
        public int Total { get; set; }
    }

    /// <summary>A byBranch row.</summary>
    public class SalesManagerComplaintBranchDto
    {
        /// <summary>The branch (the location name); "" = no branch (records: branch=none).</summary>
        public string Branch { get; set; } = "";
        /// <summary>The branch, or "No branch".</summary>
        public string Label { get; set; } = "";
        public int Open { get; set; }
        public int InProgress { get; set; }
        /// <summary>Created in the period.</summary>
        public int Total { get; set; }
    }

    public class SalesManagerComplaintMonthDto
    {
        public string Month { get; set; } = "";
        /// <summary>Complaints created in the month.</summary>
        public int Created { get; set; }
        /// <summary>Resolved or closed complaints last updated in the month.</summary>
        public int Resolved { get; set; }
    }

    /// <summary>An oldestActive row (open / in progress complaint).</summary>
    public class SalesManagerComplaintDto
    {
        public int ComplaintId { get; set; }
        public string? ComplaintNo { get; set; }
        public string? CustomerName { get; set; }
        public string? Category { get; set; }
        /// <summary>The branch (location name); null = no branch.</summary>
        public string? Branch { get; set; }
        /// <summary>open | in_progress</summary>
        public string? Status { get; set; }
        /// <summary>Business (Pakistan) time, yyyy-MM-ddTHH:mm:ss without an offset.</summary>
        public string? CreatedOn { get; set; }
        /// <summary>Days since the complaint came in.</summary>
        public int AgeDays { get; set; }
    }

    /// <summary>records/complaints rows (no money).</summary>
    public class SalesManagerComplaintRecordDto
    {
        public int ComplaintId { get; set; }
        public string? ComplaintNo { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string? Category { get; set; }
        /// <summary>The branch (location name); null = no branch.</summary>
        public string? Branch { get; set; }
        /// <summary>open | in_progress | resolved | closed</summary>
        public string? Status { get; set; }
        /// <summary>whatsapp | manual (entered by staff)</summary>
        public string? Source { get; set; }
        /// <summary>Reopened: a follow-up of a complaint the customer said was not fixed.</summary>
        public bool IsFollowUp { get; set; }
        /// <summary>Business (Pakistan) time, yyyy-MM-ddTHH:mm:ss without an offset.</summary>
        public string? CreatedOn { get; set; }
        public string? UpdatedOn { get; set; }
        /// <summary>Resolved / closed: days from created to resolved; otherwise days since it came in.</summary>
        public int AgeDays { get; set; }
        /// <summary>The first 200 characters.</summary>
        public string? Description { get; set; }
    }

    /// <summary>Query of GET api/sales-manager/records/complaints.</summary>
    public class SalesManagerComplaintRecordsQuery
    {
        /// <summary>Period of the *_period statuses: month (default) | quarter | year | custom (PeriodFrom / PeriodTo).</summary>
        public string? Period { get; set; }
        /// <summary>One of the caller's location ids (omitted = all of them, with the complaints without a branch).</summary>
        public string? LocationId { get; set; }
        /// <summary>open | in_progress | resolved | closed | active | resolved_period | new_period | reopened_period</summary>
        public string? Status { get; set; }
        /// <summary>One category ("Other" also matches complaints without one).</summary>
        public string? Category { get; set; }
        /// <summary>One branch; "none" = complaints without a branch.</summary>
        public string? Branch { get; set; }
        /// <summary>yyyy-MM: created in that month.</summary>
        public string? Month { get; set; }
        /// <summary>period = custom (CS_01): the dashboard's range, yyyy-MM-dd (from / to narrow a list on their own).</summary>
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
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

        /// <summary>
        /// Tour inquiries / complaints without a branch count too: no single location chosen ("All my locations"; super
        /// admins: all). With a location chosen only that branch's ones count.
        /// </summary>
        public bool IncludeNoBranch => LocationId == null;

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

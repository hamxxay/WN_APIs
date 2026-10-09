namespace WorkNest.Application.DTOs.Dashboard
{
    /// <summary>Role-based dashboard. Only the sections for the caller's role are filled; the rest are null.</summary>
    public class RoleDashboardDto
    {
        /// <summary>sales_executive | admin | super_admin</summary>
        public string Role { get; set; } = "";
        public string Period { get; set; } = "month";
        public SalesDashboardDto? Sales { get; set; }
        public TeamDashboardDto? Team { get; set; }
        public List<LocationComparisonDto>? Locations { get; set; }
        public StaffDashboardDto? Staff { get; set; }
        public SystemHealthDto? System { get; set; }
        public string GeneratedAt { get; set; } = "";
    }

    public class SalesDashboardDto
    {
        // pipeline: quotations created in the period, by where they are now
        public int QuotationsCreated { get; set; }
        public int Draft { get; set; }
        public int Sent { get; set; }
        public int Accepted { get; set; }
        public int AgreementStage { get; set; }
        public int Converted { get; set; }
        public int Lost { get; set; }
        public decimal ConvertedValue { get; set; }
        /// <summary>Converted / created, in percent.</summary>
        public decimal ConversionPct { get; set; }
        public List<DashboardAttentionItemDto> AwaitingSignature { get; set; } = new();
        public List<DashboardAttentionItemDto> PendingBookings { get; set; } = new();

        // tour inquiries
        public int NewInquiries { get; set; }
        public int InquiriesInPeriod { get; set; }
        public int FollowUpsDue { get; set; }
        public int ConvertedInquiries { get; set; }
        public List<FollowUpItemDto> FollowUps { get; set; } = new();

        // renewals
        public int RenewalDays { get; set; }
        public int RenewalsDue { get; set; }
        public List<DashboardAttentionItemDto> Renewals { get; set; } = new();

        // collections
        public decimal Outstanding { get; set; }
        public int OutstandingCount { get; set; }
        public decimal Overdue { get; set; }
        public int OverdueCount { get; set; }
        public List<DashboardAttentionItemDto> OverdueInvoices { get; set; } = new();
    }

    public class FollowUpItemDto
    {
        public int ContactId { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? FollowUpOn { get; set; }
        public string? Note { get; set; }
    }

    public class TeamDashboardDto
    {
        public int OpenComplaints { get; set; }
        public int UnreadWhatsApp { get; set; }
        public int NewInquiries { get; set; }
        public int KycPending { get; set; }
        public int AccessSuspended { get; set; }
        public int MachinesOffline { get; set; }
        public int Machines { get; set; }
        public List<OfflineMachineDto> OfflineMachines { get; set; } = new();
    }

    public class OfflineMachineDto
    {
        public string Name { get; set; } = "";
        public string? LastSeen { get; set; }
    }

    public class LocationComparisonDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int TotalSpaces { get; set; }
        public int OccupiedSpaces { get; set; }
        public decimal OccupancyPct { get; set; }
        public int ActiveBookings { get; set; }
        public decimal Invoiced { get; set; }
        public decimal Collected { get; set; }
        public decimal Outstanding { get; set; }
        public decimal Overdue { get; set; }
    }

    public class StaffDashboardDto
    {
        public List<StaffPerLocationDto> PerLocation { get; set; } = new();
        public List<StaffPerformanceDto> People { get; set; } = new();
    }

    public class StaffPerLocationDto
    {
        public int LocationId { get; set; }
        public string Name { get; set; } = "";
        public int Admins { get; set; }
        public int SalesExecutives { get; set; }
    }

    public class StaffPerformanceDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string? Locations { get; set; }
        public int Quotations { get; set; }
        public int Converted { get; set; }
        public decimal ConvertedValue { get; set; }
    }

    public class SystemHealthDto
    {
        public bool HikSyncEnabled { get; set; }
        public List<SystemJobDto> HikJobs { get; set; } = new();
        public int Machines { get; set; }
        public int MachinesOffline { get; set; }
        public string? LastMachineSeen { get; set; }
        public bool WhatsAppConfigured { get; set; }
        public string? LastWhatsAppMessage { get; set; }
        public int EmailPending { get; set; }
        public int EmailFailed { get; set; }
    }

    public class SystemJobDto
    {
        public string Job { get; set; } = "";
        public string? LastFinishedAt { get; set; }
        public bool? LastOk { get; set; }
        public string? LastResult { get; set; }
        public bool Running { get; set; }
    }
}

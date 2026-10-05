using System.Text.Json.Serialization;

namespace WorkNest.Application.DTOs.HikDevice
{
    /// <summary>
    /// Connection details for one Hikvision terminal (internal only — never returned to clients).
    /// </summary>
    public class HikDeviceConnection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string? Host2 { get; set; }
        public int Port { get; set; } = 80;
        public bool UseHttps { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool Online { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>
    /// Result of a single ISAPI call against a terminal.
    /// </summary>
    public class HikIsapiResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public string? SubStatusCode { get; set; }
        /// <summary>The machine could not be reached (offline / network) — the operation can be queued.</summary>
        public bool Unreachable { get; set; }

        public static HikIsapiResult Success() => new() { Ok = true };
        public static HikIsapiResult Fail(string error, string? subStatusCode = null) => new() { Ok = false, Error = error, SubStatusCode = subStatusCode };
    }

    public class HikCardCaptureResult : HikIsapiResult
    {
        public string? CardNo { get; set; }
    }

    public class HikFingerprintCaptureResult : HikIsapiResult
    {
        public string? FingerData { get; set; }
        public string? Quality { get; set; }
    }

    public class HikFaceCaptureResult : HikIsapiResult
    {
        public byte[]? Jpeg { get; set; }
    }

    /// <summary>
    /// Request body for enrolling a fingerprint / card / face for a booking attendant.
    /// The person is created on the booked room's machine + every Entrance machine, the credential
    /// is captured on the room's machine and then copied to the Entrance machines.
    /// </summary>
    public class HikEnrollRequest
    {
        /// <summary>Fingerprint slot 1–10 (fingerprint only). Machines are chosen by the server:
        /// the booked room's machine (capture) + every Entrance machine.</summary>
        public int FingerNo { get; set; } = 1;

        /// <summary>Card only: a typed card number. When set, the card is attached directly instead of
        /// waiting for a tap on the room machine's reader.</summary>
        public string? CardNo { get; set; }
    }

    public class HikEnrollDeviceResultDto
    {
        public int DeviceId { get; set; }
        public string Device { get; set; } = string.Empty;
        public bool Ok { get; set; }
        /// <summary>Machine was offline: the change is in WN_HIK_PendingOps and applies when it reconnects.</summary>
        public bool Queued { get; set; }
        public string? Error { get; set; }
    }

    public class HikEnrollResultDto
    {
        public bool Ok { get; set; }
        public string Type { get; set; } = string.Empty;
        /// <summary>Machine ID (WN_HIK_PersonMap.MachineID) — the person's number on the machines.</summary>
        public string? MachineId { get; set; }
        public string? Error { get; set; }
        public string? CardNo { get; set; }
        public string? Quality { get; set; }
        public List<HikEnrollDeviceResultDto> Devices { get; set; } = new();
    }

    /// <summary>
    /// Person + booking context used to register an attendant on the terminals.
    /// </summary>
    public class HikAttendantContext
    {
        public int PersonId { get; set; }
        public int BookingDetailId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        /// <summary>Booked space code — matched against WN_HIK_Devices.Code to find the room's machine.</summary>
        public string? SpaceCode { get; set; }
        public string? SpaceName { get; set; }
        /// <summary>WN_AccessStatus.IsEnabled — the Access Status tick in Step 3.</summary>
        public bool IsEnabled { get; set; } = true;
        /// <summary>Machine employee # from WN_HIK_PersonMap (null until first enrollment).</summary>
        public string? EmployeeNo { get; set; }
        /// <summary>Booking access suspended for an overdue challan (no active manual extension).</summary>
        public bool IsSuspended { get; set; }
        /// <summary>What the machines should do for this person: Access Status tick AND not suspended.</summary>
        public bool AllowedOnMachines => IsEnabled && !IsSuspended;
    }

    /// <summary>
    /// Result of pushing an Access Status tick/untick to the machines (block / unblock).
    /// </summary>
    public class HikAccessSyncResultDto
    {
        public bool Enabled { get; set; }
        /// <summary>People that exist on the machines (enrolled at least once).</summary>
        public int People { get; set; }
        /// <summary>People skipped because they were never enrolled on a machine.</summary>
        public int NotEnrolled { get; set; }
        /// <summary>The booking is suspended for an overdue challan, so the machines keep the person blocked.</summary>
        public bool Suspended { get; set; }
        public string? Error { get; set; }
        public List<HikEnrollDeviceResultDto> Devices { get; set; } = new();
    }

    /// <summary>
    /// Challan-based access suspension for the booking behind a booked space (WN_HIK_BookingAccessSuspensions).
    /// </summary>
    public class HikAccessSuspensionDto
    {
        /// <summary>An open suspension exists (an overdue challan is unpaid).</summary>
        public bool Open { get; set; }
        /// <summary>Access is blocked right now (open and no active manual extension).</summary>
        public bool Suspended { get; set; }
        /// <summary>A manual extension is active (access allowed through OverrideUntil).</summary>
        public bool Extended { get; set; }
        public int? SuspensionId { get; set; }
        public int? BookingId { get; set; }
        public string? Reason { get; set; }
        public string? SuspendedAt { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? DueOn { get; set; }
        public decimal? GrandTotal { get; set; }
        public decimal? BalanceDue { get; set; }
        public string? OverrideUntil { get; set; }
        public string? OverrideByEmail { get; set; }
        public string? OverrideReason { get; set; }
    }

    public class HikAccessExtendRequest
    {
        /// <summary>Last day access is allowed (yyyy-MM-dd).</summary>
        public string OverrideUntil { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Admin dashboard "Door access" card: machines, queued machine operations and suspended bookings.</summary>
    public class HikAccessOverviewDto
    {
        public int Devices { get; set; }
        public int DevicesOnline { get; set; }
        public int DevicesOffline { get; set; }
        /// <summary>Operations waiting in WN_HIK_PendingOps for offline machines.</summary>
        public int PendingOps { get; set; }
        public int PendingOpsDevices { get; set; }
        /// <summary>Running bookings blocked right now (open suspension, no active temporary access).</summary>
        public int Suspended { get; set; }
        /// <summary>Running bookings on temporary access (open suspension, OverrideUntil today or later).</summary>
        public int Extended { get; set; }
        /// <summary>Temporary access that ends within EndingSoonDays.</summary>
        public int EndingSoon { get; set; }
        public int EndingSoonDays { get; set; }
        public List<HikSuspendedBookingDto> Items { get; set; } = new();
    }

    public class HikSuspendedBookingDto
    {
        public int SuspensionId { get; set; }
        public int BookingId { get; set; }
        /// <summary>A booked space of the booking — used for the temporary access call.</summary>
        public int BookingDetailId { get; set; }
        public string? Customer { get; set; }
        public string? Space { get; set; }
        public int SpaceCount { get; set; }
        public string? BookingEnd { get; set; }
        public int EnrolledPeople { get; set; }
        public string? Reason { get; set; }
        public string? SuspendedAt { get; set; }
        public bool Extended { get; set; }
        public bool EndingSoon { get; set; }
        public string? OverrideUntil { get; set; }
        public string? OverrideByEmail { get; set; }
        public string? OverrideReason { get; set; }
    }

    /// <summary>One booking whose machine state must change, from WN_HIK_AccessSuspension_Run.</summary>
    public class HikAccessSuspensionChange
    {
        public int SuspensionId { get; set; }
        public int BookingId { get; set; }
        public bool ShouldBlock { get; set; }
    }

    /// <summary>Challans (invoices) of the booking behind a booked space — shown in Step 3.</summary>
    public class HikBookingChallansDto
    {
        public int? BookingId { get; set; }
        public string? ChallanNumber { get; set; }
        public string? ChallanValidUntil { get; set; }
        public decimal TotalBalanceDue { get; set; }
        public int OverdueCount { get; set; }
        public List<HikChallanDto> Challans { get; set; } = new();
    }

    public class HikChallanDto
    {
        public int Id { get; set; }
        public string? InvoiceNumber { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? IssuedOn { get; set; }
        public string? DueOn { get; set; }
        public string? PeriodStart { get; set; }
        public string? PeriodEnd { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal PaidTotal { get; set; }
        public decimal BalanceDue { get; set; }
        /// <summary>Unpaid | Paid | Partial | Overdue</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>Unpaid / Overdue with the due date passed — this is what suspends access.</summary>
        public bool IsOverdue { get; set; }
    }
}

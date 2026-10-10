using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.Attendant
{
    public class CreateAttendantRequest
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string IdType { get; set; } = "CNIC"; // 'CNIC' or 'Passport'
        public string IdNumber { get; set; } = string.Empty;
    }

    public class UpdateAttendantRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class AssignBookingAttendantRequest
    {
        public int BookingDetailId { get; set; }
        public int PersonId { get; set; }
        public int CustomerId { get; set; }
        public DateTime AssignedFrom { get; set; } = WorkNest.Application.Services.BusinessClock.Default.Today;
    }

    public class ToggleAccessStatusRequest
    {
        public int BookingDetailId { get; set; }
        public int CustomerId { get; set; }
        public int? PersonId { get; set; } // Nullable for batch toggle
        public bool IsEnabled { get; set; }
    }

    public class AttendantCapacityCheckDto
    {
        public int BookingDetailId { get; set; }
        public string SpaceName { get; set; } = string.Empty;
        public string SpaceCategory { get; set; } = string.Empty;
        public int RoomCapacity { get; set; }
        public int CurrentActiveAttendants { get; set; }
        public bool WouldExceedCapacity { get; set; }
        public bool AllowsOverCapacity { get; set; } = true;
        public int ExcessSeatCount { get; set; }
        public decimal SeatPrice { get; set; }
        public decimal EstimatedSurcharge { get; set; }
        public decimal SubTotal { get; set; }
        public decimal SupportChargePercentage { get; set; } = 10.00m;
        public decimal SupportChargeAmount { get; set; }
        public decimal TaxPercentage { get; set; } = 16.00m;
        public decimal TaxAmount { get; set; }
        public decimal GrandTotal { get; set; }
    }

    public class BookingAttendantDetailDto
    {
        public int Id { get; set; }
        public int BookingDetailId { get; set; }
        public int PersonId { get; set; }
        public Guid PersonGuid { get; set; }
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string IdType { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public DateTime AssignedFrom { get; set; }
        public DateTime? AssignedTo { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsOverCapacity { get; set; }
        public int ExcessSeatCount { get; set; }
        public decimal? SurchargeApplied { get; set; }
        /// <summary>Machine ID (WN_HIK_PersonMap.MachineID); null until first enrollment.</summary>
        public string? MachineId { get; set; }
        /// <summary>Verified in the mobile app (Access Request), or null; shown on the web next to the user.</summary>
        public MobileAccessVerificationDto? AppAccess { get; set; }
        public int HikPendingOps { get; set; }
    }

    public class AccessStatusExportDto
    {
        public Guid PersonGuid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public int BookingDetailId { get; set; }
        public int CustomerId { get; set; }
    }

    public class CustomerActiveSpaceDto
    {
        public int BookingDetailId { get; set; }
        public string BookingGuid { get; set; } = string.Empty;
        public string SpaceName { get; set; } = string.Empty;
        public string SpaceCode { get; set; } = string.Empty;
        public string SpaceCategory { get; set; } = string.Empty;
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public int Capacity { get; set; }
        public int ActiveAttendantsCount { get; set; }
    }

    /// <summary>Details a logged-in app user enters to claim door access; matched against booking attendants.</summary>
    public class MobileAccessVerifyRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Cnic { get; set; } = string.Empty;
    }

    public class MobileAccessSpaceDto
    {
        public int BookingDetailId { get; set; }
        public string SpaceName { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }

    public class MobileAccessVerifyResult
    {
        public bool Matched { get; set; }
        /// <summary>True when at least one matched booking has access allowed (the app shows Open Door).</summary>
        public bool CanOpenDoor { get; set; }
        public string Message { get; set; } = string.Empty;
        public Guid? PersonGuid { get; set; }
        public string? Name { get; set; }
        public List<MobileAccessSpaceDto> Spaces { get; set; } = new();
        /// <summary>Matched person (server side only, never sent to the app).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int? PersonId { get; set; }
        /// <summary>Email registered for the matched person (server side only).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PersonEmail { get; set; }
    }

    /// <summary>App "Unlock door": the verified details again + the room (booking) + optionally the door.</summary>
    public class MobileDoorRequest : MobileAccessVerifyRequest
    {
        public int BookingDetailId { get; set; }
        /// <summary>The door to open (WN_HIK_Devices.Id); not used when listing doors.</summary>
        public int? DeviceId { get; set; }
    }

    public class MobileDoorDto
    {
        public int DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>"room" (the booked office's door) or "entrance" (building / floor entrance).</summary>
        public string Kind { get; set; } = "room";
        public bool Online { get; set; }
    }

    public class MobileDoorsResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? SpaceName { get; set; }
        public List<MobileDoorDto> Doors { get; set; } = new();
    }

    public class MobileOpenDoorResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Door { get; set; }
    }

    /// <summary>An access user's verification in the mobile app (WN_MobileAccessVerifications).</summary>
    public class MobileAccessVerificationDto
    {
        public string AccountEmail { get; set; } = string.Empty;
        public DateTime VerifiedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? LastUnlockAt { get; set; }
        public int UnlockCount { get; set; }
        public bool IsActive => ExpiresAt > DateTime.UtcNow;
    }
}

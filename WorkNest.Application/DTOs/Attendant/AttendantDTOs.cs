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
}

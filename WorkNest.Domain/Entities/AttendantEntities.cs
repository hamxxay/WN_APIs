using System;

namespace WorkNest.Domain.Entities
{
    public class Person
    {
        public int PersonId { get; set; }
        public Guid PersonGuid { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string IdType { get; set; } = "CNIC"; // 'CNIC' or 'Passport'
        public string IdNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CustomerAttendant
    {
        public int Id { get; set; }
        public int PersonId { get; set; }
        public int CustomerId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeactivatedAt { get; set; }
    }

    public class BookingAttendant
    {
        public int Id { get; set; }
        public int BookingDetailId { get; set; }
        public int PersonId { get; set; }
        public int CustomerId { get; set; }
        public DateTime AssignedFrom { get; set; }
        public DateTime? AssignedTo { get; set; }
        public bool IsOverCapacity { get; set; }
        public int ExcessSeatCount { get; set; }
        public decimal? SurchargeApplied { get; set; }
    }

    public class AccessStatus
    {
        public int Id { get; set; }
        public int BookingDetailId { get; set; }
        public int PersonId { get; set; }
        public int CustomerId { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RevokedAt { get; set; }
    }
}

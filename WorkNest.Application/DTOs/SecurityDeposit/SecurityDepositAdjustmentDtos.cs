using System;
using System.Collections.Generic;

namespace WorkNest.Application.DTOs.SecurityDeposit
{
    /// <summary>A sales executive / admin asks to pay one unpaid invoice of a booking from its security deposit.</summary>
    public class SecurityDepositAdjustmentCreateDto
    {
        public int BookingId { get; set; }
        public int InvoiceId { get; set; }
        /// <summary>Due date of the top-up invoice that restores the deposit. Required.</summary>
        public DateTime? TopUpDueOn { get; set; }
        public string? Remarks { get; set; }
    }

    public class SecurityDepositAdjustmentDecisionDto
    {
        public bool Approve { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>One result set of a procedure: rows keyed by column name.</summary>
    public class SdaResultSet : List<Dictionary<string, object?>> { }

    public enum SdaOutcome { Ok, BadRequest, NotFound, Forbidden }
}

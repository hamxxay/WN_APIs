using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorkNest.Application.DTOs.SecurityDeposit;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Raised for a rule the SQL procedures enforce (RAISERROR state 1 = rule, 2 = not found, 3 = location).</summary>
    public class SecurityDepositAdjustmentException : Exception
    {
        public SdaOutcome Outcome { get; }
        public SecurityDepositAdjustmentException(SdaOutcome outcome, string message) : base(message) => Outcome = outcome;
    }

    /// <summary>dbo.WN_SDA_* (script SDA_01). Own connection; sets the audit SESSION_CONTEXT for the acting user.</summary>
    public interface ISecurityDepositAdjustmentRepository
    {
        Task<List<SdaResultSet>> GetContextAsync(int bookingId, string? allowedLocationIds);
        Task<List<SdaResultSet>> ListAsync(int? bookingId, string? status, string? allowedLocationIds);
        Task<List<SdaResultSet>> CreateAsync(SecurityDepositAdjustmentCreateDto dto, int userId, string? allowedLocationIds);
        Task<List<SdaResultSet>> DecideAsync(int id, bool approve, string? note, int userId, string? allowedLocationIds);
        Task<List<SdaResultSet>> GetNoticeDepositAsync(int noticeId);
    }
}

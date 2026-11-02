using WorkNest.Application.DTOs.HikDevice;

namespace WorkNest.Application.Interfaces
{
    public class RoomReportOutcome
    {
        public RoomReportDto? Report { get; init; }
        /// <summary>HTTP status for a failure (400, 403, 404, 503); 200 when Report is set.</summary>
        public int Status { get; init; } = 200;
        public string Message { get; init; } = string.Empty;

        public static RoomReportOutcome Ok(RoomReportDto r) => new() { Report = r };
        public static RoomReportOutcome Fail(int status, string message) => new() { Status = status, Message = message };
    }

    /// <summary>Staff Room Report over the HIK door-event archive (read-only).</summary>
    public interface IRoomReportService
    {
        /// <param name="locationIds">null = all locations (super admin); otherwise only machines of these locations.</param>
        Task<RoomReportOutcome> BuildAsync(IReadOnlyCollection<int> deviceIds, int? customerId, DateTime from, DateTime to, IReadOnlyCollection<int>? locationIds);
    }
}

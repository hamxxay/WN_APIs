using WorkNest.Application.DTOs.Challan;

namespace WorkNest.Application.Interfaces
{
    public interface IChallanService
    {
        Task<IDictionary<string, object?>?> SearchAsync(string query);
        Task<(bool Ok, string? Error)> ExtendValidityAsync(ChallanExtendValidityRequest request, string updatedBy);
    }
}

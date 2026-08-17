using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IAmountFieldService
    {
        Task<ApiResponse> GetAllAsync();
        Task<ApiResponse> UpdateAccountAsync(int id, int? accountId);
    }
}

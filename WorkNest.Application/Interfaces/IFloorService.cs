using WorkNest.Application.DTOs.Floor;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    public interface IFloorService
    {
        Task<ApiResponse> GetAllFloorsAsync(int? locationId);
        Task<ApiResponse> CreateFloorAsync(FloorUpsertRequest request);
    }
}

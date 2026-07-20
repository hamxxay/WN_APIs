using WorkNest.Application.DTOs.AccountCoa;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Interfaces
{
    /// <summary>Provides access to bank accounts from dbo.AccountsCOA.</summary>
    public interface IAccountCoaService
    {
        /// <summary>Returns all active accounts sorted alphabetically by Description.</summary>
        Task<ApiResponse> GetAllAsync();

        /// <summary>Returns a single account by its primary key.</summary>
        Task<ApiResponse> GetByIdAsync(int accountId);
    }
}

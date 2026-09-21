using System.Threading.Tasks;
using WorkNest.Application.DTOs.Agreement;

namespace WorkNest.Application.Interfaces
{
    public interface ILeaseTemplateService
    {
        Task<LeaseTemplateResponseDto?> GetActiveTemplateAsync(string name);
        Task<LeaseTemplateResponseDto> PublishTemplateAsync(PublishLeaseTemplateRequest request, int? userId);
    }
}

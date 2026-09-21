using System.Threading.Tasks;

namespace WorkNest.Application.Interfaces
{
    public interface IHtmlToPdfService
    {
        Task<byte[]> ConvertHtmlToPdfAsync(string htmlContent);
    }
}

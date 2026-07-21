using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class AmountFieldService : IAmountFieldService
    {
        private readonly IDbRepository _db;
        public AmountFieldService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllAsync()
        {
            var rows = await _db.GetAllAmountFieldsAsync();
            var result = rows.Select(r => new
            {
                id       = r.TryGetValue("Id",       out var id)  ? Convert.ToInt32(id) : 0,
                entity   = r.TryGetValue("Entity",   out var en)  ? en?.ToString() : null,
                field    = r.TryGetValue("Field",    out var fi)  ? fi?.ToString() : null,
                label    = r.TryGetValue("Label",    out var la)  ? la?.ToString() : null,
                currency = r.TryGetValue("Currency", out var cu)  ? cu?.ToString() : "PKR",
            });
            return ApiResponse.Ok(result);
        }
    }
}

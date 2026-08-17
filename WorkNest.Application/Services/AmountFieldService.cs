using WorkNest.Application.DTOs.AmountField;
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
            var result = rows.Select(r => new AmountFieldDto
            {
                Id                 = r.TryGetValue("Id",                 out var id)   ? Convert.ToInt32(id)   : 0,
                Code               = r.TryGetValue("Code",               out var co)   ? co?.ToString() ?? "" : "",
                Label              = r.TryGetValue("Label",              out var la)   ? la?.ToString() ?? "" : "",
                IsDebit            = r.TryGetValue("IsDebit",            out var deb)  && Convert.ToBoolean(deb),
                IsActive           = r.TryGetValue("IsActive",           out var act)  && Convert.ToBoolean(act),
                AccountId          = r.TryGetValue("AccountId",          out var aid)  && aid is not null ? Convert.ToInt32(aid) : null,
                AccountDescription = r.TryGetValue("AccountDescription", out var adesc) ? adesc?.ToString() : null,
            });
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> UpdateAccountAsync(int id, int? accountId)
        {
            await _db.UpdateAmountFieldAccountAsync(id, accountId);
            return ApiResponse.Ok("Account updated.");
        }
    }
}

using WorkNest.Application.DTOs.AccountCoa;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class AccountCoaService : IAccountCoaService
    {
        private readonly IDbRepository _db;

        public AccountCoaService(IDbRepository db) => _db = db;

        public async Task<ApiResponse> GetAllAsync()
        {
            var rows = await _db.GetAllAccountsCoaAsync();
            var accounts = rows.Select(r => new AccountCoaDto
            {
                AccountId   = r.TryGetValue("Id",          out var id)   && id   is not null ? Convert.ToInt32(id)   :
                              r.TryGetValue("AccountId",   out var aid)  && aid  is not null ? Convert.ToInt32(aid)  : 0,
                Description = r.TryGetValue("Description", out var desc) && desc is not null ? desc.ToString()!      : string.Empty,
            }).ToList();

            return ApiResponse.Ok(accounts);
        }

        public async Task<ApiResponse> GetByIdAsync(int accountId)
        {
            var row = await _db.GetAccountCoaByIdAsync(accountId);
            if (row is null) return ApiResponse.Fail("Account not found.");

            var account = new AccountCoaDto
            {
                AccountId   = row.TryGetValue("AccountId",   out var id)   && id   is not null ? Convert.ToInt32(id)   : 0,
                Description = row.TryGetValue("Description", out var desc) && desc is not null ? desc.ToString()!      : string.Empty,
            };

            return ApiResponse.Ok(account);
        }
    }
}

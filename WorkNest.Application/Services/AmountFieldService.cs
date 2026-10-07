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

        public async Task<ApiResponse> GetWhtRatesAsync()
        {
            // dbo.WN_WHTaxRate: Description is shown, WHRate is applied. Rows marked inactive (IsActive / Status
            // = 0, when the table has such a column) and rates outside 0-100 are left out.
            static object? Col(IDictionary<string, object?> r, params string[] names)
            {
                foreach (var n in names)
                    foreach (var kv in r)
                        if (string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return null;
            }
            static bool Inactive(object? v) => v is not null && v is not DBNull &&
                (v is bool b ? !b : decimal.TryParse(v.ToString(), out var d) && d == 0);

            var rows = await _db.GetWhtRateOptionsAsync();
            var result = rows
                .Where(r => !Inactive(Col(r, "IsActive", "Status", "Active")))
                .Select(r =>
                {
                    var rate = Col(r, "WHRate", "WHTRate", "Rate");
                    var id = Col(r, "Id", "WHTaxRateId");
                    return new WhtRateOptionDto
                    {
                        Id = id is not null && id is not DBNull && int.TryParse(id.ToString(), out var i) ? i : null,
                        Description = Col(r, "Description")?.ToString()?.Trim() ?? "",
                        Rate = rate is not null && rate is not DBNull && decimal.TryParse(rate.ToString(), out var d) ? d : 0m,
                    };
                })
                .Where(o => o.Rate > 0 && o.Rate < 100 && o.Description.Length > 0)
                .OrderBy(o => o.Description)
                .ToList();
            return ApiResponse.Ok(result);
        }

        public async Task<ApiResponse> UpdateAccountAsync(int id, int? accountId)
        {
            await _db.UpdateAmountFieldAccountAsync(id, accountId);
            return ApiResponse.Ok("Account updated.");
        }
    }
}

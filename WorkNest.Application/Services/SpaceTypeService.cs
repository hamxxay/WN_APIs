using WorkNest.Application.DTOs.SpaceType;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    public class SpaceTypeService : ISpaceTypeService
    {
        private readonly IDbRepository _db;
        public SpaceTypeService(IDbRepository db) => _db = db;

        public async Task<IEnumerable<object>> GetAllSpaceTypesAsync(int? locationId = null) =>
            (await _db.GetAllSpaceTypesAsync(Location(locationId))).Cast<object>();

        public async Task<(IEnumerable<object> Items, int Total)> GetSpaceTypesAsync(int page, int limit, int? locationId = null)
        {
            var (rows, total) = await _db.GetSpaceTypesAsync(page, limit, Location(locationId));
            return (rows.Cast<object>(), total);
        }

        public async Task<IDictionary<string, object?>?> FindSpaceTypeAsync(int? id, Guid? publicId)
        {
            var rows = await _db.GetAllSpaceTypesAsync();
            var guid = publicId?.ToString();
            return rows.FirstOrDefault(r =>
                (id is not null && IdOf(r) == id) ||
                (guid is not null && ((r.TryGetValue("IdGUID", out var g) && string.Equals(g?.ToString(), guid, StringComparison.OrdinalIgnoreCase))
                                   || (r.TryGetValue("PublicId", out var p) && string.Equals(p?.ToString(), guid, StringComparison.OrdinalIgnoreCase)))));
        }

        public async Task<ApiResponse> CreateSpaceTypeAsync(SpaceTypeUpsertRequest request, int? actorId)
        {
            var name = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : request.Description ?? string.Empty;
            var desc = !string.IsNullOrWhiteSpace(request.Description) ? request.Description : name;
            try
            {
                var (id, publicId, locationId) = await _db.InsertSpaceTypeAsync(
                    name, desc, request.CategoryId, request.Capacity, request.HourlyAllowed,
                    request.AccountReceivableId, request.RentAccountId, request.ServicesIncomeId, request.SalesTaxId, request.SecurityReceivedId,
                    actorId, Location(request.LocationId));
                return ApiResponse.Ok(new { id, publicId, locationId }, "Space type created.");
            }
            catch (SpaceTypeRuleException ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> UpdateSpaceTypeAsync(int id, SpaceTypeUpsertRequest request, int? actorId)
        {
            var name = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : request.Description;
            var desc = !string.IsNullOrWhiteSpace(request.Description) ? request.Description : name;
            try
            {
                await _db.UpdateSpaceTypeAsync(id, name, desc,
                    request.CategoryId, request.Capacity, request.HourlyAllowed,
                    request.AccountReceivableId, request.RentAccountId, request.ServicesIncomeId, request.SalesTaxId, request.SecurityReceivedId,
                    actorId, Location(request.LocationId));
                return ApiResponse.Ok($"Space type #{id} updated.");
            }
            catch (SpaceTypeRuleException ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        public async Task<ApiResponse> DeleteSpaceTypeAsync(int id)
        {
            try
            {
                await _db.DeleteSpaceTypeAsync(id);
                return ApiResponse.Ok("Space type deleted.");
            }
            catch (SpaceTypeRuleException ex)
            {
                return ApiResponse.Fail(ex.Message);
            }
        }

        /// <summary>The row's Id (0 when missing).</summary>
        public static int IdOf(IDictionary<string, object?> row) =>
            row.TryGetValue("Id", out var id) && id is not null ? Convert.ToInt32(id) : 0;

        /// <summary>The type's location; null for rows read before P1 (the database has no space type locations yet).</summary>
        public static int? LocationOf(IDictionary<string, object?> row) =>
            row.TryGetValue("LocationId", out var l) && l is not null ? Convert.ToInt32(l) : null;

        /// <summary>Only a real location id is passed on (0 / negative = none).</summary>
        private static int? Location(int? locationId) => locationId is > 0 ? locationId : null;
    }
}

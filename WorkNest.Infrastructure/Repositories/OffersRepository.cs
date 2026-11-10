using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Offers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Offers: dbo.WN_Offers and dbo.WN_Offers_GetList / _Save / _Deactivate / _GetRunning (script OFFER_01_offers). Raw SqlClient,
    /// one procedure per call. The admin calls turn "table / procedure missing" (208 / 2812) into <see cref="OffersMissingException"/>;
    /// the running-offer and snapshot lookups answer "no offer" instead, so quotations, bookings and invoices keep working before the script.
    /// </summary>
    public sealed class OffersRepository : IOffersRepository
    {
        private const int CommandTimeoutSeconds = 15;
        private readonly string _connectionString;

        public OffersRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var c = new SqlConnection(_connectionString);
            await c.OpenAsync(ct);
            return c;
        }

        /// <summary>The audit identity (WN_AuditLog triggers read SESSION_CONTEXT), as ServiceSwitchRepository sets it. Never blocks the change.</summary>
        private static async Task SetAuditContextAsync(SqlConnection c, string? userEmail, CancellationToken ct)
        {
            try
            {
                await using var cmd = new SqlCommand(@"
                    SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                    DECLARE @ResolvedId INT = NULL;
                    IF @UserEmail IS NOT NULL SELECT TOP 1 @ResolvedId = Id FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @UserEmail;
                    IF @ResolvedId IS NOT NULL EXEC sp_set_session_context @key = N'AppUserId', @value = @ResolvedId;
                    IF @UserEmail IS NOT NULL EXEC sp_set_session_context @key = N'AppUserEmail', @value = @UserEmail;
                    EXEC sp_set_session_context @key = N'SourceApp', @value = N'WebAPI';", c);
                cmd.Parameters.Add("@UserEmail", SqlDbType.NVarChar, 256).Value = (object?)userEmail ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        private static async Task<int?> ResolveUserIdAsync(SqlConnection c, string? email, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            try
            {
                await using var cmd = new SqlCommand("SELECT TOP 1 Id FROM dbo.WN_Users WITH (NOLOCK) WHERE Email = @Email", c);
                cmd.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;
                return await cmd.ExecuteScalarAsync(ct) is int id ? id : null;
            }
            catch (SqlException) { return null; }
        }

        private static DateTime Local(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Unspecified);

        private static OfferDto MapOffer(SqlDataReader r)
        {
            var cols = new HashSet<string>(Enumerable.Range(0, r.FieldCount).Select(r.GetName), StringComparer.OrdinalIgnoreCase);
            object? V(string n) => cols.Contains(n) && !r.IsDBNull(r.GetOrdinal(n)) ? r.GetValue(r.GetOrdinal(n)) : null;
            return new OfferDto
            {
                OfferId = Convert.ToInt32(V("OfferId")),
                Name = Convert.ToString(V("Name")) ?? "",
                DiscountType = Convert.ToString(V("DiscountType")) ?? "PERCENT",
                DiscountValue = V("DiscountValue") is { } dv ? Convert.ToDecimal(dv) : 0m,
                StartDate = V("StartDate") is DateTime s ? Local(s) : default,
                EndDate = V("EndDate") is DateTime e ? Local(e) : default,
                LocationId = V("LocationId") is { } li ? Convert.ToInt32(li) : null,
                LocationName = Convert.ToString(V("LocationName")),
                ApplyTo = Convert.ToString(V("ApplyTo")) ?? "FIRST_PERIOD",
                ApplyToDeposit = V("ApplyToDeposit") is { } ad && Convert.ToBoolean(ad),
                IsActive = V("IsActive") is not { } ia || Convert.ToBoolean(ia),
                CreatedBy = V("CreatedBy") is { } cb ? Convert.ToInt32(cb) : null,
                CreatedAt = V("CreatedAt") is DateTime ca ? ca : null,
                UpdatedBy = V("UpdatedBy") is { } ub ? Convert.ToInt32(ub) : null,
                UpdatedAt = V("UpdatedAt") is DateTime ua ? ua : null,
                Status = Convert.ToString(V("Status")),
                UsedCount = V("UsedCount") is { } uc ? Convert.ToInt32(uc) : 0
            };
        }

        public async Task<IReadOnlyList<OfferDto>> GetListAsync(CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                await using var cmd = new SqlCommand("dbo.WN_Offers_GetList", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<OfferDto>();
                while (await r.ReadAsync(ct)) list.Add(MapOffer(r));
                return list;
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new OffersMissingException(ex); }
        }

        public async Task<int> SaveAsync(int? offerId, OfferSaveCommand command, string? userEmail, CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                await SetAuditContextAsync(c, userEmail, ct);
                var userId = await ResolveUserIdAsync(c, userEmail, ct);
                await using var cmd = new SqlCommand("dbo.WN_Offers_Save", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@OfferId", SqlDbType.Int).Value = (object?)offerId ?? DBNull.Value;
                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = command.Name;
                cmd.Parameters.Add("@DiscountType", SqlDbType.VarChar, 10).Value = command.DiscountType;
                var dv = cmd.Parameters.Add("@DiscountValue", SqlDbType.Decimal); dv.Precision = 10; dv.Scale = 2; dv.Value = command.DiscountValue;
                cmd.Parameters.Add("@StartDate", SqlDbType.DateTime).Value = command.StartDate;
                cmd.Parameters.Add("@EndDate", SqlDbType.DateTime).Value = command.EndDate;
                cmd.Parameters.Add("@LocationId", SqlDbType.Int).Value = (object?)command.LocationId ?? DBNull.Value;
                cmd.Parameters.Add("@ApplyTo", SqlDbType.VarChar, 20).Value = command.ApplyTo;
                cmd.Parameters.Add("@ApplyToDeposit", SqlDbType.Bit).Value = command.ApplyToDeposit;
                cmd.Parameters.Add("@IsActive", SqlDbType.Bit).Value = command.IsActive;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = (object?)userId ?? DBNull.Value;
                var scalar = await cmd.ExecuteScalarAsync(ct);
                return scalar is null or DBNull ? (offerId ?? 0) : Convert.ToInt32(scalar);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new OffersMissingException(ex); }
            catch (SqlException ex) when (ex.Number == 50000) { throw new OfferRuleException(ex.Message); }
        }

        public async Task DeactivateAsync(int offerId, string? userEmail, CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                await SetAuditContextAsync(c, userEmail, ct);
                var userId = await ResolveUserIdAsync(c, userEmail, ct);
                await using var cmd = new SqlCommand("dbo.WN_Offers_Deactivate", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@OfferId", SqlDbType.Int).Value = offerId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = (object?)userId ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812) { throw new OffersMissingException(ex); }
            catch (SqlException ex) when (ex.Number == 50000) { throw new OfferRuleException(ex.Message); }
        }

        public async Task<OfferDto?> GetRunningAsync(int? locationId, DateTime? at, CancellationToken ct = default)
        {
            try
            {
                await using var c = await OpenAsync(ct);
                await using var cmd = new SqlCommand("dbo.WN_Offers_GetRunning", c) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds };
                cmd.Parameters.Add("@LocationId", SqlDbType.Int).Value = (object?)locationId ?? DBNull.Value;
                cmd.Parameters.Add("@At", SqlDbType.DateTime).Value = at.HasValue ? at.Value : DBNull.Value;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                return await r.ReadAsync(ct) ? MapOffer(r) : null;
            }
            catch (SqlException ex) when (ex.Number is 208 or 2812 or 207) { return null; }   // script not run: no offer
        }

        public async Task<IReadOnlyDictionary<int, OfferSnapshot>> GetSnapshotsAsync(OfferEntity entity, IEnumerable<int> ids, CancellationToken ct = default)
        {
            var list = ids.Where(i => i > 0).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, OfferSnapshot>();
            await using var c = await OpenAsync(ct);
            return await OfferSnapshotStore.ReadAsync(c, entity, list, ct);
        }
    }
}

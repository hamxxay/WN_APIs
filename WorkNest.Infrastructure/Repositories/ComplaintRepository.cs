using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>Data access for dbo.WN_Complaints via stored procedures.</summary>
    public class ComplaintRepository : IComplaintRepository
    {
        private readonly string _connectionString;

        public ComplaintRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        private async Task<SqlConnection> OpenAsync()
        {
            var c = new SqlConnection(_connectionString);
            await c.OpenAsync();
            return c;
        }

        public async Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status,
            BranchFilter? branch = null)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Complaints_GetList", c) { CommandType = CommandType.StoredProcedure };
            var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Search", (object?)s ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Offset", (page - 1) * limit);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = (object?)branch?.Csv ?? DBNull.Value;
            cmd.Parameters.Add("@NoBranch", SqlDbType.Bit).Value = branch?.IncludeUnassigned ?? true;
            var items = new List<ComplaintDto>();
            int total = 0;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                items.Add(Map(r));
                total = Convert.ToInt32(r["TotalCount"]);
            }
            return (items, total);
        }

        public async Task<ComplaintDto?> GetByIdAsync(int id)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Complaints_GetById", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? Map(r) : null;
        }

        public async Task<int?> InsertAsync(ComplaintDto x, int? createdById)
        {
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_Complaints_Insert", c) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@No", x.ComplaintNo);
                cmd.Parameters.AddWithValue("@Source", x.Source);
                cmd.Parameters.AddWithValue("@Name", (object?)x.CustomerName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Phone", (object?)x.PhoneNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Email", (object?)x.Email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Branch", (object?)x.Branch ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Category", x.Category);
                cmd.Parameters.AddWithValue("@Description", x.Description);
                cmd.Parameters.AddWithValue("@IsFollowUp", x.IsFollowUp);
                cmd.Parameters.AddWithValue("@PrevId", (object?)x.PreviousComplaintId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedById", (object?)createdById ?? DBNull.Value);
                return Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601) { return null; } // ComplaintNo already used: caller retries
        }

        public async Task UpdateStatusAsync(int id, string status, string? staffNotes, int? updatedById)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Complaints_UpdateStatus", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Notes", (object?)staffNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@By", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarkResolvedNotificationSentAsync(int id)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Complaints_MarkResolvedNotificationSent", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<ComplaintDto?> GetAwaitingResolutionReplyAsync(IEnumerable<string> phoneVariants, DateTime notifiedSince)
        {
            var phones = phoneVariants.Distinct().ToList();
            if (phones.Count == 0) return null;
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Complaints_GetAwaitingResolutionReply", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Phones", SqlDbType.NVarChar, -1).Value = string.Join(",", phones);
            cmd.Parameters.Add("@Since", SqlDbType.DateTime2).Value = notifiedSince;
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? Map(r) : null;
        }

        public async Task SetResolutionResponseAsync(int id, string response, string? newStatus)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("dbo.WN_Complaints_SetResolutionResponse", c) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Response", response);
            cmd.Parameters.AddWithValue("@Status", (object?)newStatus ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<int>> GetOpenIdsAsync(IReadOnlyCollection<int>? locationIds = null)
        {
            var ids = new List<int>();
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_Complaints_GetOpenIds", c) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@LocationIds", SqlDbType.NVarChar, -1).Value = locationIds is null ? DBNull.Value : string.Join(",", locationIds.Distinct());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            }
            catch (SqlException ex) when (ex.Number == 208) { } // table not created yet
            return ids;
        }

        public async Task<int?> UpdateBranchAsync(int id, string? branch, int? updatedById)
        {
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand("dbo.WN_Complaints_UpdateBranch", c) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@Branch", SqlDbType.NVarChar, 50).Value = (object?)branch ?? DBNull.Value;
                cmd.Parameters.Add("@UpdatedById", SqlDbType.Int).Value = (object?)updatedById ?? DBNull.Value;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
            }
            catch (SqlException ex) when (ex.Number == 2812) { return null; } // procedure missing: BR_01 not run
        }

        private static ComplaintDto Map(SqlDataReader r)
        {
            string? S(string k) => r[k] is DBNull ? null : r[k].ToString();
            DateTime? D(string k) => r[k] is DateTime d ? d : null;
            return new ComplaintDto
            {
                Id = Convert.ToInt32(r["Id"]),
                ComplaintNo = S("ComplaintNo") ?? "",
                Source = S("Source") ?? "",
                CustomerName = S("CustomerName"),
                PhoneNumber = S("PhoneNumber"),
                Email = S("Email"),
                Branch = S("Branch"),
                LocationId = r["LocationId"] is DBNull ? null : Convert.ToInt32(r["LocationId"]),
                LocationName = S("LocationName"),
                Category = S("Category") ?? "",
                Description = S("Description") ?? "",
                Status = S("Status") ?? ComplaintStatus.Open,
                IsFollowUp = r["IsFollowUp"] is bool f && f,
                PreviousComplaintId = r["PreviousComplaintId"] is DBNull ? null : Convert.ToInt32(r["PreviousComplaintId"]),
                PreviousComplaintNo = S("PreviousComplaintNo"),
                ResolvedNotificationSent = r["ResolvedNotificationSent"] is bool n && n,
                ResolvedNotificationSentAt = D("ResolvedNotificationSentAt"),
                ResolutionResponse = S("ResolutionResponse"),
                ResolutionResponseAt = D("ResolutionResponseAt"),
                StaffNotes = S("StaffNotes"),
                CreatedOn = D("CreatedOn") ?? DateTime.MinValue,
                UpdatedOn = D("UpdatedOn")
            };
        }
    }
}

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>Data access for dbo.WN_Complaints. Reads use NOLOCK.</summary>
    public class ComplaintRepository : IComplaintRepository
    {
        private readonly string _connectionString;

        private const string SelectColumns = @"
            c.Id, c.ComplaintNo, c.Source, c.CustomerName, c.PhoneNumber, c.Email, c.Branch, c.Category, c.Description,
            c.Status, c.IsFollowUp, c.PreviousComplaintId, p.ComplaintNo AS PreviousComplaintNo,
            c.ResolvedNotificationSent, c.ResolvedNotificationSentAt, c.ResolutionResponse, c.ResolutionResponseAt,
            c.StaffNotes, c.CreatedOn, c.UpdatedOn";

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

        public async Task<(List<ComplaintDto> Items, int Total)> GetListAsync(int page, int limit, string? search, string? status)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand($@"
                SELECT {SelectColumns}, COUNT(*) OVER () AS TotalCount
                FROM dbo.WN_Complaints c WITH (NOLOCK)
                LEFT JOIN dbo.WN_Complaints p WITH (NOLOCK) ON p.Id = c.PreviousComplaintId
                WHERE (@Status IS NULL OR c.Status = @Status)
                  AND (@Search IS NULL OR c.ComplaintNo LIKE @Like OR c.CustomerName LIKE @Like OR c.PhoneNumber LIKE @Like
                       OR c.Category LIKE @Like OR c.Description LIKE @Like)
                ORDER BY CASE c.Status WHEN N'open' THEN 0 WHEN N'in_progress' THEN 1 WHEN N'resolved' THEN 2 ELSE 3 END, c.CreatedOn DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;", c);
            var s = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Search", (object?)s ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Like", s == null ? DBNull.Value : "%" + s + "%");
            cmd.Parameters.AddWithValue("@Offset", (page - 1) * limit);
            cmd.Parameters.AddWithValue("@Limit", limit);
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
            await using var cmd = new SqlCommand($@"
                SELECT {SelectColumns}
                FROM dbo.WN_Complaints c WITH (NOLOCK)
                LEFT JOIN dbo.WN_Complaints p WITH (NOLOCK) ON p.Id = c.PreviousComplaintId
                WHERE c.Id = @Id;", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? Map(r) : null;
        }

        public async Task<int?> InsertAsync(ComplaintDto x, int? createdById)
        {
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.WN_Complaints (ComplaintNo, Source, CustomerName, PhoneNumber, Email, Branch, Category, Description,
                                                   IsFollowUp, PreviousComplaintId, CreatedById)
                    OUTPUT INSERTED.Id
                    VALUES (@No, @Source, @Name, @Phone, @Email, @Branch, @Category, @Description, @IsFollowUp, @PrevId, @CreatedById);", c);
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
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_Complaints
                SET Status = @Status, StaffNotes = COALESCE(@Notes, StaffNotes), UpdatedById = @By, UpdatedOn = SYSDATETIME()
                WHERE Id = @Id;", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Notes", (object?)staffNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@By", (object?)updatedById ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task MarkResolvedNotificationSentAsync(int id)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_Complaints
                SET ResolvedNotificationSent = 1, ResolvedNotificationSentAt = SYSDATETIME(),
                    ResolutionResponse = NULL, ResolutionResponseAt = NULL, UpdatedOn = SYSDATETIME()
                WHERE Id = @Id;", c);
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<ComplaintDto?> GetAwaitingResolutionReplyAsync(IEnumerable<string> phoneVariants, DateTime notifiedSince)
        {
            var phones = phoneVariants.Distinct().ToList();
            if (phones.Count == 0) return null;
            await using var c = await OpenAsync();
            var names = phones.Select((_, i) => "@P" + i).ToList();
            await using var cmd = new SqlCommand($@"
                SELECT TOP 1 {SelectColumns}
                FROM dbo.WN_Complaints c WITH (NOLOCK)
                LEFT JOIN dbo.WN_Complaints p WITH (NOLOCK) ON p.Id = c.PreviousComplaintId
                WHERE c.PhoneNumber IN ({string.Join(",", names)})
                  AND c.Status = N'resolved' AND c.ResolvedNotificationSent = 1 AND c.ResolutionResponse IS NULL
                  AND c.ResolvedNotificationSentAt >= @Since
                ORDER BY c.ResolvedNotificationSentAt DESC, c.Id DESC;", c);
            for (var i = 0; i < phones.Count; i++) cmd.Parameters.AddWithValue(names[i], phones[i]);
            cmd.Parameters.Add("@Since", SqlDbType.DateTime2).Value = notifiedSince;
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? Map(r) : null;
        }

        public async Task SetResolutionResponseAsync(int id, string response, string? newStatus)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_Complaints
                SET ResolutionResponse = @Response, ResolutionResponseAt = SYSDATETIME(),
                    Status = COALESCE(@Status, Status), UpdatedOn = SYSDATETIME()
                WHERE Id = @Id;", c);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Response", response);
            cmd.Parameters.AddWithValue("@Status", (object?)newStatus ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<int>> GetOpenIdsAsync()
        {
            var ids = new List<int>();
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand("SELECT Id FROM dbo.WN_Complaints WITH (NOLOCK) WHERE Status = N'open';", c);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
            }
            catch (SqlException ex) when (ex.Number == 208) { } // table not created yet
            return ids;
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

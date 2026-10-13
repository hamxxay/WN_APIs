using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.AuditLog;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Stored Procedure data access for dbo.WN_AuditLog following the solution's established pattern.
    /// Calls dbo.WN_AuditLog_GetPaged, dbo.WN_AuditLog_GetById, and dbo.WN_AuditLog_GetFilterOptions.
    /// </summary>
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly string _connectionString;
        private readonly IBusinessClock _clock;

        public AuditLogRepository(IConfiguration configuration, IBusinessClock clock)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
            _clock = clock;
        }

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        public async Task<AuditLogPagedResultDto> GetPagedAuditLogsAsync(AuditLogFilterDto filter, CancellationToken ct = default)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize > 0 ? filter.PageSize : 50, 1, 200);

            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.WN_AuditLog_GetPaged", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.Add(new SqlParameter("@Page", SqlDbType.Int) { Value = page });
            cmd.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            cmd.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime2)
            {
                Value = filter.FromDate.HasValue ? filter.FromDate.Value : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@ToDate", SqlDbType.DateTime2)
            {
                Value = filter.ToDate.HasValue ? filter.ToDate.Value : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@Operation", SqlDbType.Char, 1)
            {
                Value = !string.IsNullOrWhiteSpace(filter.Operation)
                    ? filter.Operation.Trim().ToUpperInvariant()[0]
                    : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@EntityName", SqlDbType.NVarChar, 128)
            {
                Value = !string.IsNullOrWhiteSpace(filter.EntityName)
                    ? filter.EntityName.Trim()
                    : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@ChangedByUserEmail", SqlDbType.NVarChar, 256)
            {
                Value = !string.IsNullOrWhiteSpace(filter.ChangedByUserEmail)
                    ? filter.ChangedByUserEmail.Trim()
                    : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@RecordId", SqlDbType.NVarChar, 64)
            {
                Value = !string.IsNullOrWhiteSpace(filter.RecordId)
                    ? filter.RecordId.Trim()
                    : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@SourceApp", SqlDbType.NVarChar, 50)
            {
                Value = !string.IsNullOrWhiteSpace(filter.SourceApp)
                    ? filter.SourceApp.Trim()
                    : DBNull.Value
            });

            cmd.Parameters.Add(new SqlParameter("@ErrorsOnly", SqlDbType.Bit)
            {
                Value = filter.ErrorsOnly
            });

            var result = new AuditLogPagedResultDto
            {
                Page = page,
                PageSize = pageSize,
                Items = new List<AuditLogDto>()
            };

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Result set 1: TotalCount
            if (await reader.ReadAsync(ct))
            {
                result.TotalCount = reader.GetInt32(reader.GetOrdinal("TotalCount"));
            }

            // Result set 2: Paged items
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var changedAtUtc = reader.GetDateTime(reader.GetOrdinal("ChangedAt"));
                    var changedAtLocal = _clock.FromUtc(changedAtUtc);

                    result.Items.Add(new AuditLogDto
                    {
                        AuditId = reader.GetInt64(reader.GetOrdinal("AuditId")),
                        EntityName = reader.IsDBNull(reader.GetOrdinal("EntityName")) ? null : reader.GetString(reader.GetOrdinal("EntityName")),
                        RecordId = reader.IsDBNull(reader.GetOrdinal("RecordId")) ? null : reader.GetString(reader.GetOrdinal("RecordId")),
                        Operation = reader.GetString(reader.GetOrdinal("Operation")).Trim(),
                        ChangedByUserId = reader.IsDBNull(reader.GetOrdinal("ChangedByUserId")) ? null : reader.GetInt32(reader.GetOrdinal("ChangedByUserId")),
                        ChangedByUserEmail = reader.IsDBNull(reader.GetOrdinal("ChangedByUserEmail")) ? null : reader.GetString(reader.GetOrdinal("ChangedByUserEmail")),
                        ChangedAtUtc = changedAtUtc,
                        ChangedAt = changedAtLocal,
                        SourceApp = reader.IsDBNull(reader.GetOrdinal("SourceApp")) ? null : reader.GetString(reader.GetOrdinal("SourceApp")),
                        ErrorSeverity = reader.IsDBNull(reader.GetOrdinal("ErrorSeverity")) ? null : reader.GetString(reader.GetOrdinal("ErrorSeverity")),
                        HasError = reader.GetInt32(reader.GetOrdinal("HasError")) == 1
                    });
                }
            }

            return result;
        }

        public async Task<AuditLogDetailDto?> GetAuditLogDetailAsync(long auditId, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.WN_AuditLog_GetById", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add(new SqlParameter("@AuditId", SqlDbType.BigInt) { Value = auditId });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            var changedAtUtc = reader.GetDateTime(reader.GetOrdinal("ChangedAt"));
            var changedAtLocal = _clock.FromUtc(changedAtUtc);

            return new AuditLogDetailDto
            {
                AuditId = reader.GetInt64(reader.GetOrdinal("AuditId")),
                EntityName = reader.IsDBNull(reader.GetOrdinal("EntityName")) ? null : reader.GetString(reader.GetOrdinal("EntityName")),
                RecordId = reader.IsDBNull(reader.GetOrdinal("RecordId")) ? null : reader.GetString(reader.GetOrdinal("RecordId")),
                Operation = reader.GetString(reader.GetOrdinal("Operation")).Trim(),
                OldValues = reader.IsDBNull(reader.GetOrdinal("OldValues")) ? null : reader.GetString(reader.GetOrdinal("OldValues")),
                NewValues = reader.IsDBNull(reader.GetOrdinal("NewValues")) ? null : reader.GetString(reader.GetOrdinal("NewValues")),
                ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage")),
                ErrorSeverity = reader.IsDBNull(reader.GetOrdinal("ErrorSeverity")) ? null : reader.GetString(reader.GetOrdinal("ErrorSeverity")),
                StackTrace = reader.IsDBNull(reader.GetOrdinal("StackTrace")) ? null : reader.GetString(reader.GetOrdinal("StackTrace")),
                ChangedByUserId = reader.IsDBNull(reader.GetOrdinal("ChangedByUserId")) ? null : reader.GetInt32(reader.GetOrdinal("ChangedByUserId")),
                ChangedByUserEmail = reader.IsDBNull(reader.GetOrdinal("ChangedByUserEmail")) ? null : reader.GetString(reader.GetOrdinal("ChangedByUserEmail")),
                ChangedAtUtc = changedAtUtc,
                ChangedAt = changedAtLocal,
                SourceApp = reader.IsDBNull(reader.GetOrdinal("SourceApp")) ? null : reader.GetString(reader.GetOrdinal("SourceApp")),
                HasError = reader.GetInt32(reader.GetOrdinal("HasError")) == 1
            };
        }

        public async Task<AuditLogFilterOptionsDto> GetFilterOptionsAsync(CancellationToken ct = default)
        {
            var options = new AuditLogFilterOptionsDto();

            await using var conn = await OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.WN_AuditLog_GetFilterOptions", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Result set 1: Distinct EntityNames
            while (await reader.ReadAsync(ct))
            {
                options.EntityNames.Add(reader.GetString(0));
            }

            // Result set 2: Distinct SourceApps
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    options.SourceApps.Add(reader.GetString(0));
                }
            }

            return options;
        }
    }
}

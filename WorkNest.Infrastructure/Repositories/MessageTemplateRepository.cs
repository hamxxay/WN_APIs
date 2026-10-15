using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.MessageTemplate;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>
    /// Message templates: dbo.WN_MessageTemplates_Get / dbo.WN_MessageTemplates_Save (script WT_01_whatsapp_template).
    /// SQL errors 2812 / 208 (procedure / table missing) mean WT_01 has not run: <see cref="MessageTemplatesMissingException"/>.
    /// </summary>
    public class MessageTemplateRepository : IMessageTemplateRepository
    {
        private readonly string _connectionString;

        public MessageTemplateRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
        }

        public Task<MessageTemplateRow?> GetAsync(string templateKey) =>
            RunAsync("dbo.WN_MessageTemplates_Get", cmd =>
                cmd.Parameters.Add("@TemplateKey", SqlDbType.NVarChar, 100).Value = templateKey);

        public Task<MessageTemplateRow?> SaveAsync(string templateKey, string template, int? updatedById) =>
            RunAsync("dbo.WN_MessageTemplates_Save", cmd =>
            {
                cmd.Parameters.Add("@TemplateKey", SqlDbType.NVarChar, 100).Value = templateKey;
                cmd.Parameters.Add("@Template", SqlDbType.NVarChar, 2000).Value = template;
                cmd.Parameters.Add("@UpdatedById", SqlDbType.Int).Value = (object?)updatedById ?? DBNull.Value;
            });

        private async Task<MessageTemplateRow?> RunAsync(string procedure, Action<SqlCommand> parameters)
        {
            try
            {
                await using var c = new SqlConnection(_connectionString);
                await c.OpenAsync();
                await using var cmd = new SqlCommand(procedure, c) { CommandType = CommandType.StoredProcedure };
                parameters(cmd);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return null;
                return new MessageTemplateRow
                {
                    TemplateKey = Convert.ToString(r["TemplateKey"]) ?? "",
                    Template = Convert.ToString(r["Template"]) ?? "",
                    UpdatedById = r["UpdatedById"] is DBNull ? null : Convert.ToInt32(r["UpdatedById"]),
                    // DATETIME2 written with SYSUTCDATETIME(): UTC (JSON "Z").
                    UpdatedOn = r["UpdatedOn"] is DBNull ? null : DateTime.SpecifyKind(Convert.ToDateTime(r["UpdatedOn"]), DateTimeKind.Utc),
                    UpdatedByName = r["UpdatedByName"] is DBNull ? null : Convert.ToString(r["UpdatedByName"])
                };
            }
            catch (SqlException ex) when (ex.Number is 2812 or 208) // procedure / table missing: WT_01 not run
            {
                throw new MessageTemplatesMissingException(ex);
            }
        }
    }
}

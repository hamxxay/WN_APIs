using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WorkNest.Application.DTOs.WhatsApp;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.Repositories
{
    /// <summary>Data access for dbo.WN_WhatsApp_Contacts / _Conversations / _Messages / _BotState. Reads use NOLOCK.</summary>
    public class WhatsAppRepository : IWhatsAppRepository
    {
        private readonly string _connectionString;

        // The 24-hour window is computed in SQL so the API and database clocks can't disagree.
        private const string ConversationSelect = @"
            cv.Id, cv.ContactId, ct.PhoneNumber, ct.Name, ct.Tags, ct.AdminNotes, cv.Status, cv.LastMessageAt, cv.LastMessageText,
            cv.LastIncomingAt, cv.UnreadCount,
            CAST(CASE WHEN cv.LastIncomingAt IS NOT NULL AND DATEDIFF(SECOND, cv.LastIncomingAt, SYSDATETIME()) <= 86400 THEN 1 ELSE 0 END AS BIT) AS InReplyWindow";

        public WhatsAppRepository(IConfiguration configuration)
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

        public async Task<(int ContactId, int ConversationId)> EnsureConversationAsync(string phone, string? name)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                DECLARE @ContactId INT = (SELECT Id FROM dbo.WN_WhatsApp_Contacts WITH (UPDLOCK, HOLDLOCK) WHERE PhoneNumber = @Phone);
                IF @ContactId IS NULL
                BEGIN
                    INSERT INTO dbo.WN_WhatsApp_Contacts (PhoneNumber, Name) VALUES (@Phone, @Name);
                    SET @ContactId = SCOPE_IDENTITY();
                END
                ELSE IF @Name IS NOT NULL
                    UPDATE dbo.WN_WhatsApp_Contacts SET Name = @Name, UpdatedOn = SYSDATETIME()
                    WHERE Id = @ContactId AND ISNULL(Name, N'') <> @Name;

                DECLARE @ConversationId INT = (SELECT Id FROM dbo.WN_WhatsApp_Conversations WITH (UPDLOCK, HOLDLOCK) WHERE ContactId = @ContactId);
                IF @ConversationId IS NULL
                BEGIN
                    INSERT INTO dbo.WN_WhatsApp_Conversations (ContactId) VALUES (@ContactId);
                    SET @ConversationId = SCOPE_IDENTITY();
                END
                COMMIT;
                SELECT @ContactId, @ConversationId;", c);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@Name", string.IsNullOrWhiteSpace(name) ? DBNull.Value : name.Trim());
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            return (Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)));
        }

        public async Task<bool> AddMessageAsync(int conversationId, string direction, string messageType, string? text, string? whatsAppMessageId, int? sentByUserId)
        {
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand(@"
                    SET XACT_ABORT ON;
                    BEGIN TRAN;
                    INSERT INTO dbo.WN_WhatsApp_Messages (ConversationId, WhatsAppMessageId, Direction, MessageType, MessageText, SentByUserId)
                    VALUES (@ConversationId, @WaId, @Direction, @Type, @Text, @UserId);
                    UPDATE dbo.WN_WhatsApp_Conversations
                    SET LastMessageAt = SYSDATETIME(),
                        LastMessageText = LEFT(@Text, 500),
                        LastIncomingAt = CASE WHEN @Direction = N'incoming' THEN SYSDATETIME() ELSE LastIncomingAt END,
                        UnreadCount = CASE WHEN @Direction = N'incoming' THEN UnreadCount + 1 ELSE UnreadCount END,
                        Status = CASE WHEN @Direction = N'incoming' THEN N'open' ELSE Status END,
                        UpdatedOn = SYSDATETIME()
                    WHERE Id = @ConversationId;
                    COMMIT;", c);
                cmd.Parameters.AddWithValue("@ConversationId", conversationId);
                cmd.Parameters.AddWithValue("@WaId", string.IsNullOrWhiteSpace(whatsAppMessageId) ? DBNull.Value : whatsAppMessageId);
                cmd.Parameters.AddWithValue("@Direction", direction);
                cmd.Parameters.AddWithValue("@Type", messageType);
                cmd.Parameters.AddWithValue("@Text", (object?)text ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserId", (object?)sentByUserId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { return false; } // already saved (Meta re-delivery)
        }

        public async Task<(string State, string? Data)?> GetBotStateAsync(string phone)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("SELECT State, Data FROM dbo.WN_WhatsApp_BotState WITH (NOLOCK) WHERE PhoneNumber = @Phone;", c);
            cmd.Parameters.AddWithValue("@Phone", phone);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
        }

        public async Task SaveBotStateAsync(string phone, string state, string? data)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_WhatsApp_BotState SET State = @State, Data = @Data, UpdatedOn = SYSDATETIME() WHERE PhoneNumber = @Phone;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.WN_WhatsApp_BotState (PhoneNumber, State, Data) VALUES (@Phone, @State, @Data);", c);
            cmd.Parameters.AddWithValue("@Phone", phone);
            cmd.Parameters.AddWithValue("@State", state);
            cmd.Parameters.AddWithValue("@Data", (object?)data ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<(List<WhatsAppConversationDto> Items, int Total)> GetConversationsAsync(int page, int limit, string? search)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand($@"
                SELECT {ConversationSelect}, COUNT(*) OVER () AS TotalCount
                FROM dbo.WN_WhatsApp_Conversations cv WITH (NOLOCK)
                JOIN dbo.WN_WhatsApp_Contacts ct WITH (NOLOCK) ON ct.Id = cv.ContactId
                WHERE (@Like IS NULL OR ct.PhoneNumber LIKE @Like OR ct.Name LIKE @Like OR ct.Tags LIKE @Like OR cv.LastMessageText LIKE @Like)
                ORDER BY CASE WHEN cv.UnreadCount > 0 THEN 0 ELSE 1 END, cv.LastMessageAt DESC, cv.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;", c);
            cmd.Parameters.AddWithValue("@Like", search == null ? DBNull.Value : "%" + search + "%");
            cmd.Parameters.AddWithValue("@Offset", (page - 1) * limit);
            cmd.Parameters.AddWithValue("@Limit", limit);
            var items = new List<WhatsAppConversationDto>();
            int total = 0;
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) { items.Add(MapConversation(r)); total = Convert.ToInt32(r["TotalCount"]); }
            return (items, total);
        }

        public Task<WhatsAppConversationDto?> GetConversationAsync(int id) =>
            GetOneConversationAsync("cv.Id = @Key", id);

        public Task<WhatsAppConversationDto?> GetConversationByPhoneAsync(string phone) =>
            GetOneConversationAsync("ct.PhoneNumber = @Key", phone);

        private async Task<WhatsAppConversationDto?> GetOneConversationAsync(string where, object key)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand($@"
                SELECT {ConversationSelect}
                FROM dbo.WN_WhatsApp_Conversations cv WITH (NOLOCK)
                JOIN dbo.WN_WhatsApp_Contacts ct WITH (NOLOCK) ON ct.Id = cv.ContactId
                WHERE {where};", c);
            cmd.Parameters.AddWithValue("@Key", key);
            await using var r = await cmd.ExecuteReaderAsync();
            return await r.ReadAsync() ? MapConversation(r) : null;
        }

        public async Task<List<WhatsAppMessageDto>> GetMessagesAsync(int conversationId, long? beforeId, int limit)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT TOP (@Limit) Id, ConversationId, Direction, MessageType, MessageText, SentByUserId, CreatedOn
                FROM dbo.WN_WhatsApp_Messages WITH (NOLOCK)
                WHERE ConversationId = @ConversationId AND (@BeforeId IS NULL OR Id < @BeforeId)
                ORDER BY Id DESC;", c);
            cmd.Parameters.AddWithValue("@Limit", limit);
            cmd.Parameters.AddWithValue("@ConversationId", conversationId);
            cmd.Parameters.AddWithValue("@BeforeId", (object?)beforeId ?? DBNull.Value);
            var list = new List<WhatsAppMessageDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new WhatsAppMessageDto
                {
                    Id = r.GetInt64(0),
                    ConversationId = r.GetInt32(1),
                    Direction = r.GetString(2),
                    MessageType = r.GetString(3),
                    MessageText = r.IsDBNull(4) ? null : r.GetString(4),
                    SentByUserId = r.IsDBNull(5) ? null : r.GetInt32(5),
                    CreatedOn = r.GetDateTime(6)
                });
            }
            list.Reverse(); // oldest first for the chat view
            return list;
        }

        public async Task MarkReadAsync(int conversationId)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand("UPDATE dbo.WN_WhatsApp_Conversations SET UnreadCount = 0 WHERE Id = @Id AND UnreadCount <> 0;", c);
            cmd.Parameters.AddWithValue("@Id", conversationId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateContactMetaAsync(int contactId, string? name, string? tags, string? adminNotes)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE dbo.WN_WhatsApp_Contacts
                SET Name = COALESCE(NULLIF(@Name, N''), Name), Tags = @Tags, AdminNotes = @Notes, UpdatedOn = SYSDATETIME()
                WHERE Id = @Id;", c);
            cmd.Parameters.AddWithValue("@Id", contactId);
            cmd.Parameters.AddWithValue("@Name", (object?)name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Tags", string.IsNullOrWhiteSpace(tags) ? DBNull.Value : tags);
            cmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(adminNotes) ? DBNull.Value : adminNotes);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<WhatsAppConversationDto>> GetBroadcastTargetsAsync(string? tag)
        {
            await using var c = await OpenAsync();
            await using var cmd = new SqlCommand($@"
                SELECT {ConversationSelect}
                FROM dbo.WN_WhatsApp_Conversations cv WITH (NOLOCK)
                JOIN dbo.WN_WhatsApp_Contacts ct WITH (NOLOCK) ON ct.Id = cv.ContactId
                WHERE @Tag IS NULL OR (N',' + REPLACE(LOWER(ISNULL(ct.Tags, N'')), N' ', N'') + N',') LIKE N'%,' + REPLACE(LOWER(@Tag), N' ', N'') + N',%'
                ORDER BY cv.Id;", c);
            cmd.Parameters.AddWithValue("@Tag", (object?)tag ?? DBNull.Value);
            var list = new List<WhatsAppConversationDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(MapConversation(r));
            return list;
        }

        public async Task<List<(int ConversationId, DateTime LastMessageAt)>> GetUnreadConversationsAsync()
        {
            var list = new List<(int, DateTime)>();
            try
            {
                await using var c = await OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT Id, ISNULL(LastMessageAt, CreatedOn) FROM dbo.WN_WhatsApp_Conversations WITH (NOLOCK) WHERE UnreadCount > 0;", c);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add((r.GetInt32(0), r.GetDateTime(1)));
            }
            catch (SqlException ex) when (ex.Number == 208) { } // tables not created yet
            return list;
        }

        private static WhatsAppConversationDto MapConversation(SqlDataReader r)
        {
            string? S(string k) => r[k] is DBNull ? null : r[k].ToString();
            DateTime? D(string k) => r[k] is DateTime d ? d : null;
            return new WhatsAppConversationDto
            {
                Id = Convert.ToInt32(r["Id"]),
                ContactId = Convert.ToInt32(r["ContactId"]),
                PhoneNumber = S("PhoneNumber") ?? "",
                Name = S("Name"),
                Tags = S("Tags"),
                AdminNotes = S("AdminNotes"),
                Status = S("Status") ?? "open",
                LastMessageAt = D("LastMessageAt"),
                LastMessageText = S("LastMessageText"),
                LastIncomingAt = D("LastIncomingAt"),
                UnreadCount = Convert.ToInt32(r["UnreadCount"]),
                InReplyWindow = r["InReplyWindow"] is bool b && b
            };
        }
    }
}

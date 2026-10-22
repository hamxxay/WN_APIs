using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.OneBill
{
    /// <summary>
    /// Sends invoices to EzPay: POST {OneBill:BaseUrl}/{LoginPath} with { email: Username, password: Password } answers a
    /// token, then POST {OneBill:BaseUrl}/{AddInvoicePath} with Authorization: Bearer {token} creates the invoice.
    /// Settings (section "OneBill"): Enabled, BaseUrl, Username, Password, LoginPath (api/v1/loginUser),
    /// AddInvoicePath (api/v1/AddPayment/Student), TimeoutSeconds (30), TokenReuseMinutes (50), MaxAttempts (5),
    /// FallbackEmail / FallbackPhone (sent when the customer has none on file).
    /// Which invoices, the ref and the One Bill number all come from the database (WN_OneBill_* procedures).
    /// Singleton: the EzPay token is reused between invoices.
    /// </summary>
    public class OneBillService : IOneBillService
    {
        private const string HttpClientName = "OneBill";
        private static readonly Regex PhoneDigits = new(@"^\+?[0-9]{7,15}$", RegexOptions.Compiled);

        private readonly IHttpClientFactory _http;
        private readonly IConfiguration _config;
        private readonly ILogger<OneBillService> _logger;
        private readonly SemaphoreSlim _loginLock = new(1, 1);
        private string? _token;
        private DateTime _tokenUntilUtc;

        public OneBillService(IHttpClientFactory http, IConfiguration config, ILogger<OneBillService> logger)
        {
            _http = http;
            _config = config;
            _logger = logger;
        }

        private string BaseUrl => (_config["OneBill:BaseUrl"] ?? "").Trim().TrimEnd('/');
        private string Username => (_config["OneBill:Username"] ?? "").Trim();
        private string Password => _config["OneBill:Password"] ?? "";
        private string Url(string key, string fallback) => BaseUrl + "/" + (string.IsNullOrWhiteSpace(_config[key]) ? fallback : _config[key]!.Trim().TrimStart('/'));
        private int MaxAttempts => Math.Max(1, _config.GetValue("OneBill:MaxAttempts", 5));
        public bool IsConfigured => _config.GetValue("OneBill:Enabled", false) && BaseUrl.Length > 0 && Username.Length > 0 && Password.Length > 0;

        public async Task<string?> EnsureAsync(int invoiceId, CancellationToken ct = default)
        {
            if (invoiceId <= 0) return null;
            try
            {
                if (!IsConfigured) return await GetNumberAsync(invoiceId, ct);

                ClaimedInvoice? claim;
                await using (var conn = await OpenAsync(ct))
                {
                    claim = await ClaimAsync(conn, invoiceId, ct);
                }
                if (claim is null) return null;
                if (claim.Outcome == "Done") return claim.OneBillNumber;
                if (claim.Outcome != "Claimed") return null;

                var (ok, ezPayId, tranAuthId, error) = await AddInvoiceAsync(claim, ct);
                await using (var conn = await OpenAsync(ct))
                {
                    await SetResultAsync(conn, invoiceId, ok, ezPayId, tranAuthId, error, ct);
                }
                if (ok)
                {
                    _logger.LogInformation("One Bill: invoice {InvoiceNumber} ({InvoiceId}) created on EzPay, One Bill number {OneBillNumber}.",
                        claim.InvoiceNumber, invoiceId, claim.OneBillNumber);
                    return claim.OneBillNumber;
                }
                _logger.LogWarning("One Bill: invoice {InvoiceNumber} ({InvoiceId}) was not created on EzPay: {Error}",
                    claim.InvoiceNumber, invoiceId, error);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "One Bill: sending invoice {InvoiceId} failed.", invoiceId);
                return null;
            }
        }

        public async Task<string?> GetNumberAsync(int invoiceId, CancellationToken ct = default)
        {
            if (invoiceId <= 0) return null;
            try
            {
                await using var conn = await OpenAsync(ct);
                using var cmd = new SqlCommand("dbo.WN_OneBill_GetByInvoice", conn) { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId;
                using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) return null;
                var i = r.GetOrdinal("OneBillNumber");
                return r.IsDBNull(i) ? null : r.GetString(i);
            }
            catch (SqlException ex) when (ex.Number == 2812) // procedure missing: OB_01_one_bill not run yet
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "One Bill: reading the One Bill number of invoice {InvoiceId} failed.", invoiceId);
                return null;
            }
        }

        public async Task<IReadOnlyList<int>> GetPendingAsync(int top, CancellationToken ct = default)
        {
            var ids = new List<int>();
            await using var conn = await OpenAsync(ct);
            using var cmd = new SqlCommand("dbo.WN_OneBill_GetPending", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Top", SqlDbType.Int).Value = Math.Max(1, top);
            cmd.Parameters.Add("@MaxAttempts", SqlDbType.Int).Value = MaxAttempts;
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetInt32(0));
            return ids;
        }

        public async Task<OneBillPaymentResult> RecordPaymentAsync(OneBillPaymentNotice notice, CancellationToken ct = default)
        {
            // CancellationToken.None from here on: once started, the notification is recorded completely or not at all.
            await using var conn = await OpenAsync(ct);
            // Transaction here as well as inside dbo.WN_OneBill_RecordPayment: the notification row, the invoice payment and
            // the WN_Payments row are committed only after the procedure's answer has been read; any error rolls all back.
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, CancellationToken.None);
            try
            {
                OneBillPaymentResult result;
                // dbo.WN_OneBill_RecordPayment (OB_04): @StatusCode 200 recorded, 201 same notification already recorded,
                // 202 invoice already fully paid, 104 no invoice with this bill number, 105 bad amount; an error = 103 (thrown).
                using (var cmd = new SqlCommand("dbo.WN_OneBill_RecordPayment", conn, tx) { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.Add("@ezpayRefNo", SqlDbType.NVarChar, 30).Value = notice.BillNo;
                    var amount = cmd.Parameters.Add("@ezpayRcvdAmount", SqlDbType.Decimal);
                    amount.Precision = 18;
                    amount.Scale = 2;
                    amount.Value = Math.Round(notice.Amount, 2);
                    cmd.Parameters.Add("@ezpayRcvdDate", SqlDbType.Date).Value = (object?)notice.ReceivedDate?.Date ?? DBNull.Value;
                    cmd.Parameters.Add("@ezpayRcvdTime", SqlDbType.Time).Value = (object?)notice.ReceivedTime ?? DBNull.Value;
                    cmd.Parameters.Add("@ezpayRcvdVia", SqlDbType.NVarChar, 50).Value = (object?)Cut(notice.ReceivedVia, 50) ?? DBNull.Value;
                    var statusCode = cmd.Parameters.Add("@StatusCode", SqlDbType.Int);
                    statusCode.Direction = ParameterDirection.Output;
                    cmd.Parameters.Add("@RawBody", SqlDbType.NVarChar, -1).Value = (object?)notice.RawBody ?? DBNull.Value;
                    cmd.Parameters.Add("@RemoteIp", SqlDbType.VarChar, 64).Value = (object?)Cut(notice.RemoteIp, 64) ?? DBNull.Value;

                    // The row the procedure answers (for the log); the output parameter is read once the reader is closed.
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    using (var r = await cmd.ExecuteReaderAsync(CancellationToken.None))
                    {
                        if (await r.ReadAsync(CancellationToken.None))
                            for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                        while (await r.NextResultAsync(CancellationToken.None)) { }
                    }

                    var code = statusCode.Value is int c ? c : (int?)null;
                    object? Val(string name) => row.TryGetValue(name, out var v) ? v : null;
                    result = new OneBillPaymentResult
                    {
                        NotificationId = Convert.ToInt32(Val("NotificationId") ?? 0, CultureInfo.InvariantCulture),
                        Outcome = code switch
                        {
                            200 => "Applied",
                            201 => "Duplicate",
                            202 => "AlreadyPaid",
                            104 => "Unmatched",
                            105 => "Invalid",
                            _ => throw new InvalidOperationException($"WN_OneBill_RecordPayment answered status code {code?.ToString(CultureInfo.InvariantCulture) ?? "none"}.")
                        },
                        Message = Convert.ToString(Val("Message"), CultureInfo.InvariantCulture),
                        InvoiceId = Val("InvoiceId") is { } id ? Convert.ToInt32(id, CultureInfo.InvariantCulture) : null,
                        InvoiceNumber = Convert.ToString(Val("InvoiceNumber"), CultureInfo.InvariantCulture),
                        PaidTotal = Val("PaidTotal") is { } paid ? Convert.ToDecimal(paid, CultureInfo.InvariantCulture) : null,
                        StatusId = Val("StatusId") is { } st ? Convert.ToInt32(st, CultureInfo.InvariantCulture) : null,
                    };
                }
                if (result.Outcome == "Applied" && result.InvoiceId is null)
                    throw new InvalidOperationException("WN_OneBill_RecordPayment answered 200 without the invoice.");

                await tx.CommitAsync(CancellationToken.None);
                return result;
            }
            catch
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch (InvalidOperationException) { /* already rolled back by XACT_ABORT */ }
                throw;
            }
        }

        // ---- database ---------------------------------------------------------------------------------------------

        private async Task<SqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync(ct);
            return conn;
        }

        private async Task<ClaimedInvoice?> ClaimAsync(SqlConnection conn, int invoiceId, CancellationToken ct)
        {
            using var cmd = new SqlCommand("dbo.WN_OneBill_ClaimInvoice", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId;
            cmd.Parameters.Add("@MaxAttempts", SqlDbType.Int).Value = MaxAttempts;
            using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;

            string? Str(string name) => Has(r, name) && !r.IsDBNull(r.GetOrdinal(name)) ? Convert.ToString(r.GetValue(r.GetOrdinal(name)), CultureInfo.InvariantCulture) : null;
            DateTime? Date(string name) => Has(r, name) && !r.IsDBNull(r.GetOrdinal(name)) ? r.GetDateTime(r.GetOrdinal(name)) : null;

            return new ClaimedInvoice
            {
                Outcome = Str("Outcome") ?? "",
                InvoiceId = invoiceId,
                InvoiceNumber = Str("InvoiceNumber") ?? "",
                OneBillRef = Str("OneBillRef") ?? "",
                OneBillNumber = Str("OneBillNumber"),
                Amount = Has(r, "Amount") && !r.IsDBNull(r.GetOrdinal("Amount")) ? r.GetDecimal(r.GetOrdinal("Amount")) : 0m,
                DueOn = Date("DueOn"),
                BillingPeriodStart = Date("BillingPeriodStart"),
                BillingPeriodEnd = Date("BillingPeriodEnd"),
                RegistrationNumber = Str("RegistrationNumber") ?? "",
                CustomerName = Str("CustomerName") ?? "",
                Email = Str("Email"),
                Phone = Str("Phone"),
                LocationName = Str("LocationName"),
            };
        }

        private static async Task SetResultAsync(SqlConnection conn, int invoiceId, bool ok, long? ezPayId, long? tranAuthId, string? error, CancellationToken ct)
        {
            using var cmd = new SqlCommand("dbo.WN_OneBill_SetResult", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@InvoiceId", SqlDbType.Int).Value = invoiceId;
            cmd.Parameters.Add("@Success", SqlDbType.Bit).Value = ok;
            cmd.Parameters.Add("@EzPayInvoiceId", SqlDbType.BigInt).Value = (object?)ezPayId ?? DBNull.Value;
            cmd.Parameters.Add("@EzPayTranAuthId", SqlDbType.BigInt).Value = (object?)tranAuthId ?? DBNull.Value;
            cmd.Parameters.Add("@Error", SqlDbType.NVarChar, 500).Value = (object?)error ?? DBNull.Value;
            // CancellationToken.None: once EzPay has answered, the answer is always saved.
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        private static bool Has(SqlDataReader r, string name)
        {
            for (var i = 0; i < r.FieldCount; i++)
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---- EzPay ------------------------------------------------------------------------------------------------

        private async Task<(bool Ok, long? EzPayId, long? TranAuthId, string? Error)> AddInvoiceAsync(ClaimedInvoice inv, CancellationToken ct)
        {
            if (inv.Amount <= 0) return (false, null, null, "Nothing to pay on this invoice.");
            if (inv.DueOn is null) return (false, null, null, "The invoice has no due date.");

            var email = ValidEmail(inv.Email) ?? ValidEmail(_config["OneBill:FallbackEmail"]);
            var phone = ValidPhone(inv.Phone) ?? ValidPhone(_config["OneBill:FallbackPhone"]);
            if (email is null || phone is null)
                return (false, null, null, "The customer has no valid email / phone and OneBill:FallbackEmail / FallbackPhone is not set.");

            var period = inv.BillingPeriodStart is { } s && inv.BillingPeriodEnd is { } e ? $"{s:dd MMM yyyy} - {e:dd MMM yyyy}" : null;
            var body = new Dictionary<string, object?>
            {
                ["registration_number"] = Cut(inv.RegistrationNumber, 50),
                ["concat_vendor_invoice_one_bill_ref"] = inv.OneBillRef,
                ["first_name"] = Cut(inv.CustomerName, 100),
                ["email"] = email,
                ["phone"] = phone,
                ["amount"] = Math.Round(inv.Amount, 2),
                ["amount_after_due_date"] = Math.Round(inv.Amount, 2), // no late fee
                ["due_date"] = inv.DueOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["description"] = Cut($"WorkNest invoice {inv.InvoiceNumber}", 250),
                ["misc_1"] = Cut(inv.InvoiceNumber, 250),
                ["misc_2"] = Cut(inv.LocationName, 250),
                ["misc_3"] = Cut(period, 250),
            };

            try
            {
                var token = await GetTokenAsync(force: false, ct);
                var (status, json) = await PostInvoiceAsync(body, token, ct);
                if (status == HttpStatusCode.Unauthorized)
                {
                    token = await GetTokenAsync(force: true, ct);
                    (status, json) = await PostInvoiceAsync(body, token, ct);
                }

                // EzPay: { "status": 1, "message": "Invoice created successfully!", "data": { "id", "tran_auth_id", ... } };
                // a refusal can come as HTTP 200 with "status": 0.
                var root = json?.RootElement;
                var apiStatus = root is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("status", out var st) && st.TryGetInt32(out var sv) ? sv : (int?)null;
                var message = root is { ValueKind: JsonValueKind.Object } m && m.TryGetProperty("message", out var mv) ? mv.ToString() : null;
                if ((int)status is >= 200 and < 300 && apiStatus == 1)
                {
                    long? id = null, tranAuth = null;
                    if (root!.Value.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        id = data.TryGetProperty("id", out var idv) && idv.TryGetInt64(out var idl) ? idl : null;
                        tranAuth = data.TryGetProperty("tran_auth_id", out var tv) && tv.TryGetInt64(out var tl) ? tl : null;
                    }
                    return (true, id, tranAuth, null);
                }
                var detail = root is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("data", out var dd) && dd.ValueKind == JsonValueKind.Object ? " " + dd.GetRawText() : "";
                return (false, null, null, Cut($"EzPay answered HTTP {(int)status}: {message ?? "no message"}{detail}", 500));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return (false, null, null, Cut("Could not reach EzPay: " + ex.Message, 500));
            }
        }

        private async Task<(HttpStatusCode Status, JsonDocument? Json)> PostInvoiceAsync(Dictionary<string, object?> body, string token, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Url("OneBill:AddInvoicePath", "api/v1/AddPayment/Student")) { Content = JsonContent.Create(body) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await Client().SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            return (res.StatusCode, TryParse(text));
        }

        private async Task<string> GetTokenAsync(bool force, CancellationToken ct)
        {
            if (!force && _token is not null && DateTime.UtcNow < _tokenUntilUtc) return _token;
            await _loginLock.WaitAsync(ct);
            try
            {
                if (!force && _token is not null && DateTime.UtcNow < _tokenUntilUtc) return _token;
                // EzPay loginUser, always with OneBill:Username / OneBill:Password from appsettings:
                // { "status": 200, "message": "Success!", "data": { "token": "..." } }.
                using var req = new HttpRequestMessage(HttpMethod.Post, Url("OneBill:LoginPath", "api/v1/loginUser"))
                {
                    Content = JsonContent.Create(new { email = Username, password = Password })
                };
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var res = await Client().SendAsync(req, ct);
                var json = TryParse(await res.Content.ReadAsStringAsync(ct));
                var root = json?.RootElement;
                var token = root is { ValueKind: JsonValueKind.Object } r
                            && r.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                            && data.TryGetProperty("token", out var t) ? t.GetString() : null;
                if (!res.IsSuccessStatusCode || string.IsNullOrWhiteSpace(token))
                {
                    var message = root is { ValueKind: JsonValueKind.Object } m && m.TryGetProperty("message", out var mv) ? mv.ToString() : null;
                    throw new InvalidOperationException($"EzPay login failed (HTTP {(int)res.StatusCode}): {message ?? "no token in the answer"}.");
                }
                _token = token;
                _tokenUntilUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _config.GetValue("OneBill:TokenReuseMinutes", 50)));
                return token;
            }
            finally
            {
                _loginLock.Release();
            }
        }

        private HttpClient Client()
        {
            var client = _http.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.GetValue("OneBill:TimeoutSeconds", 30), 5, 120));
            return client;
        }

        private static JsonDocument? TryParse(string text)
        {
            try { return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text); }
            catch (JsonException) { return null; }
        }

        private static string? ValidEmail(string? email)
        {
            email = email?.Trim();
            return !string.IsNullOrEmpty(email) && email.Length <= 150 && System.Net.Mail.MailAddress.TryCreate(email, out _) ? email : null;
        }

        /// <summary>Digits (and a leading +) only, as EzPay takes them: "0300-123 4567" becomes "03001234567".</summary>
        private static string? ValidPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var trimmed = phone.Trim();
            var cleaned = (trimmed.StartsWith('+') ? "+" : "") + new string(trimmed.Where(char.IsAsciiDigit).ToArray());
            return PhoneDigits.IsMatch(cleaned) ? cleaned : null;
        }

        private static string? Cut(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];

        private sealed class ClaimedInvoice
        {
            public string Outcome { get; init; } = "";
            public int InvoiceId { get; init; }
            public string InvoiceNumber { get; init; } = "";
            public string OneBillRef { get; init; } = "";
            public string? OneBillNumber { get; init; }
            public decimal Amount { get; init; }
            public DateTime? DueOn { get; init; }
            public DateTime? BillingPeriodStart { get; init; }
            public DateTime? BillingPeriodEnd { get; init; }
            public string RegistrationNumber { get; init; } = "";
            public string CustomerName { get; init; } = "";
            public string? Email { get; init; }
            public string? Phone { get; init; }
            public string? LocationName { get; init; }
        }
    }
}

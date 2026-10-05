using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Hikvision
{
    /// <summary>
    /// ISAPI client for Hikvision access terminals (DS-K1T320 / MinMoe family).
    /// Port of HIK_Access-Control backend/src/isapi.js: Digest auth, WAN failover to Host2,
    /// and the same request/response shapes for persons, cards, fingerprints and faces.
    /// </summary>
    public class HikIsapiClient : IHikIsapiClient
    {
        // One HttpClient per device+credentials so the Digest challenge and keep-alive connections are reused.
        private static readonly ConcurrentDictionary<string, HttpClient> Clients = new();

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        // Request gates, same limits as HIK isapi.js: the fleet sits behind one public IP and the office
        // router blackholes the source on bursts of parallel connections.
        private const int MaxPerHost = 2;
        private static readonly SemaphoreSlim FleetGate = new(6, 6);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> HostGates = new(StringComparer.OrdinalIgnoreCase);

        // WN-030: how long to wait for a TCP connection before trying the device's Host2 link.
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

        private static readonly Dictionary<string, string> HikMessages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["employeeNoAlreadyExist"] = "That employee number already exists on this machine.",
            ["employeeNoNotExist"] = "That user does not exist on this machine.",
            ["userExceedLimit"] = "This machine has reached its user capacity limit.",
            ["cardNoAlreadyExist"] = "This card is already assigned to another user on this machine.",
            ["duplicateCardNo"] = "This card is already registered on this machine.",
            ["cardNoExist"] = "This card is already assigned on this machine.",
            ["cardNumOverLimit"] = "This machine has reached its maximum card limit.",
            ["cardNumFull"] = "This machine has reached its maximum card limit.",
            ["fingerPrintDataExist"] = "This fingerprint is already enrolled on this machine.",
            ["duplicateFingerPrint"] = "This fingerprint is already enrolled for another user.",
            ["fingerPrintNumOverLimit"] = "This machine has reached its fingerprint capacity limit.",
            ["fingerPrintNumFull"] = "This machine has reached its fingerprint capacity limit.",
            ["faceDataExist"] = "A face is already enrolled for this user.",
            ["duplicateFace"] = "This face is already enrolled for another user.",
            ["faceNumOverLimit"] = "This machine has reached its face recognition capacity limit.",
            ["faceNumFull"] = "This machine has reached its face recognition capacity limit.",
            ["noFaceDetected"] = "No clear face was detected. Ensure proper lighting and face the camera directly.",
            ["faceQualityTooLow"] = "Face image quality is too low. Please look directly into the camera.",
            ["deviceError"] = "Nothing was presented at the reader, or the device reported an error. Please try again.",
            ["deviceBusy"] = "The machine is busy with another operation. Please wait 15 seconds and try again.",
            ["timeOut"] = "Operation timed out — no credential was presented at the reader in time.",
            ["methodNotAllowed"] = "This operation is not supported by this machine firmware.",
            ["notSupport"] = "This machine model does not support that operation.",
            ["badJsonContent"] = "The machine rejected the request parameters.",
            ["badXmlContent"] = "The machine rejected the request format.",
            ["invalidContent"] = "The machine rejected the payload content.",
            ["notActivated"] = "The terminal is not activated.",
            ["riskPassword"] = "The terminal reports a weak or risky password.",
            ["permissionDenied"] = "Terminal access denied (insufficient privileges)."
        };

        private readonly ILogger<HikIsapiClient> _logger;

        public HikIsapiClient(ILogger<HikIsapiClient> logger)
        {
            _logger = logger;
        }

        // ---- Person (UserInfo) ---------------------------------------------------

        public async Task<HikIsapiResult> UpsertPersonAsync(HikDeviceConnection device, string employeeNo, string name, string validBegin, string validEnd, bool enabled = true)
        {
            var record = new JsonObject
            {
                ["UserInfo"] = new JsonObject
                {
                    ["employeeNo"] = employeeNo,
                    ["name"] = name,
                    ["userType"] = "normal",
                    ["Valid"] = new JsonObject
                    {
                        ["enable"] = enabled,
                        ["beginTime"] = validBegin,
                        ["endTime"] = validEnd,
                        ["timeType"] = "local"
                    },
                    ["localUIRight"] = false,
                    ["doorRight"] = "1",
                    ["RightPlan"] = new JsonArray(new JsonObject { ["doorNo"] = 1, ["planTemplateNo"] = "1" })
                }
            };

            // Add uses POST /Record; Modify uses PUT /Modify (POST → methodNotAllowed).
            var add = Interpret(await SendJsonAsync(device, HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Record?format=json", record));
            if (add.Ok) return add;
            if (!string.Equals(add.SubStatusCode, "employeeNoAlreadyExist", StringComparison.OrdinalIgnoreCase)) return add;

            // Only update the existing record when it is the same person — never overwrite someone else's number.
            var existingName = await GetPersonNameAsync(device, employeeNo);
            if (existingName == null)
                return HikIsapiResult.Fail($"Employee #{employeeNo} already exists on {device.Name} but could not be verified.", "employeeNoAlreadyExist");
            if (!string.Equals(existingName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                return HikIsapiResult.Fail($"Employee #{employeeNo} is already used by \"{existingName}\" on {device.Name}.", "employeeNoAlreadyExist");

            return Interpret(await SendJsonAsync(device, HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Modify?format=json", record));
        }

        private async Task<string?> GetPersonNameAsync(HikDeviceConnection device, string employeeNo)
        {
            var (person, _) = await GetPersonAsync(device, employeeNo);
            return person?["name"]?.ToString();
        }

        /// <summary>UserInfo of one person on the device (null when not found), plus the raw call result.</summary>
        private async Task<(JsonNode? Person, HikIsapiResult Result)> GetPersonAsync(HikDeviceConnection device, string employeeNo)
        {
            var body = new JsonObject
            {
                ["UserInfoSearchCond"] = new JsonObject
                {
                    ["searchID"] = "1",
                    ["searchResultPosition"] = 0,
                    ["maxResults"] = 1,
                    ["EmployeeNoList"] = new JsonArray(new JsonObject { ["employeeNo"] = employeeNo })
                }
            };
            var res = await SendJsonAsync(device, HttpMethod.Post, "/ISAPI/AccessControl/UserInfo/Search?format=json", body);
            if (res.Error != null || res.TimedOut) return (null, Interpret(res));

            // A busy / error / rejected reply is "unknown", never "not on this machine".
            var statusCode = res.Json?["statusCode"]?.ToString();
            if (!res.IsSuccessStatus || (statusCode != null && statusCode != "1") || res.Json?["UserInfoSearch"] == null)
            {
                var failed = Interpret(res);
                return (null, failed.Ok ? HikIsapiResult.Fail($"{device.Name} returned an unexpected reply to a user lookup.") : failed);
            }
            return (res.Json["UserInfoSearch"]?["UserInfo"]?[0], HikIsapiResult.Success());
        }

        public async Task<bool?> PersonExistsAsync(HikDeviceConnection device, string employeeNo)
        {
            var (person, lookup) = await GetPersonAsync(device, employeeNo);
            if (!lookup.Ok) return null; // unknown (offline, busy, error) — never treated as "not there"
            return person != null;
        }

        public async Task<bool?> GetPersonEnabledAsync(HikDeviceConnection device, string employeeNo)
        {
            var (person, lookup) = await GetPersonAsync(device, employeeNo);
            if (!lookup.Ok || person == null) return null;
            return !string.Equals(person["Valid"]?["enable"]?.ToString(), "false", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<HikIsapiResult> DeletePersonAsync(HikDeviceConnection device, string employeeNo)
        {
            var body = new JsonObject
            {
                ["UserInfoDelCond"] = new JsonObject
                {
                    ["EmployeeNoList"] = new JsonArray(new JsonObject { ["employeeNo"] = employeeNo })
                }
            };
            return Interpret(await SendJsonAsync(device, HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Delete?format=json", body));
        }

        /// <summary>
        /// Block / unblock while keeping the person's cards, fingerprints and face — same as HIK's
        /// queued 'block' / 'unblock' ops: re-save the existing UserInfo with Valid.enable toggled.
        /// </summary>
        public async Task<HikIsapiResult> SetPersonEnabledAsync(HikDeviceConnection device, string employeeNo, bool enabled)
        {
            var (person, lookup) = await GetPersonAsync(device, employeeNo);
            if (!lookup.Ok) return lookup;
            if (person == null) return HikIsapiResult.Fail($"User #{employeeNo} is not on {device.Name}.", "employeeNoNotExist");

            var record = new JsonObject
            {
                ["UserInfo"] = new JsonObject
                {
                    ["employeeNo"] = employeeNo,
                    ["name"] = person["name"]?.ToString() ?? $"User {employeeNo}",
                    ["userType"] = person["userType"]?.ToString() ?? "normal",
                    ["Valid"] = new JsonObject
                    {
                        ["enable"] = enabled,
                        ["beginTime"] = person["Valid"]?["beginTime"]?.ToString() ?? "2020-01-01T00:00:00",
                        ["endTime"] = person["Valid"]?["endTime"]?.ToString() ?? "2037-12-31T23:59:59",
                        ["timeType"] = "local"
                    },
                    ["localUIRight"] = string.Equals(person["localUIRight"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
                    ["doorRight"] = person["doorRight"]?.ToString() ?? "1",
                    ["RightPlan"] = new JsonArray(new JsonObject { ["doorNo"] = 1, ["planTemplateNo"] = "1" })
                }
            };
            return Interpret(await SendJsonAsync(device, HttpMethod.Put, "/ISAPI/AccessControl/UserInfo/Modify?format=json", record));
        }

        // ---- Card -----------------------------------------------------------------

        public async Task<HikIsapiResult> AddCardAsync(HikDeviceConnection device, string employeeNo, string cardNo)
        {
            var body = new JsonObject
            {
                ["CardInfo"] = new JsonObject
                {
                    ["employeeNo"] = employeeNo,
                    ["cardNo"] = cardNo,
                    ["cardType"] = "normalCard"
                }
            };
            return Interpret(await SendJsonAsync(device, HttpMethod.Post, "/ISAPI/AccessControl/CardInfo/Record?format=json", body));
        }

        /// <summary>
        /// Ask the terminal to read a card at its reader (long-poll per round), then return the card number.
        /// </summary>
        public async Task<HikCardCaptureResult> CaptureCardAsync(HikDeviceConnection device)
        {
            for (var round = 0; round < 3; round++)
            {
                var res = await SendAsync(device, HttpMethod.Get, "/ISAPI/AccessControl/CaptureCardInfo?format=json", null, TimeSpan.FromSeconds(15));
                if (res.TimedOut) continue;
                if (res.Error != null) return new HikCardCaptureResult { Ok = false, Error = res.Error, Unreachable = res.Unreachable };

                var j = res.Json;
                var cardNo = j?["CardInfo"]?["cardNo"]?.ToString()
                             ?? j?["CaptureCardInfo"]?["cardNo"]?.ToString()
                             ?? j?["cardNo"]?.ToString()
                             ?? XmlTag(res.Text, "cardNo");
                if (!string.IsNullOrWhiteSpace(cardNo)) return new HikCardCaptureResult { Ok = true, CardNo = cardNo.Trim() };

                // "deviceError" here just means no card was presented in that round — keep waiting.
                var sub = j?["subStatusCode"]?.ToString() ?? XmlTag(res.Text, "subStatusCode");
                if (!string.IsNullOrEmpty(sub) && sub != "deviceError" && sub != "ok")
                    return new HikCardCaptureResult { Ok = false, Error = Describe(sub, j?["statusString"]?.ToString()), SubStatusCode = sub };
            }
            return new HikCardCaptureResult { Ok = false, Error = "No card was presented at the reader — tap the card and try again." };
        }

        // ---- Fingerprint ----------------------------------------------------------

        /// <summary>
        /// Prompt the terminal to capture a fingerprint from its own sensor. XML only (JSON → badXmlContent).
        /// </summary>
        public async Task<HikFingerprintCaptureResult> CaptureFingerprintAsync(HikDeviceConnection device, int fingerNo)
        {
            var xml = $"<CaptureFingerPrintCond><fingerNo>{fingerNo}</fingerNo></CaptureFingerPrintCond>";
            var res = await SendAsync(device, HttpMethod.Post, "/ISAPI/AccessControl/CaptureFingerPrint",
                () => new StringContent(xml, Encoding.UTF8, "application/xml"), TimeSpan.FromSeconds(45));

            if (res.TimedOut) return new HikFingerprintCaptureResult { Ok = false, Error = "No finger was placed on the sensor in time — try again." };
            if (res.Error != null) return new HikFingerprintCaptureResult { Ok = false, Error = res.Error, Unreachable = res.Unreachable };

            var fingerData = XmlTag(res.Text, "fingerData") ?? res.Json?["CaptureFingerPrint"]?["fingerData"]?.ToString();
            if (!string.IsNullOrEmpty(fingerData))
                return new HikFingerprintCaptureResult { Ok = true, FingerData = fingerData, Quality = XmlTag(res.Text, "fingerPrintQuality") };

            var failed = Interpret(res);
            return new HikFingerprintCaptureResult { Ok = false, Error = failed.Error ?? "Fingerprint capture failed.", SubStatusCode = failed.SubStatusCode };
        }

        /// <summary>
        /// Push an already-captured fingerprint template (base64) to the device.
        /// </summary>
        public async Task<HikIsapiResult> AddFingerprintAsync(HikDeviceConnection device, string employeeNo, string fingerData, int fingerNo)
        {
            var body = new JsonObject
            {
                ["FingerPrintCfg"] = new JsonObject
                {
                    ["employeeNo"] = employeeNo,
                    ["enableCardReader"] = new JsonArray(1),
                    ["fingerPrintID"] = fingerNo,
                    ["fingerType"] = "normalFP",
                    ["fingerData"] = fingerData
                }
            };
            var res = await SendJsonAsync(device, HttpMethod.Post, "/ISAPI/AccessControl/FingerPrint/SetUp?format=json", body);
            if (res.Error != null || res.TimedOut)
                return new HikIsapiResult { Ok = false, Error = res.Error ?? "The machine did not respond in time.", Unreachable = res.Unreachable || res.TimedOut };

            // SetUp reports per-reader status: cardReaderRecvStatus 1 = accepted, 5 = duplicate (errorMsg = other employee #).
            if (res.Json?["FingerPrintStatus"]?["StatusList"] is JsonArray list && list.Count > 0)
            {
                var statuses = list.Select(s => s?["cardReaderRecvStatus"]?.ToString() ?? "").ToList();
                if (res.IsSuccessStatus && statuses.All(s => s == "1")) return HikIsapiResult.Success();

                var dup = list.FirstOrDefault(s => s?["cardReaderRecvStatus"]?.ToString() == "5");
                if (dup != null)
                    return HikIsapiResult.Fail($"This fingerprint is already enrolled for employee #{dup["errorMsg"]}.");

                var first = statuses.FirstOrDefault(s => s != "1");
                return HikIsapiResult.Fail(first switch
                {
                    "2" => "Poor scan quality. Please press finger firmly on the sensor.",
                    "3" => "Terminal fingerprint capacity full.",
                    "4" => "Fingerprint already exists for this member.",
                    "6" => "Failed to write fingerprint template to card reader.",
                    _ => $"Fingerprint rejected by the machine (status {string.Join(",", statuses)})."
                });
            }
            return Interpret(res);
        }

        // ---- Face -----------------------------------------------------------------

        /// <summary>
        /// Prompt the terminal to capture a face with its own camera; returns the JPEG on success.
        /// </summary>
        public async Task<HikFaceCaptureResult> CaptureFaceAsync(HikDeviceConnection device)
        {
            const string xml = "<CaptureFaceDataCond version=\"2.0\" xmlns=\"http://www.isapi.org/ver20/XMLSchema\">" +
                               "<captureInfrared>false</captureInfrared><dataType>binary</dataType></CaptureFaceDataCond>";

            for (var round = 0; round < 4; round++)
            {
                var res = await SendAsync(device, HttpMethod.Post, "/ISAPI/AccessControl/CaptureFaceData",
                    () => new StringContent(xml, Encoding.UTF8, "application/xml"), TimeSpan.FromSeconds(20));
                if (res.TimedOut) continue;
                if (res.Error != null) return new HikFaceCaptureResult { Ok = false, Error = res.Error, Unreachable = res.Unreachable };

                // On success the body contains the raw JPEG (possibly inside multipart).
                var jpeg = ExtractJpeg(res.Body);
                if (jpeg != null) return new HikFaceCaptureResult { Ok = true, Jpeg = jpeg };

                var sub = XmlTag(res.Text, "subStatusCode");
                if (!string.IsNullOrEmpty(sub) && sub != "deviceError" && sub != "ok")
                    return new HikFaceCaptureResult { Ok = false, Error = Describe(sub, null), SubStatusCode = sub };
                // captureProgress 0 → nobody presented a face this round; keep waiting.
            }
            return new HikFaceCaptureResult { Ok = false, Error = "No face was captured — stand in front of the machine camera and try again." };
        }

        /// <summary>
        /// Upload a face JPEG to the access face library (FDID 1, blackFD) via multipart.
        /// </summary>
        public async Task<HikIsapiResult> AddFaceByImageAsync(HikDeviceConnection device, string employeeNo, byte[] jpeg)
        {
            var boundary = "----HikBoundary" + DateTime.UtcNow.Ticks;
            var meta = JsonSerializer.Serialize(new { faceLibType = "blackFD", FDID = "1", FPID = employeeNo });

            HttpContent Build()
            {
                using var ms = new MemoryStream();
                void W(string s) { var b = Encoding.UTF8.GetBytes(s); ms.Write(b, 0, b.Length); }
                W($"--{boundary}\r\n");
                W("Content-Disposition: form-data; name=\"FaceDataRecord\";\r\nContent-Type: application/json\r\n\r\n");
                W(meta + "\r\n");
                W($"--{boundary}\r\n");
                W("Content-Disposition: form-data; name=\"FaceImage\"; filename=\"face.jpg\";\r\nContent-Type: image/jpeg\r\n\r\n");
                ms.Write(jpeg, 0, jpeg.Length);
                W($"\r\n--{boundary}--\r\n");
                var content = new ByteArrayContent(ms.ToArray());
                content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
                return content;
            }

            return Interpret(await SendAsync(device, HttpMethod.Post, "/ISAPI/Intelligent/FDLib/FaceDataRecord?format=json", Build, TimeSpan.FromSeconds(30)));
        }

        // ---- Transport ------------------------------------------------------------

        private sealed class IsapiResponse
        {
            public int Status { get; init; }
            public byte[] Body { get; init; } = Array.Empty<byte>();
            public string Text { get; init; } = string.Empty;
            public JsonNode? Json { get; init; }
            public bool TimedOut { get; init; }
            public string? Error { get; init; }
            public bool Unreachable { get; init; }
            public bool IsSuccessStatus => Status >= 200 && Status < 300;
        }

        private Task<IsapiResponse> SendJsonAsync(HikDeviceConnection device, HttpMethod method, string path, JsonNode body)
        {
            var json = body.ToJsonString();
            return SendAsync(device, method, path, () => new StringContent(json, Encoding.UTF8, "application/json"), DefaultTimeout);
        }

        /// <summary>
        /// Sends one request, failing over from Host to Host2 only on connection errors
        /// (a timeout during a capture just means nothing was presented yet).
        /// </summary>
        private async Task<IsapiResponse> SendAsync(HikDeviceConnection device, HttpMethod method, string path, Func<HttpContent>? content, TimeSpan timeout)
        {
            var hosts = new List<string> { device.Host };
            if (!string.IsNullOrWhiteSpace(device.Host2) && device.Host2 != device.Host) hosts.Add(device.Host2!);

            var client = GetClient(device);
            string? lastError = null;

            foreach (var host in hosts)
            {
                var hostGate = HostGates.GetOrAdd(host, _ => new SemaphoreSlim(MaxPerHost, MaxPerHost));
                await FleetGate.WaitAsync();
                await hostGate.WaitAsync();
                try
                {
                    using var cts = new CancellationTokenSource(timeout);
                    using var req = new HttpRequestMessage(method, BaseUrl(device, host) + path);
                    if (content != null) req.Content = content();

                    try
                    {
                        using var resp = await client.SendAsync(req, cts.Token);
                        var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                        var text = Encoding.UTF8.GetString(bytes);
                        JsonNode? json = null;
                        try { if (text.TrimStart().StartsWith("{")) json = JsonNode.Parse(text); } catch (JsonException) { }

                        if (resp.StatusCode == HttpStatusCode.Unauthorized)
                            return new IsapiResponse { Status = 401, Error = $"{device.Name}: the machine rejected the username/password." };

                        return new IsapiResponse { Status = (int)resp.StatusCode, Body = bytes, Text = text, Json = json };
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        // Our own timeout: the request reached the machine but no answer in time
                        // (for captures this just means nothing was presented yet).
                        return new IsapiResponse { TimedOut = true };
                    }
                    catch (OperationCanceledException ex)
                    {
                        // Connect timeout (link down / packets dropped) → try the next link (Host2).
                        lastError = "connection timed out";
                        _logger.LogWarning("Hik ISAPI {Method} {Path} on {Device} via {Host}: {Error}", method, path, device.Name, host, ex.Message);
                    }
                    catch (HttpRequestException ex)
                    {
                        lastError = ex.Message;
                        _logger.LogWarning("Hik ISAPI {Method} {Path} on {Device} via {Host} failed: {Error}", method, path, device.Name, host, ex.Message);
                    }
                }
                finally
                {
                    hostGate.Release();
                    FleetGate.Release();
                }
            }

            return new IsapiResponse { Error = $"{device.Name} is unreachable ({lastError}).", Unreachable = true };
        }

        private static HttpClient GetClient(HikDeviceConnection d)
        {
            var key = $"{d.Host}|{d.Host2}|{d.Port}|{d.UseHttps}|{d.Username}|{d.Password}";
            return Clients.GetOrAdd(key, _ =>
            {
                var cache = new CredentialCache();
                var cred = new NetworkCredential(d.Username, d.Password);
                cache.Add(new Uri(BaseUrl(d, d.Host)), "Digest", cred);
                cache.Add(new Uri(BaseUrl(d, d.Host)), "Basic", cred);
                if (!string.IsNullOrWhiteSpace(d.Host2) && d.Host2 != d.Host)
                {
                    cache.Add(new Uri(BaseUrl(d, d.Host2!)), "Digest", cred);
                    cache.Add(new Uri(BaseUrl(d, d.Host2!)), "Basic", cred);
                }

                var handler = new SocketsHttpHandler
                {
                    Credentials = cache,
                    PreAuthenticate = true,
                    ConnectTimeout = ConnectTimeout,
                    // Terminals ship with self-signed certificates.
                    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    }
                };
                return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            });
        }

        private static string BaseUrl(HikDeviceConnection d, string host)
        {
            var proto = d.UseHttps ? "https" : "http";
            var port = d.Port > 0 ? d.Port : (d.UseHttps ? 443 : 80);
            return $"{proto}://{host}:{port}";
        }

        // ---- Parsing --------------------------------------------------------------

        private static HikIsapiResult Interpret(IsapiResponse res)
        {
            if (res.Error != null) return new HikIsapiResult { Ok = false, Error = res.Error, Unreachable = res.Unreachable };
            if (res.TimedOut) return new HikIsapiResult { Ok = false, Error = "The machine did not respond in time.", Unreachable = true };

            // Some endpoints (and older firmware) answer in XML, not JSON.
            var statusCode = res.Json?["statusCode"]?.ToString() ?? XmlTag(res.Text, "statusCode");
            var statusString = res.Json?["statusString"]?.ToString() ?? XmlTag(res.Text, "statusString");
            var sub = res.Json?["subStatusCode"]?.ToString() ?? XmlTag(res.Text, "subStatusCode");
            var errorMsg = res.Json?["errorMsg"]?.ToString();

            var ok = res.IsSuccessStatus && (statusCode == null || statusCode == "1");
            if (ok) return HikIsapiResult.Success();

            return HikIsapiResult.Fail(
                !string.IsNullOrEmpty(sub) && HikMessages.ContainsKey(sub) ? HikMessages[sub]
                : !string.IsNullOrEmpty(errorMsg) ? errorMsg
                : Describe(sub, statusString) ?? $"Device returned HTTP {res.Status}",
                sub);
        }

        private static string Describe(string? sub, string? statusString)
        {
            if (!string.IsNullOrEmpty(sub) && HikMessages.TryGetValue(sub, out var msg)) return msg;
            if (!string.IsNullOrEmpty(statusString) && statusString != "Invalid Operation") return statusString;
            return sub ?? "Device communication error";
        }

        private static string? XmlTag(string? text, string tag)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Regex.Match(text, $@"<(?:\w+:)?{tag}(?:\s[^>]*)?>([\s\S]*?)</(?:\w+:)?{tag}>");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static byte[]? ExtractJpeg(byte[] buf)
        {
            int start = -1, end = -1;
            for (var i = 0; i < buf.Length - 1; i++)
            {
                if (buf[i] == 0xFF && buf[i + 1] == 0xD8) { start = i; break; }
            }
            if (start < 0) return null;
            for (var i = buf.Length - 2; i > start; i--)
            {
                if (buf[i] == 0xFF && buf[i + 1] == 0xD9) { end = i; break; }
            }
            if (end <= start) return null;
            return buf[start..(end + 2)];
        }
    }
}

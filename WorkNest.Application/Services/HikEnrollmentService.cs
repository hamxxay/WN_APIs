using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Enrolls booking attendants on Hikvision terminals: registers the person (with the booking's
    /// validity window) on every selected machine, captures the credential on one machine, then
    /// copies it to the rest — the same flow as the HIK_Access-Control Users page.
    /// </summary>
    public partial class HikEnrollmentService : IHikEnrollmentService
    {
        private readonly IDbRepository _db;
        private readonly IHikIsapiClient _isapi;
        private readonly ILogger<HikEnrollmentService> _logger;
        private readonly IOrderStatusService _orderStatus;
        private readonly IBusinessClock _clock;

        public HikEnrollmentService(IDbRepository db, IHikIsapiClient isapi, ILogger<HikEnrollmentService> logger, IOrderStatusService orderStatus, IBusinessClock clock)
        {
            _clock = clock;
            _db = db;
            _isapi = isapi;
            _logger = logger;
            _orderStatus = orderStatus;
        }

        public async Task<HikEnrollResultDto> EnrollFingerprintAsync(int bookingDetailId, int personId, HikEnrollRequest request)
        {
            var prep = await PrepareAsync("fingerprint", bookingDetailId, personId, request);
            return prep.Failed ?? await RunFingerprintAsync(prep, request);
        }

        public async Task<HikEnrollResultDto> EnrollCardAsync(int bookingDetailId, int personId, HikEnrollRequest request)
        {
            var invalid = ValidateCardNo("card", request);
            if (invalid != null) return invalid;
            var prep = await PrepareAsync("card", bookingDetailId, personId, request);
            return prep.Failed ?? await RunCardAsync(prep, request);
        }

        public async Task<HikEnrollResultDto> EnrollFaceAsync(int bookingDetailId, int personId, HikEnrollRequest request)
        {
            var prep = await PrepareAsync("face", bookingDetailId, personId, request);
            return prep.Failed ?? await RunFaceAsync(prep);
        }

        // ---- Capture on prep.Capture → copy to prep.Others → queue for prep.Offline ------
        // Shared by booking attendants and staff (HikEnrollmentService.Staff.cs).

        private async Task<HikEnrollResultDto> RunFingerprintAsync(Prepared prep, HikEnrollRequest? request)
        {
            var fingerNo = request?.FingerNo is >= 1 and <= 10 ? request.FingerNo : 1;

            var capture = await _isapi.CaptureFingerprintAsync(prep.Capture!, fingerNo);
            if (!capture.Ok) return Fail(prep, capture.Error);

            var added = await _isapi.AddFingerprintAsync(prep.Capture!, prep.EmployeeNo, capture.FingerData!, fingerNo);
            if (!added.Ok) return Fail(prep, added.Error);

            try { await _db.SaveHikFingerprintTemplateDbAsync(prep.EmployeeNo, prep.Context!.Name, fingerNo, capture.FingerData!); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to vault fingerprint for employee #{Emp}", prep.EmployeeNo); }

            await ReplicateAsync(prep,
                d => _isapi.AddFingerprintAsync(d, prep.EmployeeNo, capture.FingerData!, fingerNo),
                grant => AddPrint(grant, capture.FingerData!, fingerNo));
            prep.Result.Ok = true;
            prep.Result.Quality = capture.Quality;
            return prep.Result;
        }

        private async Task<HikEnrollResultDto> RunCardAsync(Prepared prep, HikEnrollRequest? request)
        {
            var cardNo = request?.CardNo?.Trim();
            if (string.IsNullOrEmpty(cardNo))
            {
                var capture = await _isapi.CaptureCardAsync(prep.Capture!);
                if (!capture.Ok) return Fail(prep, capture.Error);
                cardNo = capture.CardNo!;
            }

            var added = await _isapi.AddCardAsync(prep.Capture!, prep.EmployeeNo, cardNo);
            if (!added.Ok) return Fail(prep, added.Error);

            await ReplicateAsync(prep,
                d => _isapi.AddCardAsync(d, prep.EmployeeNo, cardNo),
                grant => AddCard(grant, cardNo));
            prep.Result.Ok = true;
            prep.Result.CardNo = cardNo;
            return prep.Result;
        }

        private async Task<HikEnrollResultDto> RunFaceAsync(Prepared prep)
        {
            var capture = await _isapi.CaptureFaceAsync(prep.Capture!);
            if (!capture.Ok) return Fail(prep, capture.Error);

            var added = await _isapi.AddFaceByImageAsync(prep.Capture!, prep.EmployeeNo, capture.Jpeg!);
            if (!added.Ok) return Fail(prep, added.Error);

            // A queued grant can only carry face *model* data, not a photo: the person is queued and the
            // HIK credential sync copies the face from an online machine once they exist on the offline one.
            await ReplicateAsync(prep, d => _isapi.AddFaceByImageAsync(d, prep.EmployeeNo, capture.Jpeg!), _ => { },
                queuedNote: "Machine offline — user queued; the face is copied by the HIK credential sync after it reconnects.");
            prep.Result.Ok = true;
            return prep.Result;
        }

        /// <summary>Typed card number (manual) — validated before anything is pushed to a machine.</summary>
        private static HikEnrollResultDto? ValidateCardNo(string type, HikEnrollRequest? request)
        {
            var typed = request?.CardNo?.Trim();
            if (!string.IsNullOrEmpty(typed) && !System.Text.RegularExpressions.Regex.IsMatch(typed, "^[0-9A-Za-z]{1,32}$"))
                return new HikEnrollResultDto { Type = type, Ok = false, Error = "Card number must be 1–32 letters or digits." };
            return null;
        }

        // ---- Access Status tick → block / unblock on the machines ---------------------

        /// <summary>
        /// Applies the Access Status tick to the machines for one attendant (personId) or the whole booking
        /// (personId null): untick blocks the person on the booked room's machine + every Entrance machine
        /// (credentials stay enrolled), tick unblocks. Offline machines get a queued 'block' / 'unblock'.
        /// People never enrolled on a machine are skipped — there is nothing to block.
        /// </summary>
        public async Task<HikAccessSyncResultDto> SetAccessEnabledAsync(int bookingDetailId, int? personId, bool isEnabled)
        {
            var result = new HikAccessSyncResultDto { Enabled = isEnabled };
            var people = (await _db.GetHikAttendantContextsDbAsync(bookingDetailId, personId)).ToList();
            var enrolled = people.Where(p => !string.IsNullOrWhiteSpace(p.EmployeeNo)).ToList();
            result.People = enrolled.Count;
            result.NotEnrolled = people.Count - enrolled.Count;
            result.Suspended = people.Any(p => p.IsSuspended);
            if (enrolled.Count == 0) return result;

            // While the booking is suspended for an overdue challan the machines keep everyone blocked;
            // the tick is still saved and takes effect when the suspension is lifted.
            result.Devices = await ApplyMachineAccessAsync(enrolled, p => isEnabled && !p.IsSuspended);
            return result;
        }

        /// <summary>
        /// Pushes block / unblock to the booked room's machine + every Entrance machine for each enrolled
        /// person (per-person target state). Offline machines get a queued 'block' / 'unblock'.
        /// </summary>
        private async Task<List<HikEnrollDeviceResultDto>> ApplyMachineAccessAsync(List<HikAttendantContext> enrolled, Func<HikAttendantContext, bool> allowed)
        {
            var (devices, roomDevices) = await ResolveBookingMachinesAsync(enrolled[0]);
            var roomIds = roomDevices.Select(d => d.Id).ToHashSet();

            // Per person: older queued ops (WN-032) and whether another active booking still allows them
            // through the Entrance machines (WN-034).
            var pending = new Dictionary<string, HashSet<int>>();
            var otherBookingAllows = new Dictionary<string, bool>();
            foreach (var person in enrolled)
            {
                var emp = person.EmployeeNo!;
                pending[emp] = await _db.GetHikPendingOpDeviceIdsForEmployeeDbAsync(emp);
                var others = await _db.GetHikAttendantContextsDbAsync(null, person.PersonId);
                otherBookingAllows[emp] = others.Any(o => o.BookingDetailId != person.BookingDetailId && o.AllowedOnMachines);
            }

            // Per machine: one result line, people pushed in sequence (requests are rate-limited per host).
            var lines = await Task.WhenAll(devices.Select(async device =>
            {
                var line = new HikEnrollDeviceResultDto { DeviceId = device.Id, Device = device.Name, Ok = true };
                foreach (var person in enrolled)
                {
                    var emp = person.EmployeeNo!;
                    var enable = allowed(person) || (!roomIds.Contains(device.Id) && otherBookingAllows[emp]);
                    var op = enable ? "unblock" : "block";
                    if (device.Online && !pending[emp].Contains(device.Id))
                    {
                        var r = await _isapi.SetPersonEnabledAsync(device, emp, enable);
                        if (r.Ok || r.SubStatusCode == "employeeNoNotExist") continue; // confirmed not on this machine → nothing to change
                        if (!r.Unreachable) { line.Ok = false; line.Error ??= $"{person.Name}: {r.Error}"; continue; }
                    }

                    try
                    {
                        await _db.QueueHikPendingOpDbAsync(device.Id, op, emp, "{}");
                        line.Ok = false;
                        line.Queued = true;
                        line.Error ??= device.Online
                            ? "Queued behind earlier pending changes for this machine."
                            : "Machine offline — change queued; applies when it reconnects.";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to queue Hik {Op} for employee #{Emp} on device {Device}", op, emp, device.Id);
                        line.Ok = false;
                        line.Error ??= "Machine offline and the change could not be queued.";
                    }
                }
                return line;
            }));
            return lines.ToList();
        }

        /// <summary>
        /// The person's Machine ID. A new number starts above the highest member number already on the
        /// machines (HIK's roster cache) and is checked against the online machines; if one of them already
        /// has it, a higher number is taken instead (WN-033). Only WN_HIK_PersonMap is written.
        /// </summary>
        private async Task<string> AllocateMachineIdAsync(HikAttendantContext ctx, IEnumerable<HikDeviceConnection> onlineDevices)
        {
            if (!string.IsNullOrWhiteSpace(ctx.EmployeeNo)) return ctx.EmployeeNo!;

            var floor = await MachineIdFloorAsync();
            var emp = await _db.GetOrCreateHikEmployeeNoDbAsync(ctx.PersonId, floor);
            var targets = onlineDevices.ToList();

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var checks = await Task.WhenAll(targets.Select(d => _isapi.PersonExistsAsync(d, emp)));
                if (!checks.Any(c => c == true)) return emp;

                _logger.LogWarning("Machine ID {Emp} is already used on a machine; allocating a higher number for person {PersonId}.", emp, ctx.PersonId);
                emp = await _db.ReallocateHikEmployeeNoDbAsync(ctx.PersonId, int.TryParse(emp, out var n) ? n : floor);
            }
            return emp;
        }

        /// <summary>Highest member number (below 8500) on any machine, from HIK's roster cache (read only).</summary>
        private async Task<int> MachineIdFloorAsync()
        {
            var max = 0;
            foreach (var (_, users) in ParseRosters(await _db.GetHikDevCacheSnapshotsDbAsync()))
                foreach (var emp in users.Keys)
                    if (int.TryParse(emp, out var n) && n < 8500 && n > max) max = n;
            return max;
        }

        /// <summary>
        /// The booking's machines: the booked room's machine(s) (matched by Code) + every Entrance machine.
        /// </summary>
        private async Task<(List<HikDeviceConnection> All, List<HikDeviceConnection> Room)> ResolveBookingMachinesAsync(HikAttendantContext ctx)
        {
            var allDevices = (await _db.GetHikDevicesAsync()).ToList();
            var roomIds = allDevices
                .Where(d => !(d.Grp ?? "").StartsWith("Entrance", StringComparison.OrdinalIgnoreCase))
                .Where(d => RoomMatches(d.Code, ctx.SpaceCode, ctx.SpaceName))
                .Select(d => d.Id)
                .ToList();
            var entranceIds = await _db.GetHikEntranceDeviceIdsDbAsync();
            var devices = (await _db.GetHikDeviceConnectionsDbAsync(roomIds.Concat(entranceIds))).ToList();
            return (devices, devices.Where(d => roomIds.Contains(d.Id)).ToList());
        }

        // ---- Shared flow ------------------------------------------------------------

        private const string OfflineQueuedNote = "Machine offline — queued; it will be applied automatically when the machine reconnects.";

        private sealed class Prepared
        {
            public HikEnrollResultDto Result { get; init; } = new();
            public HikEnrollResultDto? Failed { get; set; }
            public HikAttendantContext? Context { get; set; }
            public string EmployeeNo { get; set; } = string.Empty;
            public string ValidBegin { get; set; } = string.Empty;
            public string ValidEnd { get; set; } = string.Empty;
            /// <summary>Entrance machines hold one record per person for ALL their bookings (WN-034).</summary>
            public HashSet<int> EntranceIds { get; } = new();
            public string EntranceBegin { get; set; } = string.Empty;
            public string EntranceEnd { get; set; } = string.Empty;
            public bool EntranceEnabled { get; set; }
            /// <summary>Capture machine also has older queued ops → queue this change after them (WN-032).</summary>
            public bool RequeueCapture { get; set; }

            public (string Begin, string End, bool Enabled) RecordFor(HikDeviceConnection d) =>
                EntranceIds.Contains(d.Id)
                    ? (EntranceBegin, EntranceEnd, EntranceEnabled)
                    : (ValidBegin, ValidEnd, Context!.AllowedOnMachines);
            public HikDeviceConnection? Capture { get; set; }
            /// <summary>Online machines the person was registered on (credential is pushed live).</summary>
            public List<HikDeviceConnection> Others { get; } = new();
            /// <summary>Offline machines (credential goes to WN_HIK_PendingOps).</summary>
            public List<HikDeviceConnection> Offline { get; } = new();
        }

        /// <summary>
        /// Loads the attendant, allocates their employee #, and makes sure they exist on every
        /// selected online machine before anything is captured. Offline machines are collected for the queue.
        /// </summary>
        private async Task<Prepared> PrepareAsync(string type, int bookingDetailId, int personId, HikEnrollRequest request)
        {
            var prep = new Prepared { Result = new HikEnrollResultDto { Type = type } };

            prep.Context = await _db.GetHikAttendantContextDbAsync(bookingDetailId, personId);
            if (prep.Context == null)
            {
                prep.Failed = Fail(prep, "This user is not assigned to the selected booked space.");
                return prep;
            }

            // Machines: the booked room's machine(s) + every Entrance machine. Capture happens only on
            // the room's machine — never on any other machine.
            var (devices, roomDevices) = await ResolveBookingMachinesAsync(prep.Context);
            if (roomDevices.Count == 0)
            {
                prep.Failed = Fail(prep, $"No machine is linked to {prep.Context.SpaceName ?? "the booked room"}. Set the room machine's Code to the room number.");
                return prep;
            }

            prep.Capture = roomDevices.FirstOrDefault(d => d.Online) ?? roomDevices.FirstOrDefault();
            if (prep.Capture == null || !prep.Capture.Online)
            {
                prep.Failed = Fail(prep, $"{prep.Capture?.Name ?? "The booked room's machine"} is offline — enrollment can't be captured until it is back online.");
                return prep;
            }

            prep.EmployeeNo = await AllocateMachineIdAsync(prep.Context, devices.Where(d => d.Online));
            prep.Result.MachineId = prep.EmployeeNo;
            (prep.ValidBegin, prep.ValidEnd) = ValidityWindow(prep.Context);

            // Entrance machines: one record across all of the person's active bookings —
            // earliest start, latest end, allowed if ANY booking allows them (WN-034).
            var roomIds = roomDevices.Select(d => d.Id).ToHashSet();
            foreach (var d in devices.Where(d => !roomIds.Contains(d.Id))) prep.EntranceIds.Add(d.Id);
            var allBookings = (await _db.GetHikAttendantContextsDbAsync(null, personId)).ToList();
            if (allBookings.Count == 0) allBookings.Add(prep.Context);
            var windows = allBookings.Select(ValidityWindow).ToList();
            prep.EntranceBegin = windows.Min(w => w.Begin)!;
            prep.EntranceEnd = windows.Max(w => w.End)!;
            prep.EntranceEnabled = allBookings.Any(b => b.AllowedOnMachines);

            // Machines that are offline, or still have older queued ops for this person, are not changed live:
            // the change is queued after the older ops so the newest always wins (WN-032).
            var pending = await _db.GetHikPendingOpDeviceIdsForEmployeeDbAsync(prep.EmployeeNo);
            prep.RequeueCapture = pending.Contains(prep.Capture.Id);
            prep.Offline.AddRange(devices.Where(d => d.Id != prep.Capture.Id && (!d.Online || pending.Contains(d.Id))));
            var online = devices.Where(d => d.Online && (d.Id == prep.Capture.Id || !pending.Contains(d.Id))).ToList();

            var registered = await Task.WhenAll(online.Select(async d =>
            {
                // Respect the Access Status tick and challan suspension: enrolling must not re-enable a blocked person.
                var (begin, end, enabled) = prep.RecordFor(d);
                var r = await _isapi.UpsertPersonAsync(d, prep.EmployeeNo, prep.Context.Name, begin, end, enabled);
                return (Device: d, Result: r);
            }));

            foreach (var (device, result) in registered)
            {
                if (device.Id == prep.Capture.Id) continue;
                if (result.Ok) prep.Others.Add(device);
                else if (result.Unreachable) prep.Offline.Add(device);
                else prep.Result.Devices.Add(new HikEnrollDeviceResultDto { DeviceId = device.Id, Device = device.Name, Ok = false, Error = result.Error });
            }

            var onCapture = registered.First(x => x.Device.Id == prep.Capture.Id).Result;
            if (!onCapture.Ok)
            {
                prep.Failed = Fail(prep, onCapture.Unreachable
                    ? $"{prep.Capture.Name} (the booked room's machine) is not reachable — enrollment can't be captured until it is back online."
                    : $"Could not register the user on {prep.Capture.Name}: {onCapture.Error}");
                return prep;
            }

            try
            {
                var cnic = string.Equals(prep.Context.IdType, "CNIC", StringComparison.OrdinalIgnoreCase)
                    ? new string(prep.Context.IdNumber.Where(char.IsDigit).ToArray())
                    : null;
                await _db.SaveHikUserCnicDbAsync(prep.EmployeeNo, prep.Context.Name, string.IsNullOrEmpty(cnic) ? null : cnic);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save CNIC for employee #{Emp}", prep.EmployeeNo);
            }

            return prep;
        }

        /// <summary>
        /// Captured on the capture machine → copy the credential to every other online machine;
        /// machines that are (or turn out to be) offline get a queued 'grant' in WN_HIK_PendingOps.
        /// </summary>
        private async Task ReplicateAsync(Prepared prep, Func<HikDeviceConnection, Task<HikIsapiResult>> push,
            Action<JsonObject> addToGrant, string queuedNote = OfflineQueuedNote)
        {
            prep.Result.Devices.Insert(0, new HikEnrollDeviceResultDto { DeviceId = prep.Capture!.Id, Device = prep.Capture.Name, Ok = true });

            var copies = await Task.WhenAll(prep.Others.Select(async d => (Device: d, Result: await push(d))));
            foreach (var (device, result) in copies)
            {
                if (result.Unreachable) prep.Offline.Add(device);
                else prep.Result.Devices.Add(new HikEnrollDeviceResultDto { DeviceId = device.Id, Device = device.Name, Ok = result.Ok, Error = result.Error });
            }

            // The capture machine still has older queued ops for this person: queue this credential after them
            // too, so the replay order ends on the newest state (WN-032).
            if (prep.RequeueCapture)
            {
                try { await QueueGrantAsync(prep, prep.Capture, addToGrant); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to queue Hik grant for employee #{Emp} on capture device {Device}", prep.EmployeeNo, prep.Capture.Id); }
            }

            foreach (var device in prep.Offline)
            {
                try
                {
                    await QueueGrantAsync(prep, device, addToGrant);
                    prep.Result.Devices.Add(new HikEnrollDeviceResultDto
                    {
                        DeviceId = device.Id, Device = device.Name, Queued = true,
                        Error = device.Online ? "Queued behind earlier pending changes for this machine." : queuedNote
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to queue Hik grant for employee #{Emp} on device {Device}", prep.EmployeeNo, device.Id);
                    prep.Result.Devices.Add(new HikEnrollDeviceResultDto { DeviceId = device.Id, Device = device.Name, Error = "Machine offline and the change could not be queued." });
                }
            }
        }

        /// <summary>
        /// Writes/merges the HIK 'grant' op for an offline machine: { record, cards[], prints[], faces[] } —
        /// the payload HIK scheduler.replayPendingOps() applies when the machine is back online.
        /// </summary>
        private async Task QueueGrantAsync(Prepared prep, HikDeviceConnection device, Action<JsonObject> addToGrant)
        {
            JsonObject grant;
            var existing = await _db.GetHikPendingOpPayloadDbAsync(device.Id, "grant", prep.EmployeeNo);
            try { grant = existing != null ? JsonNode.Parse(existing) as JsonObject ?? new JsonObject() : new JsonObject(); }
            catch (JsonException) { grant = new JsonObject(); }

            var (begin, end, enabled) = prep.RecordFor(device);
            grant["record"] = new JsonObject
            {
                ["employeeNo"] = prep.EmployeeNo,
                ["name"] = prep.Context!.Name,
                ["admin"] = false,
                ["enabled"] = enabled,
                ["validBegin"] = begin,
                ["validEnd"] = end
            };
            if (grant["cards"] is not JsonArray) grant["cards"] = new JsonArray();
            if (grant["prints"] is not JsonArray) grant["prints"] = new JsonArray();
            if (grant["faces"] is not JsonArray) grant["faces"] = new JsonArray();
            addToGrant(grant);

            await _db.QueueHikPendingOpDbAsync(device.Id, "grant", prep.EmployeeNo, grant.ToJsonString());
        }

        private static void AddCard(JsonObject grant, string cardNo)
        {
            var cards = (JsonArray)grant["cards"]!;
            if (!cards.Any(c => c?.ToString() == cardNo)) cards.Add(cardNo);
        }

        private static void AddPrint(JsonObject grant, string fingerData, int fingerNo)
        {
            var prints = (JsonArray)grant["prints"]!;
            foreach (var old in prints.Where(p => p?["fingerPrintID"]?.ToString() == fingerNo.ToString()).ToList()) prints.Remove(old);
            prints.Add(new JsonObject { ["fingerData"] = fingerData, ["fingerPrintID"] = fingerNo });
        }

        /// <summary>
        /// A machine belongs to the booked room when its Code equals the space code, or — as the HIK
        /// dashboard stores the room number in Code — equals the room number in the space code/name
        /// (e.g. Code "354" ↔ "Office 354").
        /// </summary>
        public static bool RoomMatches(string? deviceCode, string? spaceCode, string? spaceName)
        {
            var code = (deviceCode ?? "").Trim();
            if (code.Length == 0) return false;
            if (string.Equals(code, (spaceCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return true;

            var codeNumber = System.Text.RegularExpressions.Regex.Match(code, @"\d+").Value.TrimStart('0');
            if (codeNumber.Length == 0) return false;
            foreach (var source in new[] { spaceCode, spaceName })
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(source ?? "", @"\d+"))
                    if (m.Value.TrimStart('0') == codeNumber) return true;
            }
            return false;
        }

        /// <summary>
        /// Terminal validity follows the booking: start → end (end-of-day when the end has no time part).
        /// </summary>
        private static (string Begin, string End) ValidityWindow(HikAttendantContext ctx)
        {
            const string fmt = "yyyy-MM-ddTHH:mm:ss";
            var begin = ctx.StartDateTime?.ToString(fmt) ?? "2020-01-01T00:00:00";

            string end;
            if (ctx.EndDateTime is DateTime e)
                end = (e.TimeOfDay == TimeSpan.Zero ? e.Date.AddDays(1).AddSeconds(-1) : e).ToString(fmt);
            else
                end = "2037-12-31T23:59:59";

            return (begin, end);
        }

        private static HikEnrollResultDto Fail(Prepared prep, string? error)
        {
            prep.Result.Ok = false;
            prep.Result.Error = error ?? "Enrollment failed.";
            return prep.Result;
        }
    }
}

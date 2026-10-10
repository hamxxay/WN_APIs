using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Building staff (janitors, office boys, …): machine users with a job tag and no booking — the same
    /// people the HIK dashboard's "Add user" creates. Every Entrance machine is always included; room
    /// machines are picked per person. Shares the capture / copy / offline-queue flow with attendants.
    /// </summary>
    public partial class HikEnrollmentService : IHikStaffService
    {
        private const string NoEndDate = "2037-12-31T23:59:59";
        private const string DefaultBegin = "2020-01-01T00:00:00";

        // ---- Read ----------------------------------------------------------------------

        public async Task<IEnumerable<HikStaffDto>> GetStaffAsync(bool includeMachineAdmins = true, IReadOnlyCollection<int>? locationIds = null)
        {
            var rowsTask = _db.GetHikStaffDbAsync();
            // Location-bound callers: only their locations' machines (+ unassigned); staff on none of them are hidden.
            var devicesTask = _db.GetHikDevicesAsync(null, locationIds);
            var snapsTask = _db.GetHikDevCacheSnapshotsDbAsync();
            await Task.WhenAll(rowsTask, devicesTask, snapsTask);

            var devices = devicesTask.Result.ToDictionary(d => d.Id);
            var rosters = ParseRosters(snapsTask.Result);

            return rowsTask.Result
                .GroupBy(r => Convert.ToString(r["employee_no"]) ?? "")
                .Select(g =>
                {
                    var r = g.First();
                    var emp = g.Key;
                    var staff = new HikStaffDto
                    {
                        EmployeeNo = emp,
                        Name = Convert.ToString(r["name"]) ?? "",
                        Cnic = Convert.ToString(r["cnic"]),
                        TagId = r["tag_id"] != null ? Convert.ToInt32(r["tag_id"]) : null,
                        Tag = Convert.ToString(r["tag"]),
                        PendingOps = r["pending_ops"] != null ? Convert.ToInt32(r["pending_ops"]) : 0
                    };

                    foreach (var (deviceId, users) in rosters)
                    {
                        if (!users.TryGetValue(emp, out var u) || !devices.TryGetValue(deviceId, out var d)) continue;
                        staff.Machines.Add(new HikStaffMachineDto { DeviceId = d.Id, Name = d.Name, Grp = d.Grp, Code = d.Code, Online = d.Online == 1 });
                        if (u["Valid"]?["enable"]?.ToString() == "false") staff.Enabled = false;
                        if (HikDeviceService.IsMachineAdmin(u)) staff.IsMachineAdmin = true;
                        staff.ValidEnd ??= u["Valid"]?["endTime"]?.ToString();
                        staff.Cards = Math.Max(staff.Cards, IntOf(u["numOfCard"]));
                        staff.Fingerprints = Math.Max(staff.Fingerprints, IntOf(u["numOfFP"]));
                        staff.Faces = Math.Max(staff.Faces, IntOf(u["numOfFace"]));
                    }
                    if (staff.ValidEnd != null && staff.ValidEnd.StartsWith("2037-12-31")) staff.ValidEnd = null;
                    staff.Machines = staff.Machines.OrderBy(m => IsEntrance(m.Grp) ? 0 : 1).ThenBy(m => m.Name).ToList();
                    return staff;
                })
                .Where(s => includeMachineAdmins || !s.IsMachineAdmin)
                .Where(s => locationIds == null || s.Machines.Count > 0 || s.PendingOps > 0)
                .ToList();
        }

        public async Task<IEnumerable<HikTagDto>> GetTagsAsync()
        {
            var rows = await _db.GetHikTagsDbAsync();
            return rows.Select(r => new HikTagDto { Id = Convert.ToInt32(r["Id"]), Name = Convert.ToString(r["Name"]) ?? "" }).ToList();
        }

        public async Task<HikTagDto> AddTagAsync(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 48) throw new ArgumentException("Tag name is required (max 48 characters).");
            var id = await _db.AddHikTagDbAsync(name);
            return new HikTagDto { Id = id, Name = name };
        }

        // ---- Create / machines / block / delete -----------------------------------------

        public async Task<HikStaffResultDto> CreateStaffAsync(HikStaffCreateRequest request)
        {
            var name = (request?.Name ?? "").Trim();
            var cnic = new string((request?.Cnic ?? "").Where(char.IsDigit).ToArray());
            if (name.Length == 0 || name.Length > 64) return new HikStaffResultDto { Error = "Name is required (max 64 characters)." };
            if (!Cnic.IsValid(request?.Cnic)) return new HikStaffResultDto { Error = Cnic.ErrorMessage };
            if (!TryValidUntil(request!.ValidUntil, out var validEnd)) return new HikStaffResultDto { Error = "Access-until date is not valid." };

            var targets = await StaffTargetDevicesAsync(request.RoomDeviceIds, request.CallerLocationIds);
            if (targets.Count == 0) return new HikStaffResultDto { Error = "No Entrance machines are registered." };

            var emp = await _db.CreateHikStaffDbAsync(name, cnic, request.TagId, await MachineIdFloorAsync());
            var result = new HikStaffResultDto { MachineId = emp };

            foreach (var line in await Task.WhenAll(targets.Select(d => RegisterAsync(d, emp, name, DefaultBegin, validEnd, true, live: d.Online))))
                result.Devices.Add(line);

            result.Ok = result.Devices.Any(d => d.Ok || d.Queued);
            if (!result.Ok) result.Error = "The staff member could not be added to any machine.";
            return result;
        }

        /// <summary>
        /// Sets the staff member's machines (Entrance always + the given rooms) and access-until date:
        /// adds them where missing, updates validity everywhere, removes them from rooms no longer selected.
        /// Credentials (cards / prints / face) reach newly added machines through the HIK credential sync.
        /// </summary>
        public async Task<HikStaffResultDto> UpdateStaffMachinesAsync(string employeeNo, HikStaffMachinesRequest request)
        {
            var staff = (await GetStaffAsync()).FirstOrDefault(s => s.EmployeeNo == employeeNo);
            if (staff == null) return new HikStaffResultDto { MachineId = employeeNo, Error = "Staff member not found." };
            if (!TryValidUntil(request?.ValidUntil, out var validEnd)) return new HikStaffResultDto { MachineId = employeeNo, Error = "Access-until date is not valid." };

            var desired = await StaffTargetDevicesAsync(request!.RoomDeviceIds, request.CallerLocationIds);
            var desiredIds = desired.Select(d => d.Id).ToHashSet();
            var m = await StaffCurrentMachinesAsync(employeeNo, staff);
            var enabled = await StaffLiveEnabledAsync(employeeNo, m, staff);
            // Entrance machines are always in `desired`, so only de-selected room machines are removed.
            var removeFrom = m.Present.Concat(m.Queue).Where(d => !desiredIds.Contains(d.Id)).ToList();

            var result = new HikStaffResultDto { MachineId = employeeNo };
            var adds = desired.Select(d => RegisterAsync(d, employeeNo, staff.Name, DefaultBegin, validEnd, enabled, live: m.IsLive(d)));
            var removes = removeFrom.Select(d => RemoveFromAsync(d, employeeNo, live: m.IsLive(d)));
            result.Devices.AddRange(await Task.WhenAll(adds.Concat(removes)));
            result.Ok = result.Devices.All(d => d.Ok || d.Queued);
            if (!result.Ok) result.Error = "Some machines could not be updated.";
            return result;
        }

        public async Task<HikAccessSyncResultDto> SetStaffEnabledAsync(string employeeNo, bool isEnabled)
        {
            var result = new HikAccessSyncResultDto { Enabled = isEnabled, People = 1 };
            var staff = (await GetStaffAsync()).FirstOrDefault(s => s.EmployeeNo == employeeNo);
            if (staff == null) return new HikAccessSyncResultDto { Enabled = isEnabled, Error = "Staff member not found." };

            var m = await StaffCurrentMachinesAsync(employeeNo, staff);
            var op = isEnabled ? "unblock" : "block";

            var online = await Task.WhenAll(m.Present.Select(async d =>
            {
                var r = await _isapi.SetPersonEnabledAsync(d, employeeNo, isEnabled);
                if (r.Unreachable) return await QueueLineAsync(d, op, employeeNo, "{}", isEnabled ? "Machine offline — unblock queued." : "Machine offline — block queued.");
                return new HikEnrollDeviceResultDto { DeviceId = d.Id, Device = d.Name, Ok = r.Ok, Error = r.Ok ? null : r.Error };
            }));
            var queued = await Task.WhenAll(m.Queue.Select(d =>
                QueueLineAsync(d, op, employeeNo, "{}", QueuedNote(d))));

            result.Devices = online.Concat(queued).Concat(m.UnknownLines()).ToList();
            return result;
        }

        /// <summary>
        /// Removes the staff member from every machine and from WN_HIK_Users. Machines that are offline or
        /// could not be checked get a queued 'delete-user' (WN_HIK_PendingOps) that HIK applies when they respond.
        /// </summary>
        public async Task<HikStaffResultDto> DeleteStaffAsync(string employeeNo)
        {
            var staff = (await GetStaffAsync()).FirstOrDefault(s => s.EmployeeNo == employeeNo);
            if (staff == null) return new HikStaffResultDto { MachineId = employeeNo, Error = "Staff member not found." };

            var m = await StaffCurrentMachinesAsync(employeeNo, staff);
            var result = new HikStaffResultDto { MachineId = employeeNo };
            result.Devices.AddRange(await Task.WhenAll(m.Present.Concat(m.Queue).Select(d => RemoveFromAsync(d, employeeNo, live: m.IsLive(d)))));
            // Machines that could not be checked (busy / error) get a queued 'delete-user' too: HIK's scheduler
            // replays it when the machine responds, and a delete of someone who is not there is a no-op.
            result.Devices.AddRange(await Task.WhenAll(m.Unknown.Select(d =>
                QueueLineAsync(d, "delete-user", employeeNo, "{}", "Could not check this machine — removal queued; applies automatically."))));
            result.Ok = result.Devices.All(d => d.Ok || d.Queued);

            if (result.Ok) await _db.DeleteHikStaffDbAsync(employeeNo);
            else result.Error = "Some machines could not remove the staff member — try again.";
            return result;
        }

        // ---- Enrollment -------------------------------------------------------------------

        public async Task<HikEnrollResultDto> EnrollStaffAsync(string employeeNo, string type, HikStaffEnrollRequest request)
        {
            if (type is not ("fingerprint" or "card" or "face"))
                return new HikEnrollResultDto { Type = type, Error = "Unknown credential type." };
            if (type == "card")
            {
                var invalid = ValidateCardNo(type, request);
                if (invalid != null) return invalid;
            }

            var prep = new Prepared { Result = new HikEnrollResultDto { Type = type, MachineId = employeeNo } };
            var staff = (await GetStaffAsync()).FirstOrDefault(s => s.EmployeeNo == employeeNo);
            if (staff == null) return Fail(prep, "Staff member not found.");

            var m = await StaffCurrentMachinesAsync(employeeNo, staff);
            prep.Capture = m.Present.FirstOrDefault(d => d.Id == request?.CaptureDeviceId);
            if (prep.Capture == null)
                return Fail(prep, "Choose one of this staff member's online machines to capture on.");

            prep.EmployeeNo = employeeNo;
            prep.ValidBegin = DefaultBegin;
            prep.ValidEnd = staff.ValidEnd ?? NoEndDate;
            prep.Context = new HikAttendantContext { Name = staff.Name, IsEnabled = await StaffLiveEnabledAsync(employeeNo, m, staff), EmployeeNo = employeeNo };
            prep.Others.AddRange(m.Present.Where(d => d.Id != prep.Capture.Id));
            prep.Offline.AddRange(m.Queue);

            return type switch
            {
                "fingerprint" => await RunFingerprintAsync(prep, request),
                "card" => await RunCardAsync(prep, request),
                _ => await RunFaceAsync(prep)
            };
        }

        // ---- Helpers ------------------------------------------------------------------------

        /// <summary>
        /// The selected room machines + the Entrance machines of the same location(s). The location comes from
        /// the selected machines, else from the caller (admins / sales executives); with neither, every Entrance.
        /// Machines without a location yet count everywhere.
        /// </summary>
        private async Task<List<HikDeviceConnection>> StaffTargetDevicesAsync(IEnumerable<int>? roomDeviceIds, IReadOnlyCollection<int>? callerLocationIds = null)
        {
            var all = (await _db.GetHikDevicesAsync()).ToList();
            var requested = (roomDeviceIds ?? Enumerable.Empty<int>()).ToHashSet();
            var locations = all.Where(d => requested.Contains(d.Id) && d.LocationId != null).Select(d => d.LocationId!.Value).ToHashSet();
            if (locations.Count == 0 && callerLocationIds is { Count: > 0 }) locations = callerLocationIds.ToHashSet();
            bool EntranceHere(HikDeviceDto d) => IsEntrance(d.Grp) && (locations.Count == 0 || d.LocationId == null || locations.Contains(d.LocationId.Value));
            var ids = all.Where(d => EntranceHere(d) || requested.Contains(d.Id)).Select(d => d.Id);
            return (await _db.GetHikDeviceConnectionsDbAsync(ids)).ToList();
        }

        /// <summary>Where a staff member is on the machines right now.</summary>
        private sealed class StaffMachines
        {
            /// <summary>Online, confirmed to have the person, no older queued ops → change live.</summary>
            public List<HikDeviceConnection> Present { get; } = new();
            /// <summary>Offline (known to have them) or still has older queued ops → queue the change (WN-032).</summary>
            public List<HikDeviceConnection> Queue { get; } = new();
            /// <summary>Online but the lookup failed (busy / error) — must not be treated as "not there" (WN-029).</summary>
            public List<HikDeviceConnection> Unknown { get; } = new();
            public HashSet<int> Pending { get; init; } = new();

            public bool IsLive(HikDeviceConnection d) => d.Online && !Pending.Contains(d.Id);

            public IEnumerable<HikEnrollDeviceResultDto> UnknownLines() => Unknown.Select(d => new HikEnrollDeviceResultDto
            {
                DeviceId = d.Id, Device = d.Name, Error = "Could not check this machine (busy or error) — try again."
            });
        }

        /// <summary>
        /// Online machines are checked live; offline machines count when the last roster snapshot or a queued
        /// grant says the person is there. A failed lookup on an online machine is reported, never skipped.
        /// </summary>
        private async Task<StaffMachines> StaffCurrentMachinesAsync(string employeeNo, HikStaffDto staff)
        {
            var allIds = (await _db.GetHikDevicesAsync()).Select(d => d.Id).ToList();
            var devices = (await _db.GetHikDeviceConnectionsDbAsync(allIds)).ToList();
            var known = staff.Machines.Select(x => x.DeviceId).Concat(await _db.GetHikPendingOpDeviceIdsDbAsync(employeeNo)).ToHashSet();
            var result = new StaffMachines { Pending = await _db.GetHikPendingOpDeviceIdsForEmployeeDbAsync(employeeNo) };

            var checks = await Task.WhenAll(devices.Select(async d => (Device: d, Exists: d.Online ? await _isapi.PersonExistsAsync(d, employeeNo) : null)));
            foreach (var (d, exists) in checks)
            {
                if (exists == true) (result.Pending.Contains(d.Id) ? result.Queue : result.Present).Add(d);
                else if (exists == null && !d.Online && known.Contains(d.Id)) result.Queue.Add(d);
                else if (exists == null && d.Online) (known.Contains(d.Id) ? result.Queue : result.Unknown).Add(d);
            }
            return result;
        }

        /// <summary>The staff member's current Allowed/Blocked state, read live from a machine (WN-031).</summary>
        private async Task<bool> StaffLiveEnabledAsync(string employeeNo, StaffMachines m, HikStaffDto staff)
        {
            foreach (var d in m.Present)
            {
                var enabled = await _isapi.GetPersonEnabledAsync(d, employeeNo);
                if (enabled.HasValue) return enabled.Value;
            }
            return staff.Enabled; // no machine readable right now → fall back to the roster snapshot
        }

        private static string QueuedNote(HikDeviceConnection d) =>
            d.Online ? "Queued behind earlier pending changes for this machine." : "Machine offline — change queued.";

        /// <summary>Create / update the person on one machine, queueing a 'grant' when it is offline.</summary>
        private async Task<HikEnrollDeviceResultDto> RegisterAsync(HikDeviceConnection d, string emp, string name, string begin, string end, bool enabled, bool live)
        {
            if (live)
            {
                var r = await _isapi.UpsertPersonAsync(d, emp, name, begin, end, enabled);
                if (!r.Unreachable) return new HikEnrollDeviceResultDto { DeviceId = d.Id, Device = d.Name, Ok = r.Ok, Error = r.Ok ? null : r.Error };
            }

            JsonObject grant;
            var existing = await _db.GetHikPendingOpPayloadDbAsync(d.Id, "grant", emp);
            try { grant = existing != null ? JsonNode.Parse(existing) as JsonObject ?? new JsonObject() : new JsonObject(); }
            catch (JsonException) { grant = new JsonObject(); }
            grant["record"] = new JsonObject
            {
                ["employeeNo"] = emp, ["name"] = name, ["admin"] = false, ["enabled"] = enabled,
                ["validBegin"] = begin, ["validEnd"] = end
            };
            if (grant["cards"] is not JsonArray) grant["cards"] = new JsonArray();
            if (grant["prints"] is not JsonArray) grant["prints"] = new JsonArray();
            if (grant["faces"] is not JsonArray) grant["faces"] = new JsonArray();
            return await QueueLineAsync(d, "grant", emp, grant.ToJsonString(), QueuedNote(d));
        }

        private async Task<HikEnrollDeviceResultDto> RemoveFromAsync(HikDeviceConnection d, string emp, bool live)
        {
            if (live)
            {
                var r = await _isapi.DeletePersonAsync(d, emp);
                if (r.Ok || r.SubStatusCode == "employeeNoNotExist")
                    return new HikEnrollDeviceResultDto { DeviceId = d.Id, Device = d.Name, Ok = true, Error = "Removed" };
                // Any failure (offline, busy, error): queue it — HIK retries the delete when the machine responds.
                return await QueueLineAsync(d, "delete-user", emp, "{}", $"Removal failed now ({r.Error}) — queued; applies automatically.");
            }
            return await QueueLineAsync(d, "delete-user", emp, "{}", d.Online ? "Removal queued behind earlier pending changes." : "Machine offline — removal queued.");
        }

        private async Task<HikEnrollDeviceResultDto> QueueLineAsync(HikDeviceConnection d, string op, string emp, string payload, string note)
        {
            try
            {
                await _db.QueueHikPendingOpDbAsync(d.Id, op, emp, payload);
                return new HikEnrollDeviceResultDto { DeviceId = d.Id, Device = d.Name, Queued = true, Error = note };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue Hik {Op} for employee #{Emp} on device {Device}", op, emp, d.Id);
                return new HikEnrollDeviceResultDto { DeviceId = d.Id, Device = d.Name, Error = "Machine offline and the change could not be queued." };
            }
        }

        private static Dictionary<int, Dictionary<string, JsonNode>> ParseRosters(IEnumerable<(int DeviceId, string? UsersJson)> snaps)
        {
            var map = new Dictionary<int, Dictionary<string, JsonNode>>();
            foreach (var (deviceId, json) in snaps)
            {
                var users = new Dictionary<string, JsonNode>();
                try
                {
                    if (JsonNode.Parse(json ?? "[]") is JsonArray arr)
                        foreach (var u in arr)
                        {
                            var emp = u?["employeeNo"]?.ToString();
                            if (!string.IsNullOrEmpty(emp) && u != null) users[emp] = u;
                        }
                }
                catch (JsonException) { }
                map[deviceId] = users;
            }
            return map;
        }

        private static bool TryValidUntil(string? validUntil, out string validEnd)
        {
            validEnd = NoEndDate;
            if (string.IsNullOrWhiteSpace(validUntil)) return true;
            if (!Regex.IsMatch(validUntil.Trim(), @"^\d{4}-\d{2}-\d{2}$") || !DateTime.TryParse(validUntil, out var d)) return false;
            validEnd = d.ToString("yyyy-MM-dd") + "T23:59:59";
            return true;
        }

        private static bool IsEntrance(string? grp) => (grp ?? "").StartsWith("Entrance", StringComparison.OrdinalIgnoreCase);

        private static int IntOf(JsonNode? n) => int.TryParse(n?.ToString(), out var v) ? v : 0;
    }
}

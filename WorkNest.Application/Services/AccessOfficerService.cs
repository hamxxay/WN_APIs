using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.AccessOfficer;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Access Officer page (role access_officer, own locations; super admins: every location). One job: door access.
    /// - Lookup by CNIC / passport number (letters and digits compared, so 12345-1234567-1 = 1234512345671): every
    ///   confirmed booking that has not ended at the caller's locations where the person is on the room ("assigned") or
    ///   was added by the customer and not checked yet ("pending"), via dbo.WN_AccessOfficer_Lookup (AO_01).
    /// - Add to room: the sales CNIC check (<see cref="IBookingEmployeeService.VerifyByCnicAsync"/>, no over-capacity,
    ///   VerifiedById = the officer).
    /// - Enrol face / fingerprint: <see cref="IHikEnrollmentService"/> (capture on the room machine, copied to every Entrance
    ///   machine, queued for offline ones).
    /// - Remove / restore access: the Access Status tick of the Attendants page (WN_ToggleAccessStatus) + block / unblock
    ///   on the machines. The person stays on the booking.
    /// Every write re-reads the match with all locations and answers 403 when its location is not the caller's.
    /// </summary>
    public class AccessOfficerService : IAccessOfficerService
    {
        public const string NotFoundMessage =
            "No customer has added this CNIC to a confirmed booking at your locations. Ask the customer to add them under " +
            "My Bookings > Employees, or ask the sales team.";
        public const string OtherLocationMessage = "This booking is at a location you are not assigned to.";
        public const string NotOnRoomMessage = "This person is not on a confirmed booking for this room.";

        private readonly IAccessOfficerRepository _repo;
        private readonly IBookingEmployeeService _employees;
        private readonly IAttendantService _attendants;
        private readonly IHikEnrollmentService _hik;
        private readonly IDbRepository _db;
        private readonly ILogger<AccessOfficerService> _logger;

        public AccessOfficerService(IAccessOfficerRepository repo, IBookingEmployeeService employees, IAttendantService attendants,
            IHikEnrollmentService hik, IDbRepository db, ILogger<AccessOfficerService> logger)
        {
            _repo = repo;
            _employees = employees;
            _attendants = attendants;
            _hik = hik;
            _db = db;
            _logger = logger;
        }

        // ---- 1. Lookup -------------------------------------------------------------------------------------------

        public async Task<AccessOfficerOutcome> LookupAsync(string? cnic, AccessOfficerScope scope)
        {
            var key = Key(cnic);
            if (key == null) return Bad(BookingEmployeeService.OtherIdErrorMessage);

            var rows = await _repo.LookupAsync(key, null, null, LocationCsv(scope));
            if (rows.Count == 0) return NotFound(NotFoundMessage);

            var first = rows[0];
            return Ok(new AccessOfficerLookupDto
            {
                Cnic = Display(key),
                Person = new AccessOfficerPersonDto
                {
                    PersonId = first.PersonId,
                    Name = first.Name,
                    Email = Blank(first.Email),
                    Phone = Blank(first.Phone),
                    IdType = first.IdType,
                    IdNumber = BookingEmployeeService.FormatId(first.IdType, first.IdNumber)
                },
                Matches = await ToMatchesAsync(rows)
            });
        }

        // ---- 2. Add to room (customer-added, CNIC not checked yet) -----------------------------------------------------

        public async Task<AccessOfficerOutcome> AddToRoomAsync(AccessOfficerAddToRoomRequest? request, AccessOfficerScope scope)
        {
            if (request == null || request.BookingDetailId <= 0) return Bad("Choose the room.");
            var key = Key(request.Cnic);
            if (key == null) return Bad(BookingEmployeeService.OtherIdErrorMessage);

            var rows = await _repo.LookupAsync(key, null, request.BookingDetailId, null);
            if (rows.Count == 0) return NotFound(NotFoundMessage);
            rows = rows.Where(r => scope.Covers(r.LocationId)).ToList();
            if (rows.Count == 0) return Forbidden(OtherLocationMessage);

            var assigned = rows.FirstOrDefault(r => r.State == AccessOfficerStates.Assigned);
            if (assigned != null) return Conflict($"{assigned.Name} is already on {assigned.Room}.");
            var pending = rows[0];

            // Exactly the sales CNIC check (Attendants & Access "+ Add User"): the request is verified by the officer and
            // the person is assigned to the room, never beyond its seats.
            var outcome = await _employees.VerifyByCnicAsync(request.BookingDetailId, key, scope.UserId,
                allowOverCapacity: false, acceptOtherIds: true);
            if (!outcome.IsSuccessful)
            {
                return outcome.StatusCode == 404
                    ? NotFound(NotFoundMessage)                       // no manual add for this role
                    : new AccessOfficerOutcome { StatusCode = outcome.StatusCode, Message = outcome.Message };
            }

            var after = (await _repo.LookupAsync(key, pending.PersonId, request.BookingDetailId, null))
                .OrderBy(r => r.State == AccessOfficerStates.Assigned ? 0 : 1).ToList();
            var match = after.Count > 0 ? (await ToMatchesAsync(after.Take(1).ToList()))[0] : null;
            return Ok(match, $"{pending.Name} is on {pending.Room} now. Enrol their face or fingerprint.");
        }

        // ---- 3. Enrol face / fingerprint ---------------------------------------------------------------------------

        public async Task<AccessOfficerOutcome> EnrollAsync(string kind, AccessOfficerEnrollRequest? request, AccessOfficerScope scope)
        {
            var type = (kind ?? "").Trim().ToLowerInvariant();
            if (type != "face" && type != "fingerprint") return NotFound("Enrol a face or a fingerprint.");
            var fingerNo = request?.FingerNo ?? 1;
            if (type == "fingerprint" && fingerNo is < 1 or > 10) return Bad("Choose a finger from 1 to 10.");

            var (row, refusal) = await FindAssignedAsync(request, scope);
            if (row == null) return refusal!;
            if (!row.AccessEnabled)
                return Conflict($"{row.Name}'s door access to {row.Room} is removed. Restore access before enrolling.");

            // The capture waits for the person at the room machine (up to about 90 seconds), then the credential is
            // copied to every Entrance machine of the location (queued for offline ones).
            var result = type == "face"
                ? await _hik.EnrollFaceAsync(row.BookingDetailId, row.PersonId, new HikEnrollRequest())
                : await _hik.EnrollFingerprintAsync(row.BookingDetailId, row.PersonId, new HikEnrollRequest { FingerNo = fingerNo });
            return result.Ok
                ? Ok(result, result.Summary ?? (type == "face" ? "Face enrolled." : "Fingerprint enrolled."))
                : new AccessOfficerOutcome { StatusCode = 400, Message = result.Error ?? "Enrolment failed.", Data = result };
        }

        // ---- 4 / 5. Remove / restore door access (the person stays on the booking) ------------------------------------

        public async Task<AccessOfficerOutcome> SetAccessAsync(AccessOfficerPersonRequest? request, bool enable, AccessOfficerScope scope)
        {
            var (row, refusal) = await FindAssignedAsync(request, scope);
            if (row == null) return refusal!;
            if (row.AccessEnabled == enable)
                return Conflict(enable
                    ? $"{row.Name} already has door access to {row.Room}."
                    : $"{row.Name}'s door access to {row.Room} is already removed.");

            // Same as the Attendants page Access Status tick for one person: the status first, then the machines.
            await _attendants.ToggleAccessStatusAsync(new ToggleAccessStatusRequest
            {
                BookingDetailId = row.BookingDetailId,
                CustomerId = row.CustomerId ?? 0,
                PersonId = row.PersonId,
                IsEnabled = enable
            });

            HikAccessSyncResultDto machines;
            try
            {
                machines = await _hik.SetAccessEnabledAsync(row.BookingDetailId, row.PersonId, enable);
            }
            catch (Exception ex)
            {
                // The access status is saved; the machines are retried by the next change / sync. Logged for follow-up.
                _logger.LogError(ex, "Access Officer: {Action} person {PersonId} on the machines of booking detail {BookingDetailId} failed",
                    enable ? "unblocking" : "blocking", row.PersonId, row.BookingDetailId);
                machines = new HikAccessSyncResultDto { Enabled = enable, Error = "The door machines could not be updated right away." };
            }

            var after = (await _repo.LookupAsync(null, row.PersonId, row.BookingDetailId, null))
                .Where(r => r.State == AccessOfficerStates.Assigned).Take(1).ToList();
            var match = after.Count > 0 ? (await ToMatchesAsync(after))[0] : null;

            var customer = string.IsNullOrWhiteSpace(row.Customer) ? "the customer" : row.Customer;
            var message = enable
                ? $"{row.Name}'s door access to {row.Room} and the entrances is back."
                : $"{row.Name}'s door access to {row.Room} and the entrances is removed. They stay on {customer}'s booking.";
            if (machines.Error != null || machines.Devices.Any(d => !d.Ok && !d.Queued))
                message += " Some door machines could not be updated; see the machine list.";
            return Ok(new AccessOfficerAccessChangeDto { Match = match, Machines = machines }, message);
        }

        // ---- helpers ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The person's "assigned" row on this booked space, re-read with every location: 404 when they are not on a
        /// confirmed, running booking for it, 403 when it is at another location, 409 when the customer added them and
        /// nobody checked the CNIC yet.
        /// </summary>
        private async Task<(AccessOfficerMatchRow? Row, AccessOfficerOutcome? Refusal)> FindAssignedAsync(AccessOfficerPersonRequest? request,
            AccessOfficerScope scope)
        {
            if (request == null || request.BookingDetailId <= 0 || request.PersonId <= 0)
                return (null, Bad("Choose the person and the room."));

            var rows = await _repo.LookupAsync(null, request.PersonId, request.BookingDetailId, null);
            if (rows.Count == 0) return (null, NotFound(NotOnRoomMessage));
            rows = rows.Where(r => scope.Covers(r.LocationId)).ToList();
            if (rows.Count == 0) return (null, Forbidden(OtherLocationMessage));

            var assigned = rows.FirstOrDefault(r => r.State == AccessOfficerStates.Assigned);
            if (assigned == null)
                return (null, Conflict($"{rows[0].Name} is waiting for the CNIC check. Add them to {rows[0].Room} first."));
            return (assigned, null);
        }

        /// <summary>Rows with the room machine (online or not), the enrolled credentials and the seats.</summary>
        private async Task<List<AccessOfficerMatchDto>> ToMatchesAsync(List<AccessOfficerMatchRow> rows)
        {
            var devices = (await _db.GetHikDevicesAsync()).ToList();

            Dictionary<int, Dictionary<string, JsonNode>> rosters;
            try
            {
                rosters = HikEnrollmentService.ParseRosters(await _db.GetHikDevCacheSnapshotsDbAsync());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Access Officer: the machines' user lists could not be read; credentials shown as none");
                rosters = new Dictionary<int, Dictionary<string, JsonNode>>();
            }

            var seats = new Dictionary<int, int?>();
            var list = new List<AccessOfficerMatchDto>();
            foreach (var r in rows)
            {
                if (!seats.TryGetValue(r.BookingDetailId, out var total))
                {
                    try { total = (await _attendants.CheckCapacityBeforeAssignAsync(r.BookingDetailId)).RoomCapacity; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Access Officer: seats of booking detail {BookingDetailId} could not be read", r.BookingDetailId);
                        total = null;
                    }
                    seats[r.BookingDetailId] = total;
                }

                var machine = HikEnrollmentService.PickRoomMachine(devices, r.LocationId is int loc ? new[] { loc } : null, r.SpaceCode, r.SpaceName);
                list.Add(new AccessOfficerMatchDto
                {
                    BookingDetailId = r.BookingDetailId,
                    BookingId = r.BookingId,
                    PersonId = r.PersonId,
                    Room = r.Room,
                    SpaceCode = Blank(r.SpaceCode),
                    Location = Blank(r.Location),
                    LocationId = r.LocationId,
                    Customer = Blank(r.Customer),
                    BookingStatus = Blank(r.BookingStatus),
                    StartOn = r.StartOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EndOn = r.EndOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    State = r.State,
                    AccessEnabled = r.State == AccessOfficerStates.Assigned && r.AccessEnabled,
                    Credentials = BookingEmployeeService.CredentialsOn(rosters, r.MachineId),
                    RoomMachine = machine == null ? null : new AccessOfficerRoomMachineDto { Name = machine.Name, Online = machine.Online == 1 },
                    Seats = total is int t ? new AccessOfficerSeatsDto { Used = r.ActiveCount + r.PendingCount, Total = t } : null
                });
            }
            return list;
        }

        /// <summary>The ID compared: letters and digits only, upper case; null when too short / long to be an ID.</summary>
        internal static string? Key(string? cnic)
        {
            var key = BookingEmployeeService.IdKey(cnic);
            return key.Length is >= BookingEmployeeService.MinOtherIdLength and <= BookingEmployeeService.MaxOtherIdLength ? key : null;
        }

        /// <summary>13 digits as 12345-1234567-1; anything else as compared.</summary>
        internal static string Display(string key) =>
            key.Length == 13 && key.All(char.IsDigit) ? $"{key[..5]}-{key[5..12]}-{key[12..]}" : key;

        /// <summary>CSV of the caller's locations for the procedure; null = every location (super admin), "0" = none.</summary>
        internal static string? LocationCsv(AccessOfficerScope scope) =>
            scope.LocationIds == null ? null
            : scope.LocationIds.Count == 0 ? "0"
            : string.Join(",", scope.LocationIds.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static AccessOfficerOutcome Ok(object? data, string message = "Success") => new() { Data = data, Message = message };
        private static AccessOfficerOutcome Bad(string message) => new() { StatusCode = 400, Message = message };
        private static AccessOfficerOutcome Forbidden(string message) => new() { StatusCode = 403, Message = message };
        private static AccessOfficerOutcome NotFound(string message) => new() { StatusCode = 404, Message = message };
        private static AccessOfficerOutcome Conflict(string message) => new() { StatusCode = 409, Message = message };
    }
}

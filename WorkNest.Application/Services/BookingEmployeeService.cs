using System.Collections.Concurrent;
using System.Net.Mail;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WorkNest.Application.DTOs.Attendant;
using WorkNest.Application.DTOs.HikDevice;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Domain.Enums;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// Customer "Employees" on a booking (website My Bookings + app), on top of the Attendants &amp; Access code:
    /// the customer adds people as Pending requests (WN_Persons + WN_CustomerAttendants via WN_AddAttendant, like
    /// staff, + WN_BookingEmployeeRequests); a sales executive verifies them by CNIC at the branch, which assigns them
    /// to the booked space with the same procedure as a staff assignment (WN_AssignAttendantToBooking). Removing an
    /// active person is the staff remove + door access cancelled on the room + Entrance machines.
    /// </summary>
    public class BookingEmployeeService : IBookingEmployeeService
    {
        private const int MaxNameLength = 150;  // WN_Persons.Name
        private const int MaxEmailLength = 150; // WN_Persons.Email

        private readonly IBookingEmployeeRepository _repo;
        private readonly IAttendantService _attendants;
        private readonly IDbRepository _db;
        private readonly IBusinessClock _clock;
        private readonly ILogger<BookingEmployeeService> _logger;
        private readonly IHikEnrollmentService _hik;
        private readonly IHikAccessSuspensionService _suspension;

        /// <summary>One capture at a time per booked room (the customer stands at the room's machine). In-process only.</summary>
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> RoomLocks = new();

        public const string MachineBusyMessage = "The room's machine is busy with another enrolment. Try again in a minute.";
        public const string SuspendedMessage = "Door access is suspended until the overdue challan is paid.";

        public BookingEmployeeService(IBookingEmployeeRepository repo, IAttendantService attendants, IDbRepository db,
            IBusinessClock clock, ILogger<BookingEmployeeService> logger, IHikEnrollmentService hik, IHikAccessSuspensionService suspension)
        {
            _hik = hik;
            _suspension = suspension;
            _repo = repo;
            _attendants = attendants;
            _db = db;
            _clock = clock;
            _logger = logger;
        }

        public async Task<int?> ResolveUserIdAsync(string? email, string? idClaim)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                var (id, _) = await _db.GetUserIdByEmailAsync(email);
                if (id is > 0) return id;
            }
            return int.TryParse(idClaim, out var n) && n > 0 ? n : null;
        }

        // ---- Customer ------------------------------------------------------------------------------

        public async Task<BookingEmployeeOutcome> GetAsync(int bookingId, int userId)
        {
            var (booking, requests) = await _repo.GetAsync(bookingId, userId);
            if (booking == null) return NotFound("Booking not found.");
            var s = await LoadAsync(booking, requests);

            var verified = requests.Where(r => IsStatus(r, "Verified")).Select(r => r.PersonId).ToHashSet();
            var access = await EnrolledCredentialsAsync(s.Active);

            var employees = s.Active.Select(a => new BookingEmployeeDto
            {
                PersonId = a.PersonId,
                Name = a.Name,
                Email = a.Email,
                NeedsEmail = string.IsNullOrWhiteSpace(a.Email),
                Phone = a.Phone,
                IdType = a.IdType,
                IdNumber = FormatId(a.IdType, a.IdNumber),
                AddedOn = a.AssignedFrom == DateTime.MinValue ? null : a.AssignedFrom.ToString("yyyy-MM-dd"),
                Status = "active",
                AddedBy = verified.Contains(a.PersonId) ? "customer" : "staff",
                Access = access.TryGetValue(a.PersonId, out var acc) ? acc : new BookingEmployeeAccessDto()
            }).Concat(s.Pending.Select(r => new BookingEmployeeDto
            {
                PersonId = r.PersonId,
                Name = r.Name,
                Email = r.Email,
                NeedsEmail = string.IsNullOrWhiteSpace(r.Email),
                Phone = r.Phone,
                IdType = r.IdType,
                IdNumber = FormatId(r.IdType, r.IdNumber),
                AddedOn = _clock.FromUtc(r.AddedAt).ToString("yyyy-MM-dd"),
                Status = "active", // no sales check any more: a person added earlier and still pending counts as active
                AddedBy = "customer",
                Access = new BookingEmployeeAccessDto()
            }))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

            var enrollBlock = await EnrollBlockedReasonAsync(booking, checkMachine: true);

            var dto = new BookingEmployeesDto
            {
                BookingId = booking.BookingId,
                Space = booking.SpaceName,
                Location = booking.LocationName,
                Seats = s.Seats,
                Used = s.Used,
                CanManage = s.CanManage,
                Reason = s.Reason,
                CanEnroll = enrollBlock == null,
                EnrollBlockedReason = enrollBlock,
                Employees = employees,
                SavedEmployees = await SavedEmployeesAsync(booking.CustomerId, s)
            };
            return new BookingEmployeeOutcome { Message = "Success", Data = dto };
        }

        public async Task<BookingEmployeeOutcome> AddAsync(int bookingId, int userId, AddBookingEmployeeRequest? body)
        {
            var (booking, requests) = await _repo.GetAsync(bookingId, userId);
            if (booking == null) return NotFound("Booking not found.");

            var mode = (body?.Mode ?? "").Trim().ToLowerInvariant();
            if (body == null || (mode != "new" && mode != "existing"))
                return Bad("Choose how to add the employee: a new person or someone you added before.");

            var s = await LoadAsync(booking, requests);
            if (!s.CanManage) return Bad(s.Reason!);
            if (s.Used >= s.Seats) return Bad(SeatsFull(s.Seats));
            if (booking.CustomerId is not int customerId)
                return Bad("Your customer account could not be found. Please contact WorkNest.");
            var bookingDetailId = booking.BookingDetailId!.Value; // CanManage: the booking has its room

            int personId;
            if (mode == "existing")
            {
                if (body.PersonId is not > 0) return Bad("Choose the employee to add.");
                personId = body.PersonId.Value;
                if (s.IsOnBooking(personId)) return Bad("This person is already on this booking.");
                var saved = await _attendants.GetCustomerAttendantsAsync(customerId);
                if (!saved.Any(r => ToInt(r, "PersonId") == personId))
                    return Bad("Choose someone you added before.");
            }
            else
            {
                var name = Regex.Replace((body.FullName ?? "").Trim(), @"\s+", " ");
                if (name.Length < 2 || name.Length > MaxNameLength)
                    return Bad($"Full name must be 2 to {MaxNameLength} characters.");

                var emailError = EmailError(body.Email, out var email);
                if (emailError != null) return Bad(emailError);

                var phoneError = Phone.Validate(body.Phone);
                if (phoneError != null) return Bad(phoneError);
                var phone = AttendantService.CleanRawPhone(body.Phone ?? "");

                var idTypeText = (body.IdType ?? "").Trim();
                string idType, idNumber;
                if (idTypeText.Equals("CNIC", StringComparison.OrdinalIgnoreCase))
                {
                    idType = "CNIC";
                    if (!Cnic.IsValid(body.IdNumber)) return Bad(Cnic.ErrorMessage);
                    idNumber = Cnic.Digits(body.IdNumber!);
                }
                else if (idTypeText.Equals("Passport", StringComparison.OrdinalIgnoreCase))
                {
                    idType = "Passport";
                    idNumber = Regex.Replace((body.IdNumber ?? "").Trim(), @"[\s-]", "").ToUpperInvariant();
                    if (!Regex.IsMatch(idNumber, "^[A-Z0-9]{6,20}$"))
                        return Bad("Passport number must be 6 to 20 letters or numbers.");
                }
                else return Bad("ID type must be CNIC or Passport.");

                if (s.HasIdNumber(idNumber)) return Bad("Someone with this ID is already on this booking.");

                // Same key as WN_AddAttendant (IdType + IdNumber). A person of this customer is reused as saved; a person
                // registered for someone else is never changed or linked from here (their details would be overwritten).
                var existing = await _repo.FindPersonAsync(idType, idNumber, customerId);
                if (existing != null)
                {
                    if (!existing.LinkedToCustomer)
                        return Bad("This ID is already registered with WorkNest. Please ask WorkNest staff at the branch to add this person.");
                    if (s.IsOnBooking(existing.PersonId)) return Bad("This person is already on this booking.");
                    personId = existing.PersonId;
                    // Their login e-mail for the app's door: the one just typed (same owner, same uniqueness rule).
                    if (!await _repo.SetPersonEmailAsync(personId, email, userId)) return Bad(EmailTakenMessage);
                }
                else
                {
                    if (await _repo.EmailUsedByOtherAsync(email, 0)) return Bad(EmailTakenMessage);
                    // The staff "Add attendant" procedure: WN_Persons + WN_CustomerAttendants.
                    (personId, _) = await _db.AddAttendantSpAsync(name, email, phone, idType, idNumber, customerId);
                }
            }

            // The request row keeps the seat limit atomic (and who added the person); there is no sales check any more, so
            // the person goes on the room at once, with the same procedure as the staff / sales assignment.
            var (result, requestId, _) = await _repo.AddRequestAsync(booking.BookingId, bookingDetailId, customerId, personId, userId, s.Seats);
            if (result == "ok")
            {
                var assignError = await AssignToRoomAsync(bookingDetailId, personId, customerId, userId, removeRequestOnFailure: booking.BookingId);
                if (assignError != null) return Bad(assignError);
            }
            return result switch
            {
                "ok" => new BookingEmployeeOutcome
                {
                    StatusCode = 201,
                    Message = "Employee added. You can now add their fingerprint or face at the room's machine.",
                    Data = new { personId }
                },
                "duplicate" => Bad("This person is already on this booking."),
                "full" => Bad(SeatsFull(s.Seats)),
                _ => new BookingEmployeeOutcome { StatusCode = 500, Message = "The employee could not be added. Please try again." }
            };
        }

        public async Task<BookingEmployeeOutcome> RemoveAsync(int bookingId, int userId, int personId)
        {
            var (booking, requests) = await _repo.GetAsync(bookingId, userId);
            if (booking == null) return NotFound("Booking not found.");
            var s = await LoadAsync(booking, requests);

            var isActive = s.Active.Any(a => a.PersonId == personId);
            var isPending = requests.Any(r => r.PersonId == personId && IsStatus(r, "Pending"));
            if (!isActive && !isPending) return NotFound("This employee is not on this booking.");
            // Removing is allowed at any time (it only takes access away), even when adding is not.

            if (isActive)
            {
                // Exactly the staff remove (DELETE api/bookings/{bookingDetailId}/attendants/{personId}): blocked on the
                // room + Entrance machines (queued for offline machines), then the assignment and access status end.
                var machines = await _attendants.RemoveAttendantAndRevokeAccessAsync(booking.BookingDetailId!.Value, personId);
                if (machines.Error != null || machines.Devices.Any(d => !d.Ok && !d.Queued))
                    _logger.LogWarning("Customer removed person {PersonId} from booking {BookingId}; machines: {Error}",
                        personId, booking.BookingId, machines.Error ?? string.Join("; ", machines.Devices.Where(d => !d.Ok && !d.Queued).Select(d => $"{d.Device}: {d.Error}")));
            }
            await _repo.RemoveRequestsAsync(booking.BookingId, personId);

            return new BookingEmployeeOutcome
            {
                Message = isActive ? "Employee removed and their door access cancelled." : "Employee removed."
            };
        }

        // ---- Customer: e-mail of an employee (the app's door login) ---------------------------------------

        public async Task<BookingEmployeeOutcome> SetEmailAsync(int bookingId, int userId, int personId, SetBookingEmployeeEmailRequest? body)
        {
            var (booking, requests) = await _repo.GetAsync(bookingId, userId);
            if (booking == null) return NotFound("Booking not found.");
            var s = await LoadAsync(booking, requests);
            if (!s.IsOnBooking(personId)) return NotFound("This employee is not on this booking.");
            // Allowed at any time (like remove): it only helps the person sign in.

            var error = EmailError(body?.Email, out var email);
            if (error != null) return Bad(error);
            if (await _repo.EmailUsedByOtherAsync(email, personId) || !await _repo.SetPersonEmailAsync(personId, email, userId))
                return Bad(EmailTakenMessage);
            return new BookingEmployeeOutcome { Message = "Email saved.", Data = new { personId, email } };
        }

        public const string EmailRequiredMessage = "Enter their email: they sign in to the WorkNest app with it to open the room door.";
        public const string EmailInvalidMessage = "Enter a valid email.";
        public const string EmailTakenMessage = "This email is already used by another person.";

        /// <summary>Required, valid format, fits WN_Persons.Email; the cleaned (trimmed, lower case) e-mail comes out.</summary>
        internal static string? EmailError(string? raw, out string email)
        {
            email = (raw ?? "").Trim().ToLowerInvariant();
            if (email.Length == 0) return EmailRequiredMessage;
            if (email.Length > MaxEmailLength || !IsValidEmail(email)) return EmailInvalidMessage;
            return null;
        }

        // ---- Staff: employee history of a booking (CMS) -----------------------------------------------------

        public async Task<List<BookingEmployeeHistoryItemDto>> GetHistoryAsync(int bookingId) =>
            EmployeeHistory.Build(await _repo.GetHistoryAsync(bookingId), _clock.TimeZone);

        // ---- Customer: fingerprint / face on the room machine -------------------------------------------

        public async Task<BookingEmployeeOutcome> EnrollAsync(int bookingId, int userId, int personId, string? kind)
        {
            var (booking, requests) = await _repo.GetAsync(bookingId, userId);
            if (booking == null) return NotFound("Booking not found.");

            var type = (kind ?? "").Trim().ToLowerInvariant();
            if (type != "fingerprint" && type != "face") return Bad("Choose fingerprint or face.");

            var s = await LoadAsync(booking, requests);
            var onRoom = s.Active.FirstOrDefault(a => a.PersonId == personId);
            var waiting = onRoom == null ? s.Pending.FirstOrDefault(p => p.PersonId == personId) : null;
            if (onRoom == null && waiting == null) return Bad("This person is not on this booking.");

            var blocked = await EnrollBlockedReasonAsync(booking, checkMachine: false);
            if (blocked != null) return Bad(blocked);
            var detailId = booking.BookingDetailId!.Value;

            var gate = RoomLocks.GetOrAdd(detailId, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0)) return new BookingEmployeeOutcome { StatusCode = 409, Message = MachineBusyMessage };
            try
            {
                if (onRoom == null)
                {
                    // Added before the sales check was dropped and still pending: put them on the room now (seats are
                    // already counted for them), then enrol.
                    var customerId = booking.CustomerId ?? 0;
                    var assignError = await AssignToRoomAsync(detailId, personId, customerId, userId, removeRequestOnFailure: null);
                    if (assignError != null) return Bad(assignError);
                }

                var result = type == "face"
                    ? await _hik.EnrollFaceAsync(detailId, personId, new HikEnrollRequest())
                    : await _hik.EnrollFingerprintAsync(detailId, personId, new HikEnrollRequest { FingerNo = 1 });
                if (!result.Ok) return Bad(EnrollErrorText(result.Error, type));

                var access = onRoom != null && !string.IsNullOrWhiteSpace(onRoom.MachineId)
                    ? await EnrolledCredentialsAsync(new List<BookingAttendantDetailDto> { onRoom }) : new();
                var acc = access.TryGetValue(personId, out var known) ? known : new BookingEmployeeAccessDto();
                if (type == "face") acc.Face = true; else acc.Fingerprint = true; // the roster cache is a little behind
                return new BookingEmployeeOutcome
                {
                    Message = type == "face" ? "Face added." : "Fingerprint added.",
                    Data = new { personId, kind = type, access = acc }
                };
            }
            finally { gate.Release(); }
        }

        /// <summary>The device's plain wording for the customer (the "no finger in time" case in particular).</summary>
        internal static string EnrollErrorText(string? error, string type)
        {
            if (string.IsNullOrWhiteSpace(error)) return type == "face" ? "The face could not be added. Try again." : "The fingerprint could not be added. Try again.";
            if (error.Contains("in time", StringComparison.OrdinalIgnoreCase) && error.Contains("finger", StringComparison.OrdinalIgnoreCase))
                return "No finger was placed on the machine in time. Try again.";
            return error;
        }

        /// <summary>
        /// Puts the person on the booked room (WN_AssignAttendantToBooking, never over the seats) and marks their request
        /// Verified by the customer. null = done, else the reason. With removeRequestOnFailure (a new add) the request is
        /// removed again so no seat stays taken.
        /// </summary>
        private async Task<string?> AssignToRoomAsync(int bookingDetailId, int personId, int customerId, int userId, int? removeRequestOnFailure)
        {
            try
            {
                await _attendants.AssignAttendantToBookingAsync(new AssignBookingAttendantRequest
                {
                    BookingDetailId = bookingDetailId, PersonId = personId, CustomerId = customerId
                }, allowOverCapacity: false);
            }
            catch (InvalidOperationException ex)
            {
                if (removeRequestOnFailure is int bookingId) await _repo.RemoveRequestsAsync(bookingId, personId);
                return ex.Message;
            }
            catch
            {
                if (removeRequestOnFailure is int bookingId) await _repo.RemoveRequestsAsync(bookingId, personId);
                throw;
            }
            await MarkVerifiedIfRequestedAsync(bookingDetailId, personId, userId);
            return null;
        }

        /// <summary>Why a fingerprint / face can't be added now (null = it can): booking state, overdue challan, room machine.</summary>
        private async Task<string?> EnrollBlockedReasonAsync(BookingEmployeeBookingRow b, bool checkMachine)
        {
            var state = EnrollStateReason(b, _clock.Today);
            if (state != null) return state;
            var detailId = b.BookingDetailId!.Value;
            try
            {
                var suspension = await _suspension.GetAccessSuspensionAsync(detailId);
                if (suspension.Open && suspension.Suspended) return SuspendedMessage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Access suspension of booking detail {BookingDetailId} could not be read", detailId);
            }
            if (!checkMachine) return null;
            try
            {
                var machine = await _hik.GetRoomMachineAsync(detailId);
                if (machine.Checked && machine.Name == null) return machine.Reason;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Room machine of booking detail {BookingDetailId} could not be read", detailId);
            }
            return null;
        }

        /// <summary>Confirmed, not ended (Pakistan date), has a room; else why not (pure).</summary>
        internal static string? EnrollStateReason(BookingEmployeeBookingRow b, DateTime today)
        {
            if (b.BookingStatusId == 1 || b.BookingStatusId == (int)BookingStatus.Pending)
                return "Fingerprint and face can be added once the booking is confirmed.";
            if (b.BookingStatusId != (int)BookingStatus.Confirmed)
                return "This booking is not active, so a fingerprint or face can't be added.";
            var end = b.DetailEnd ?? b.EndOn;
            if (end.HasValue && end.Value.Date < today)
                return "This booking has ended, so a fingerprint or face can't be added.";
            if (b.BookingDetailId == null)
                return "This booking has no room assigned yet. Please contact WorkNest.";
            return null;
        }

        // ---- Sales: CNIC check at the branch -------------------------------------------------------

        public async Task<BookingEmployeeOutcome> VerifyByCnicAsync(int bookingDetailId, string? idNumber, int? staffUserId, bool allowOverCapacity,
            bool acceptOtherIds = false)
        {
            // A CNIC is its 13 digits; with acceptOtherIds (Access Officer) a passport / other ID number is its letters and
            // digits, upper case, as typed.
            string digits;
            if (Cnic.IsValid(idNumber)) digits = Cnic.Digits(idNumber!);
            else if (acceptOtherIds && IdKey(idNumber) is { Length: >= MinOtherIdLength and <= MaxOtherIdLength } key) digits = key;
            else return Bad(acceptOtherIds ? OtherIdErrorMessage : Cnic.ErrorMessage);

            var match = await _repo.FindPendingByIdNumberAsync(bookingDetailId, digits);
            var active = (await _attendants.GetBookingAttendantsAsync(bookingDetailId))
                .FirstOrDefault(a => string.Equals(AttendantService.CleanRawDigitsAndChars(a.IdNumber), digits, StringComparison.OrdinalIgnoreCase));
            if (active != null)
            {
                // Already on the space (e.g. added manually): their open request is done.
                if (match != null && match.PersonId == active.PersonId)
                    await _repo.VerifyAsync(match.RequestId, bookingDetailId, null, staffUserId);
                return new BookingEmployeeOutcome { StatusCode = 409, Message = $"{active.Name} is already on this booking." };
            }
            if (match == null)
                return new BookingEmployeeOutcome
                {
                    StatusCode = 404,
                    Message = "No employee with this CNIC was added by the customer for this booking.",
                    Data = new { canAddManually = true }
                };

            if (!allowOverCapacity)
            {
                var cap = await _attendants.CheckCapacityBeforeAssignAsync(bookingDetailId);
                if (cap.WouldExceedCapacity)
                    return Bad($"{cap.SpaceName} is full ({cap.CurrentActiveAttendants} of {cap.RoomCapacity} seats). Only an admin can add more people to this room.");
            }

            // Claim the request first so two people can't verify (and assign) it twice; reopen it if the assignment fails.
            if (await _repo.VerifyAsync(match.RequestId, bookingDetailId, null, staffUserId) == 0)
                return new BookingEmployeeOutcome { StatusCode = 409, Message = $"{match.Name} has just been verified by someone else." };

            IDictionary<string, object?> assigned;
            try
            {
                assigned = await _attendants.AssignAttendantToBookingAsync(new AssignBookingAttendantRequest
                {
                    BookingDetailId = bookingDetailId,
                    PersonId = match.PersonId,
                    CustomerId = match.CustomerId ?? 0
                }, allowOverCapacity);
            }
            catch
            {
                await _repo.ReopenAsync(match.RequestId);
                throw; // e.g. the room filled up meanwhile: the procedure's message is shown (400)
            }

            var message = "Employee verified and added. Now add their fingerprint or face.";
            if (assigned.TryGetValue("SurchargeInvoiceError", out var invoiceError) && invoiceError is string err)
                message += " " + err;
            return new BookingEmployeeOutcome
            {
                Message = message,
                Data = new
                {
                    personId = match.PersonId,
                    name = match.Name,
                    email = match.Email,
                    phone = match.Phone,
                    idType = match.IdType,
                    idNumber = FormatId(match.IdType, match.IdNumber)
                }
            };
        }

        public async Task MarkVerifiedIfRequestedAsync(int bookingDetailId, int personId, int? staffUserId)
        {
            try
            {
                await _repo.VerifyAsync(null, bookingDetailId, personId, staffUserId);
            }
            catch (BookingEmployeesNotInstalledException) { /* EMP_01 not run yet: no requests exist */ }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not mark the employee request of person {PersonId} on booking detail {BookingDetailId} verified", personId, bookingDetailId);
            }
        }

        public async Task<HashSet<int>> GetPendingOnlyPersonIdsAsync(int customerId)
        {
            try
            {
                var rows = await _repo.GetPendingPersonsAsync(customerId, null);
                return rows.Where(r => !r.IsActiveSomewhere).Select(r => r.PersonId).ToHashSet();
            }
            catch (BookingEmployeesNotInstalledException) { return new HashSet<int>(); }
        }

        public async Task<bool> HasPendingRequestAsync(int bookingDetailId, int personId)
        {
            try
            {
                var rows = await _repo.GetPendingPersonsAsync(null, bookingDetailId);
                return rows.Any(r => r.PersonId == personId);
            }
            catch (BookingEmployeesNotInstalledException) { return false; }
        }

        // ---- helpers ---------------------------------------------------------------------------------

        /// <summary>The booking's people right now: on the room (attendants) + Pending requests of anyone else.</summary>
        private sealed class Snapshot
        {
            public List<BookingAttendantDetailDto> Active { get; set; } = new();
            public List<BookingEmployeeRequestRow> Pending { get; set; } = new();
            public int Seats { get; set; }
            public string? Reason { get; set; }
            public bool CanManage => Reason == null;
            /// <summary>Seats used: Pending requests + people on the room, each person once.</summary>
            public int Used => Active.Count + Pending.Count;

            public bool IsOnBooking(int personId) =>
                Active.Any(a => a.PersonId == personId) || Pending.Any(p => p.PersonId == personId);

            public bool HasIdNumber(string idNumber) =>
                Active.Any(a => SameId(a.IdNumber, idNumber)) || Pending.Any(p => SameId(p.IdNumber, idNumber));

            private static bool SameId(string? stored, string idNumber) =>
                string.Equals(AttendantService.CleanRawDigitsAndChars(stored ?? ""), idNumber, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Snapshot> LoadAsync(BookingEmployeeBookingRow booking, List<BookingEmployeeRequestRow> requests)
        {
            var s = new Snapshot { Reason = NotManageableReason(booking) };
            if (booking.BookingDetailId is int detailId)
            {
                // Same list and seat count as the Attendants & Access page (GET api/bookings/{id}/attendants, capacity-check).
                s.Active = (await _attendants.GetBookingAttendantsAsync(detailId))
                    .GroupBy(a => a.PersonId).Select(g => g.First()).ToList();
                s.Seats = (await _attendants.CheckCapacityBeforeAssignAsync(detailId)).RoomCapacity;
            }
            var onRoom = s.Active.Select(a => a.PersonId).ToHashSet();
            s.Pending = requests
                .Where(r => IsStatus(r, "Pending") && !onRoom.Contains(r.PersonId))
                .GroupBy(r => r.PersonId).Select(g => g.First())
                .ToList();
            return s;
        }

        /// <summary>Employees can be changed on a Confirmed booking that has not ended; otherwise why not.</summary>
        private string? NotManageableReason(BookingEmployeeBookingRow b)
        {
            if (b.BookingStatusId == 1 || b.BookingStatusId == (int)BookingStatus.Pending) // 1 = older "pending" value
                return "Employees can be added once the booking is confirmed.";
            if (b.BookingStatusId != (int)BookingStatus.Confirmed)
                return "This booking is not active, so employees can't be added. You can still remove them.";
            var end = b.DetailEnd ?? b.EndOn;
            if (end.HasValue && end.Value.Date < _clock.Today)
                return "This booking has ended, so employees can't be added. You can still remove them.";
            if (b.BookingDetailId == null)
                return "This booking has no room assigned yet. Please contact WorkNest.";
            return null;
        }

        /// <summary>Fingerprint / face / card per person, from the machines' user lists (HIK roster cache).</summary>
        private async Task<Dictionary<int, BookingEmployeeAccessDto>> EnrolledCredentialsAsync(List<BookingAttendantDetailDto> active)
        {
            var map = new Dictionary<int, BookingEmployeeAccessDto>();
            var enrolled = active.Where(a => !string.IsNullOrWhiteSpace(a.MachineId)).ToList();
            if (enrolled.Count == 0) return map;

            var rosters = HikEnrollmentService.ParseRosters(await _db.GetHikDevCacheSnapshotsDbAsync());
            foreach (var a in enrolled)
                map[a.PersonId] = CredentialsOn(rosters, a.MachineId);
            return map;
        }

        /// <summary>
        /// What a machine user (Machine ID, WN_HIK_PersonMap) has on any machine, from the machines' user lists (HIK roster
        /// cache, <see cref="HikEnrollmentService.ParseRosters"/>): a fingerprint, a face, a card. All false when never
        /// enrolled (no Machine ID).
        /// </summary>
        internal static BookingEmployeeAccessDto CredentialsOn(Dictionary<int, Dictionary<string, JsonNode>> rosters, string? machineId)
        {
            var acc = new BookingEmployeeAccessDto();
            if (string.IsNullOrWhiteSpace(machineId)) return acc;
            foreach (var users in rosters.Values)
            {
                if (!users.TryGetValue(machineId.Trim(), out var u)) continue;
                acc.Fingerprint |= Count(u["numOfFP"]) > 0;
                acc.Face |= Count(u["numOfFace"]) > 0;
                acc.Card |= Count(u["numOfCard"]) > 0;
            }
            return acc;
        }

        /// <summary>Shortest / longest passport or other ID number accepted next to a CNIC (letters and digits).</summary>
        internal const int MinOtherIdLength = 5;
        internal const int MaxOtherIdLength = 30; // WN_Persons.IdNumber
        internal const string OtherIdErrorMessage =
            "Enter the CNIC (13 digits, e.g. 12345-1234567-1) or the passport number.";

        /// <summary>An ID number compared on its letters and digits only, upper case (12345-1234567-1 = 1234512345671).</summary>
        internal static string IdKey(string? idNumber) =>
            AttendantService.CleanRawDigitsAndChars(idNumber ?? "").ToUpperInvariant();

        /// <summary>The customer's people (WN_CustomerAttendants) who are not on this booking.</summary>
        private async Task<List<SavedEmployeeDto>> SavedEmployeesAsync(int? customerId, Snapshot s)
        {
            if (customerId is not int id) return new List<SavedEmployeeDto>();
            var rows = await _attendants.GetCustomerAttendantsAsync(id);
            return rows
                .Select(r => new SavedEmployeeDto
                {
                    PersonId = ToInt(r, "PersonId"),
                    Name = r.TryGetValue("Name", out var n) ? Convert.ToString(n) ?? "" : "",
                    IdType = r.TryGetValue("IdType", out var t) ? Convert.ToString(t) ?? "" : "",
                    IdNumber = FormatId(r.TryGetValue("IdType", out var t2) ? Convert.ToString(t2) : null,
                                        r.TryGetValue("IdNumber", out var x) ? Convert.ToString(x) : null)
                })
                .Where(p => p.PersonId > 0 && !s.IsOnBooking(p.PersonId))
                .GroupBy(p => p.PersonId).Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>A CNIC as 12345-1234567-1 (stored as 13 digits); anything else as stored.</summary>
        internal static string FormatId(string? idType, string? idNumber)
        {
            var v = (idNumber ?? "").Trim();
            if (string.Equals(idType, "Passport", StringComparison.OrdinalIgnoreCase)) return v;
            var d = v.Replace("-", "").Replace(" ", "");
            return d.Length == 13 && d.All(char.IsDigit) ? $"{d[..5]}-{d[5..12]}-{d[12..]}" : v;
        }

        private static string SeatsFull(int seats) => seats == 1
            ? "The only seat is taken. Remove an employee first."
            : $"All {seats} seats are taken. Remove an employee first.";

        private static bool IsValidEmail(string email) =>
            Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") && MailAddress.TryCreate(email, out var a) && a.Address == email;

        private static bool IsStatus(BookingEmployeeRequestRow r, string status) =>
            string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase);

        private static int ToInt(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v != null && v is not DBNull ? Convert.ToInt32(v) : 0;

        private static int Count(JsonNode? n) => int.TryParse(n?.ToString(), out var v) ? v : 0;

        private static BookingEmployeeOutcome Bad(string message) => new() { StatusCode = 400, Message = message };
        private static BookingEmployeeOutcome NotFound(string message) => new() { StatusCode = 404, Message = message };
    }
}

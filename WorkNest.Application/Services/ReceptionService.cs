using System.Globalization;
using System.Text;
using System.Text.Json;
using WorkNest.Application.DTOs.Booking;
using WorkNest.Application.DTOs.Complaint;
using WorkNest.Application.DTOs.Contact;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.DTOs.Reception;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The Receptionist's own endpoints (api/reception/*). Reuses the existing services: tour inquiries
    /// (<see cref="IContactService"/>), complaints, the customer meeting-room booking rules (<see cref="IMeetingRoomService"/>:
    /// entitlement, clashes, 1 to 3 whole hours) and the network dashboard + speed test (UniFi). Answers are
    /// <see cref="MeetingRoomOutcome"/>s (status + message + data), turned into the ApiResponse envelope by the controller.
    /// </summary>
    public sealed class ReceptionService : IReceptionService
    {
        public const string NotYourLocation = "That belongs to a location you are not assigned to.";
        public const string BookingNotFound = "Meeting-room booking not found.";

        private readonly IReceptionRepository _repo;
        private readonly IDbRepository _db;
        private readonly IContactService _contacts;
        private readonly IComplaintService _complaints;
        private readonly IMeetingRoomService _rooms;
        private readonly IBusinessClock _clock;
        private readonly INetworkDashboardService _network;
        private readonly IUnifiService _unifi;

        public ReceptionService(IReceptionRepository repo, IDbRepository db, IContactService contacts, IComplaintService complaints,
            IMeetingRoomService rooms, IBusinessClock clock, INetworkDashboardService network, IUnifiService unifi)
        {
            _repo = repo;
            _db = db;
            _contacts = contacts;
            _complaints = complaints;
            _rooms = rooms;
            _clock = clock;
            _network = network;
            _unifi = unifi;
        }

        // ---- Today ----------------------------------------------------------------------------------------------

        public async Task<MeetingRoomOutcome> GetTodayAsync(IReadOnlyList<int>? locations)
        {
            var today = _clock.Today;
            var tours = (await _repo.GetToursForDayAsync(locations, today))
                .Select(t => new TodayTourDto(t.Id, t.PublicId, t.Name, t.Phone, t.Email, Day(t.PreferredDate), t.PreferredTime, TourStatus(t.StatusId), t.LocationName))
                .ToList();

            var meetings = (await _repo.GetMeetingBookingsAsync(locations, today, today.AddDays(1)))
                .Select(b => new TodayMeetingDto(b.Id, b.SpaceName, b.CustomerName, Time(b.Start), Time(b.End), BookingStatus(b.StatusId), TimeOrNull(b.CheckedInAt), TimeOrNull(b.CheckedOutAt)))
                .ToList();

            var branch = locations == null ? null : new BranchFilter(locations, true, false);
            var open = new List<ComplaintDto>();
            foreach (var status in new[] { ComplaintStatus.Open, ComplaintStatus.InProgress })
                open.AddRange((await _complaints.GetListAsync(1, 100, null, status, branch)).Items);
            var complaints = open.OrderByDescending(c => c.CreatedOn)
                .Select(c => new TodayComplaintDto(c.Id, string.IsNullOrWhiteSpace(c.Category) ? c.ComplaintNo : $"{c.ComplaintNo} {c.Category}".Trim(),
                    c.CustomerName, c.Status, Time(c.CreatedOn), c.LocationName ?? c.Branch))
                .ToList();

            var invoices = (await _repo.GetInvoicesDueAsync(locations, today))
                .Select(i => new TodayInvoiceDto(i.InvoiceId, i.InvoiceNumber, i.CustomerName, Day(i.DueDate), i.Balance))
                .ToList();

            return Ok(new TodayDto(tours, meetings, complaints, invoices, new TodayCountsDto(tours.Count, meetings.Count, complaints.Count, invoices.Count)));
        }

        // ---- Tour inquiries -------------------------------------------------------------------------------------

        public async Task<MeetingRoomOutcome> CreateTourInquiryAsync(TourInquiryRequest? request, string? staffEmail, BranchScope scope)
        {
            if (request is null) return Bad("Inquiry details are required.");
            var name = Clean(request.Name);
            if (name is null) return Bad("Enter the visitor's name.");
            if (name.Length > 200) return Bad("The name can be at most 200 characters.");
            var phoneError = Phone.Validate(request.Phone);
            if (phoneError != null || string.IsNullOrWhiteSpace(request.Phone)) return Bad(phoneError ?? Phone.ErrorMessage);
            var email = Clean(request.Email);
            if (email is { Length: > 256 }) return Bad("The email can be at most 256 characters.");
            var company = Clean(request.Company);
            if (company is { Length: > 200 }) return Bad("The company can be at most 200 characters.");
            var spaceType = Clean(request.SpaceType);
            if (spaceType is { Length: > 100 }) return Bad("The space type can be at most 100 characters.");
            var time = Clean(request.PreferredTime);
            if (time is { Length: > 20 }) return Bad("The preferred time can be at most 20 characters.");
            var notes = Clean(request.Notes);
            if (notes is { Length: > 1000 }) return Bad("The notes can be at most 1000 characters.");
            var source = (request.Source ?? "walk_in").Trim().ToLowerInvariant();
            if (source is not ("walk_in" or "phone" or "other")) return Bad("Source must be walk_in, phone or other.");
            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(request.PreferredDate))
            {
                if (!DateTime.TryParseExact(request.PreferredDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return Bad("The preferred date must be in the format YYYY-MM-DD.");
                date = d;
            }
            if (request.LocationId <= 0) return Bad("Choose the branch.");

            var branch = await _contacts.ChooseBranchAsync(request.LocationId, scope);
            if (branch.IsError) return new MeetingRoomOutcome { StatusCode = branch.ErrorStatus, Message = branch.Error! };

            // The details also go into the message, so they show on the Tour Inquiries list (and are kept before ROLE_01 adds the columns).
            var message = new StringBuilder();
            message.Append("Added at the front desk (").Append(source switch { "phone" => "phone call", "other" => "other", _ => "walk-in" }).Append(").");
            if (company != null) message.Append("\nCompany: ").Append(company);
            if (spaceType != null) message.Append("\nSpace type: ").Append(spaceType);
            if (date != null || time != null)
                message.Append("\nPreferred visit: ").Append(string.Join(" ", new[] { date?.ToString("d MMM yyyy", CultureInfo.InvariantCulture), time }.Where(x => x != null)));
            if (notes != null) message.Append("\nNotes: ").Append(notes);

            var inquiry = new ContactRequest
            {
                FullName = name,
                Email = email ?? string.Empty,
                Phone = request.Phone!.Trim(),
                Message = message.ToString(),
                InquiryType = InquiryTypes.WalkIn
            };
            var result = await _contacts.CreateContactAsync(inquiry, "book_tour", staffEmail, fromStaff: true, branch.LocationId);
            if (!result.IsSuccessful) return Bad(result.Message ?? "The inquiry could not be saved.");

            int? id = null;
            Guid? publicId = null;
            try
            {
                var json = JsonSerializer.SerializeToElement(result.Data);
                if (json.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number) id = i.GetInt32();
                if (json.TryGetProperty("publicId", out var p) && Guid.TryParse(p.ToString(), out var g)) publicId = g;
            }
            catch (JsonException) { }
            var extrasSaved = id is > 0 && await _repo.SaveTourExtrasAsync(id.Value, date, time, company, spaceType);

            return new MeetingRoomOutcome
            {
                StatusCode = 201,
                Message = "Tour inquiry recorded.",
                Data = new { id, publicId, locationId = branch.LocationId, extrasSaved }
            };
        }

        // ---- Meeting rooms --------------------------------------------------------------------------------------

        public async Task<MeetingRoomOutcome> GetMeetingCalendarAsync(IReadOnlyList<int>? locations, string? from, string? to)
        {
            if (!TryDay(from, out var start, _clock.Today) || !TryDay(to, out var end, start.AddDays(1)))
                return Bad("Dates must be in the format YYYY-MM-DD.");
            // "to" is a day: the whole day is included.
            var until = string.IsNullOrWhiteSpace(to) ? start.AddDays(1) : end.AddDays(1);
            if (until <= start) return Bad("The end date cannot be before the start date.");
            if ((until - start).TotalDays > 62) return Bad("Choose at most 2 months at a time.");

            var rooms = (await _repo.GetMeetingRoomsAsync(locations))
                .Select(r => new MeetingRoomItemDto(r.SpaceId, r.Name, r.LocationName, r.Capacity)).ToList();
            var bookings = (await _repo.GetMeetingBookingsAsync(locations, start, until)).Select(ToDto).ToList();
            return Ok(new MeetingCalendarDto(rooms, bookings));
        }

        public async Task<MeetingRoomOutcome> BookMeetingRoomAsync(ReceptionMeetingBookRequest? request, IReadOnlyList<int>? locations, int staffUserId)
        {
            if (request is null) return Bad("Booking details are required.");
            if (request.CustomerId <= 0) return Bad("Choose the customer.");
            if (request.SpaceId <= 0) return Bad("Choose a meeting room.");
            var start = MeetingRoomService.ParseStartOn(request.Start, _clock);
            var end = MeetingRoomService.ParseStartOn(request.End, _clock);
            if (start is null || end is null) return Bad("Choose a valid start and end date and time.");
            var minutes = (end.Value - start.Value).TotalMinutes;
            if (minutes < 60 || minutes > 180 || minutes % 60 != 0) return Bad("A meeting room is booked for 1, 2 or 3 whole hours.");

            // The room must be at one of the caller's locations (the customer's own entitlement and clashes are checked by the booking rules).
            var rooms = await _repo.GetMeetingRoomsAsync(locations);
            if (rooms.All(r => r.SpaceId != request.SpaceId))
                return new MeetingRoomOutcome { StatusCode = 403, Message = NotYourLocation };

            var outcome = await _rooms.BookAsync(request.CustomerId, new FreeMeetingRoomBookRequest
            {
                SpaceId = request.SpaceId,
                StartOn = start.Value.ToString("s", CultureInfo.InvariantCulture),
                DurationHours = (int)(minutes / 60),
                Notes = "Booked at the front desk."
            });
            if (!outcome.IsSuccessful) return outcome;

            var bookingId = (outcome.Data as FreeMeetingRoomBookedDto)?.BookingId ?? 0;
            if (bookingId <= 0) return outcome;
            await _repo.SetBookingCreatorAsync(bookingId, staffUserId);
            var row = await _repo.GetMeetingBookingAsync(bookingId);
            return row == null ? outcome : new MeetingRoomOutcome { StatusCode = 201, Message = outcome.Message, Data = ToDto(row) };
        }

        public async Task<MeetingRoomOutcome> CancelMeetingRoomAsync(int bookingId, IReadOnlyList<int>? locations, int staffUserId)
        {
            var (row, error) = await FindAsync(bookingId, locations);
            if (error != null) return error;
            if (!row!.IsFree)
                return new MeetingRoomOutcome { StatusCode = 409, Message = "Only a booking made with free meeting-room hours can be cancelled here." };
            var outcome = await _rooms.CancelAsync(row.CustomerId, bookingId, new FreeMeetingRoomCancelRequest { CancelReason = "Cancelled at the front desk." });
            if (!outcome.IsSuccessful) return outcome;
            var after = await _repo.GetMeetingBookingAsync(bookingId);
            return new MeetingRoomOutcome { StatusCode = 200, Message = outcome.Message, Data = after is null ? null : ToDto(after) };
        }

        public async Task<MeetingRoomOutcome> CheckInAsync(int bookingId, IReadOnlyList<int>? locations, int staffUserId)
        {
            var (row, error) = await FindAsync(bookingId, locations);
            if (error != null) return error;
            if (row!.StatusId != 33) return new MeetingRoomOutcome { StatusCode = 409, Message = "Only a confirmed booking can be checked in." };
            if (row.CheckedInAt != null) return new MeetingRoomOutcome { StatusCode = 409, Message = "This booking is already checked in." };
            if (!await _repo.SetCheckInAsync(bookingId, _clock.Now, staffUserId > 0 ? staffUserId : null))
                return new MeetingRoomOutcome { StatusCode = 409, Message = "This booking could not be checked in." };
            return Ok((await _repo.GetMeetingBookingAsync(bookingId)) is { } after ? ToDto(after) : null, "Checked in.");
        }

        public async Task<MeetingRoomOutcome> CheckOutAsync(int bookingId, IReadOnlyList<int>? locations, int staffUserId)
        {
            var (row, error) = await FindAsync(bookingId, locations);
            if (error != null) return error;
            if (row!.CheckedInAt == null) return new MeetingRoomOutcome { StatusCode = 409, Message = "Check the booking in first." };
            if (row.CheckedOutAt != null) return new MeetingRoomOutcome { StatusCode = 409, Message = "This booking is already checked out." };
            if (!await _repo.SetCheckOutAsync(bookingId, _clock.Now, staffUserId > 0 ? staffUserId : null))
                return new MeetingRoomOutcome { StatusCode = 409, Message = "This booking could not be checked out." };
            return Ok((await _repo.GetMeetingBookingAsync(bookingId)) is { } after ? ToDto(after) : null, "Checked out.");
        }

        /// <summary>The booking, or the 404 (not found) / 403 (a room of another location) answer.</summary>
        private async Task<(ReceptionBookingRow? Row, MeetingRoomOutcome? Error)> FindAsync(int bookingId, IReadOnlyList<int>? locations)
        {
            if (bookingId <= 0) return (null, new MeetingRoomOutcome { StatusCode = 404, Message = BookingNotFound });
            var row = await _repo.GetMeetingBookingAsync(bookingId);
            if (row == null) return (null, new MeetingRoomOutcome { StatusCode = 404, Message = BookingNotFound });
            if (locations != null && (row.LocationId is not { } loc || !locations.Contains(loc)))
                return (null, new MeetingRoomOutcome { StatusCode = 403, Message = NotYourLocation });
            return (row, null);
        }

        // ---- Network --------------------------------------------------------------------------------------------

        public async Task<MeetingRoomOutcome> GetNetworkAsync(IReadOnlyList<int>? locations, CancellationToken ct = default)
        {
            var names = await LocationNamesAsync();
            var ids = locations ?? names.Keys.OrderBy(i => i).ToList();
            List<SpeedtestHistoryItem>? history = null;
            var list = new List<ReceptionNetworkDto>();
            foreach (var id in ids)
            {
                if (!names.TryGetValue(id, out var name)) continue; // inactive / unknown location
                var dash = await _network.GetAsync(new NetworkScope(locations, id), ct);
                var lines = dash.Internet?.Lines ?? new List<NetworkWanDto>();
                var supported = dash.Available && lines.Any(l => l.Live);
                var tests = new List<ReceptionSpeedTestDto>();
                if (supported)
                {
                    history ??= await SafeHistoryAsync();
                    tests = history.Select(h => new ReceptionSpeedTestDto(IsoMs(h.AtMs), h.DownMbps, h.UpMbps, h.PingMs)).ToList();
                }
                var latestLine = lines.Where(l => l.Speedtest != null).OrderByDescending(l => l.Speedtest!.At).FirstOrDefault()?.Speedtest;
                var last = latestLine != null ? new ReceptionSpeedTestDto(latestLine.At, latestLine.DownMbps, latestLine.UpMbps, latestLine.PingMs)
                    : tests.FirstOrDefault();
                var online = dash.Available && ((dash.Internet is { LinesTotal: > 0 } net) ? net.LinesUp > 0 : (dash.Devices?.Online ?? 0) > 0);
                var clients = (dash.Clients?.Total ?? 0) + (dash.Gwn?.Clients?.Total ?? 0);
                list.Add(new ReceptionNetworkDto(id, name, online, dash.Internet?.UptimePct, null, clients, last, tests, supported));
            }
            return Ok(list);
        }

        public async Task<MeetingRoomOutcome> StartSpeedtestAsync(int locationId, IReadOnlyList<int>? locations, CancellationToken ct = default)
        {
            if (locationId <= 0) return Bad("Choose the location.");
            if (locations != null && !locations.Contains(locationId)) return new MeetingRoomOutcome { StatusCode = 403, Message = NotYourLocation };
            var dash = await _network.GetAsync(new NetworkScope(locations, locationId), ct);
            var live = (dash.Internet?.Lines ?? new List<NetworkWanDto>()).Where(l => l.Live && l.Up).ToList();
            var line = live.FirstOrDefault(l => string.Equals(l.Role, "Primary", StringComparison.OrdinalIgnoreCase)) ?? live.FirstOrDefault();
            if (!dash.Available || line == null)
                return Ok(new { started = false, message = "A speed test cannot be run for this location (its internet line is not on the monitored gateway, or it is down)." },
                    "A speed test is not available here.");
            try
            {
                await _unifi.StartSpeedtestAsync(line.Label.ToUpperInvariant());
                return Ok(new { started = true, message = "The speed test is running. It takes about 30 seconds; refresh the network page afterwards." }, "Speed test started.");
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("A speed test is already running", StringComparison.Ordinal))
            {
                return new MeetingRoomOutcome { StatusCode = 409, Message = "A speed test is already running. Wait for it to finish (about 30 seconds).", Data = new { started = false, message = ex.Message } };
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unknown WAN", StringComparison.Ordinal))
            {
                return Ok(new { started = false, message = "This line cannot be tested: the gateway does not report it." }, "A speed test is not available here.");
            }
            catch (Exception)
            {
                return new MeetingRoomOutcome { StatusCode = 502, Message = "The network could not be reached to run the speed test. Try again in a minute." };
            }
        }

        private async Task<List<SpeedtestHistoryItem>> SafeHistoryAsync()
        {
            try { return await _unifi.GetSpeedtestHistoryAsync(10); }
            catch { return new List<SpeedtestHistoryItem>(); } // history is a nicety: the status still shows
        }

        private async Task<Dictionary<int, string>> LocationNamesAsync()
        {
            var map = new Dictionary<int, string>();
            foreach (var o in BranchScope.Options(await _db.GetAllLocationsAsync())) map[o.Id] = o.Name ?? $"Location {o.Id}";
            return map;
        }

        // ---- helpers --------------------------------------------------------------------------------------------

        private static MeetingBookingDto ToDto(ReceptionBookingRow b) =>
            new(b.Id, b.SpaceId, b.CustomerId, b.CustomerName, Time(b.Start), Time(b.End), BookingStatus(b.StatusId),
                TimeOrNull(b.CheckedInAt), TimeOrNull(b.CheckedOutAt), b.BookedBy);

        private static string TourStatus(int id) => id switch { 1 => "new", 2 => "in_progress", 3 => "resolved", _ => "new" };

        private static string BookingStatus(int id) => id switch { 33 => "confirmed", 5 => "pending", 6 => "no_show", 86 => "cancelled", _ => "booked" };

        private static string? Day(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string Time(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        private static string? TimeOrNull(DateTime? d) => d is null ? null : Time(d.Value);
        private static string IsoMs(double ms) => DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static bool TryDay(string? text, out DateTime day, DateTime fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) { day = fallback.Date; return true; }
            return DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        private static MeetingRoomOutcome Ok(object? data, string message = "Success") => new() { StatusCode = 200, Message = message, Data = data };
        private static MeetingRoomOutcome Bad(string message) => new() { StatusCode = 400, Message = message };
    }
}

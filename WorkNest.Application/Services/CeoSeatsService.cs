using System.Globalization;
using WorkNest.Application.DTOs.Dashboard;
using WorkNest.Application.Helpers;
using WorkNest.Application.Interfaces;
using WorkNest.Common.Responses;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// CEO dashboard Seats section (GET api/ceo/seats, procedure dbo.WN_Ceo_Seats, script SEAT_01_ceo_seats). The
    /// dashboard's occupancy counts rooms (a 9-seat office sold for 5 seats is "occupied"); this counts seats, on the rooms
    /// the Rooms section shows (private offices and shared spaces sold by the month, WN_Spaces.Status = 1).
    /// <list type="bullet">
    /// <item>Seats of a room = WN_Spaces.Capacity (1 when not set). Booking seats = its Capacity when above 0, else the room's.</item>
    /// <item>CP_02: booking seats = its Capacity, else its quotation's, else the room's; at most the room's seats.</item>
    /// <item>Shift rule (<see cref="SeatsSold"/>): a seat is sold when any shift on it is sold. Seats sold in a room = 24/7 seats
    /// + the larger of Morning and Evening seats (a Morning and an Evening booking share the same desks), at most the room's
    /// seats. Seats sold for one of Morning / Evening only count as sold and are shown as "one shift free".</item>
    /// <item>Revenue per seat = monthly rent of the confirmed bookings running now after the booking discount (as the Rooms
    /// section) / seats sold, against the average list price per seat of the sold seats (WN_Spaces.Price, else the active
    /// monthly seat price, as RM_01); CP_02: against the rent expected at the list price (Morning / Evening at the list
    /// less the shift discount cap, as the Rooms section).</item>
    /// <item>Rent not earned = empty seats x their room's list price per seat ("vacancy cost a month").</item>
    /// </list>
    /// "Now" figures are for right now (Pakistan time) and follow the location only; the period figures (seat-days and
    /// room-days) follow the period too; the 12 months are month ends. Before SEAT_01: available = false and the message.
    /// </summary>
    public class CeoSeatsService : ICeoSeatsService
    {
        private static readonly CultureInfo En = CultureInfo.InvariantCulture;

        /// <summary>The shifts, in the order byShift lists them.</summary>
        public static readonly IReadOnlyList<string> Shifts = new[] { "24/7", "Morning", "Evening" };

        /// <summary>The name of the group of rooms with no location.</summary>
        public const string NoLocation = "No location";

        private readonly ICeoSeatsRepository _repo;
        private readonly IBusinessClock _clock;

        public CeoSeatsService(ICeoSeatsRepository repo, IBusinessClock clock)
        {
            _repo = repo;
            _clock = clock;
        }

        public async Task<ApiResponse> GetSeatsAsync(string? period, int? locationId, string? from = null, string? to = null)
        {
            if (!DashboardPeriod.TryParse(period, from, to, _clock.Today, out var p, out var periodError)) return ApiResponse.Fail(periodError!);
            var now = _clock.Now;
            var dto = new CeoSeatsDto
            {
                Period = p.Key,
                PeriodLabel = p.Label(now),
                From = p.FromText,
                To = p.ToText,
                GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", En),
                AsOf = now.ToString("yyyy-MM-dd", En)
            };
            List<List<IDictionary<string, object?>>> s;
            try
            {
                s = await _repo.GetSeatsAsync(locationId is > 0 ? locationId : null, p.Months, now, p.From, p.To);
            }
            catch (CeoSeatsDatabaseUpdateMissingException)
            {
                dto.Available = false;
                dto.Message = CeoSeatsDatabaseUpdateMissingException.UserMessage;
                return ApiResponse.Ok(dto);
            }

            var rooms = new List<CeoSeatRoomDto>();
            var seen = new HashSet<int>();
            foreach (var r in Set(s, 0))
            {
                var room = RoomOf(r);
                if (seen.Add(room.SpaceId)) rooms.Add(room);
            }
            dto.Rooms = SortRooms(rooms);
            dto.Summary = Summarize(dto.Rooms, "");
            dto.ByShift = ShiftsOf(dto.Rooms);
            dto.ByLocation = ByLocation(dto.Rooms);
            dto.BySpaceType = BySpaceType(dto.Rooms);
            dto.Months = Set(s, 1).Select(MonthOf).ToList();
            return ApiResponse.Ok(dto);
        }

        // ------------------------------------------------------------------ the rules

        /// <summary>
        /// Seats sold in a room of <paramref name="seats"/> seats with the given shift seats running (the shift rule): 24/7
        /// seats + the larger of Morning and Evening seats, at most the room's seats (never below 0). OneShift = the sold
        /// seats that have only one of Morning / Evening (the other shift could still be sold on them).
        /// </summary>
        public static (int Sold, int OneShift) SeatsSold(int seats, int seats247, int morning, int evening)
        {
            seats = Math.Max(0, seats);
            seats247 = Math.Max(0, seats247);
            morning = Math.Max(0, morning);
            evening = Math.Max(0, evening);
            var sold = Math.Min(seats, seats247 + Math.Max(morning, evening));
            var shiftSeats = sold - Math.Min(seats, seats247);          // seats taken by Morning / Evening bookings
            var both = Math.Min(Math.Min(morning, evening), shiftSeats);  // of those, with a Morning and an Evening booking
            return (sold, shiftSeats - both);
        }

        /// <summary>
        /// One room from the procedure's row: seats (1 when the capacity is not set), seats sold now by the shift rule,
        /// empty seats, sold monthly rent and per seat, rent not earned (empty x list price per seat; null without a list
        /// price) and the period percentages.
        /// </summary>
        public static CeoSeatRoomDto RoomOf(IDictionary<string, object?> r)
        {
            var seats = Int(r, "Seats");
            if (seats <= 0) seats = Math.Max(1, Int(r, "Capacity"));
            int s247 = Int(r, "Seats247Now"), morning = Int(r, "SeatsMorningNow"), evening = Int(r, "SeatsEveningNow");
            var (sold, oneShift) = SeatsSold(seats, s247, morning, evening);
            var list = Val(r, "ListSeatPrice") is { } lp && Convert.ToDecimal(lp, En) > 0 ? Money(Convert.ToDecimal(lp, En)) : (decimal?)null;
            var soldMonthly = Money(Dec(r, "SoldMonthlyNow"));
            var empty = seats - sold;
            var bookings = Int(r, "BookingsNow");
            var periodDays = Int(r, "PeriodDays");
            var seatDays = Int(r, "FilledSeatDays");
            var daysWithBooking = Int(r, "OccupiedDays");
            return new CeoSeatRoomDto
            {
                SpaceId = Int(r, "SpaceId"),
                Name = Str(r, "Name"),
                Code = Str(r, "Code"),
                LocationId = OptInt(r, "LocationId"),
                Location = Str(r, "Location"),
                SpaceTypeId = OptInt(r, "SpaceTypeId"),
                SpaceType = Str(r, "SpaceType"),
                CategoryCode = Str(r, "CategoryCode"),
                Seats = seats,
                SeatsSold = sold,
                EmptySeats = empty,
                SeatOccupancyPct = Pct(sold, seats),
                Seats247 = s247,
                SeatsMorning = morning,
                SeatsEvening = evening,
                OneShiftSeats = oneShift,
                BookingsNow = bookings,
                Bookings247 = Int(r, "Bookings247Now"),
                BookingsMorning = Int(r, "BookingsMorningNow"),
                BookingsEvening = Int(r, "BookingsEveningNow"),
                OccupiedNow = bookings > 0 || s247 + morning + evening > 0,
                ListSeatPrice = list,
                SoldMonthly = soldMonthly,
                RevenuePerSeat = sold > 0 ? Money(soldMonthly / sold) : 0m,
                ExpectedMonthly = Val(r, "ExpectedMonthlyNow") is { } ex ? Money(Convert.ToDecimal(ex, En)) : null,   // CP_02
                RentNotEarned = list is { } l ? Money(empty * l) : null,
                PeriodSeatDaysSold = seatDays,
                PeriodDaysWithBooking = daysWithBooking,
                PeriodDays = periodDays,
                PeriodSeatOccupancyPct = Pct(seatDays, seats * periodDays),
                PeriodRoomOccupancyPct = Pct(daysWithBooking, periodDays)
            };
        }

        /// <summary>
        /// Totals of a set of rooms (see <see cref="CeoSeatGroupDto"/>): occupancy of rooms and seats now and the gap
        /// between them, partly filled rooms, revenue per sold seat against the list price of the sold seats, rent not
        /// earned, and the period occupancy from the seat-days / room-days of every room.
        /// </summary>
        public static CeoSeatGroupDto Summarize(IReadOnlyCollection<CeoSeatRoomDto> rooms, string name, int? locationId = null, int? spaceTypeId = null)
        {
            int seats = rooms.Sum(r => r.Seats), sold = rooms.Sum(r => r.SeatsSold), occupied = rooms.Count(r => r.OccupiedNow);
            var soldMonthly = rooms.Sum(r => r.SoldMonthly);
            var priced = rooms.Where(r => r.ListSeatPrice is > 0).ToList();
            var pricedSold = priced.Where(r => r.SeatsSold > 0).ToList();
            int pricedSoldSeats = pricedSold.Sum(r => r.SeatsSold), pricedSeats = priced.Sum(r => r.Seats);
            decimal? listSold = pricedSoldSeats > 0 ? Money(pricedSold.Sum(r => r.SeatsSold * r.ListSeatPrice!.Value) / pricedSoldSeats) : null;
            decimal? listAll = pricedSeats > 0 ? Money(priced.Sum(r => r.Seats * r.ListSeatPrice!.Value) / pricedSeats) : null;
            decimal? vsList = null;
            // CP_02: sold against the rent expected at the list price (shift seats at the shift price) when every priced
            // room sold has it; else (before CP_02) sold per seat against the list per seat.
            var expected = pricedSold.Sum(r => r.ExpectedMonthly ?? 0m);
            if (pricedSold.Count > 0 && pricedSold.All(r => r.ExpectedMonthly is > 0))
                vsList = Math.Round((pricedSold.Sum(r => r.SoldMonthly) / expected - 1) * 100m, 1, MidpointRounding.AwayFromZero);
            else if (pricedSoldSeats > 0 && listSold is > 0)
                vsList = Math.Round((pricedSold.Sum(r => r.SoldMonthly) / pricedSoldSeats / listSold.Value - 1) * 100m, 1, MidpointRounding.AwayFromZero);
            var roomPct = Pct(occupied, rooms.Count);
            var seatPct = Pct(sold, seats);
            var partly = rooms.Where(r => r.OccupiedNow && r.EmptySeats > 0).ToList();
            return new CeoSeatGroupDto
            {
                LocationId = locationId,
                SpaceTypeId = spaceTypeId,
                Name = name,
                Rooms = rooms.Count,
                RoomsOccupied = occupied,
                RoomOccupancyPct = roomPct,
                Seats = seats,
                SeatsSold = sold,
                EmptySeats = seats - sold,
                SeatOccupancyPct = seatPct,
                GapPts = roomPct - seatPct,
                PartlyFilledRooms = partly.Count,
                PartlyFilledEmptySeats = partly.Sum(r => r.EmptySeats),
                OneShiftSeats = rooms.Sum(r => r.OneShiftSeats),
                SoldMonthly = Money(soldMonthly),
                RevenuePerSeat = sold > 0 ? Money(soldMonthly / sold) : 0m,
                ListSeatPriceSold = listSold,
                ListSeatPriceAll = listAll,
                RevenueVsListPct = vsList,
                RentNotEarned = Money(rooms.Sum(r => r.RentNotEarned ?? 0m)),
                EmptySeatsUnpriced = rooms.Where(r => r.ListSeatPrice is not > 0).Sum(r => r.EmptySeats),
                PeriodSeatOccupancyPct = Pct(rooms.Sum(r => r.PeriodSeatDaysSold), rooms.Sum(r => r.Seats * r.PeriodDays)),
                PeriodRoomOccupancyPct = Pct(rooms.Sum(r => r.PeriodDaysWithBooking), rooms.Sum(r => r.PeriodDays)),
                PeriodDays = rooms.Count > 0 ? rooms.Max(r => r.PeriodDays) : 0
            };
        }

        /// <summary>Seats and bookings of each shift running now (always 24/7, Morning, Evening), before the room cap.</summary>
        public static List<CeoSeatShiftDto> ShiftsOf(IReadOnlyCollection<CeoSeatRoomDto> rooms) => new()
        {
            new CeoSeatShiftDto { Shift = Shifts[0], Seats = rooms.Sum(r => r.Seats247), Bookings = rooms.Sum(r => r.Bookings247) },
            new CeoSeatShiftDto { Shift = Shifts[1], Seats = rooms.Sum(r => r.SeatsMorning), Bookings = rooms.Sum(r => r.BookingsMorning) },
            new CeoSeatShiftDto { Shift = Shifts[2], Seats = rooms.Sum(r => r.SeatsEvening), Bookings = rooms.Sum(r => r.BookingsEvening) }
        };

        /// <summary>One group per location, by name (rooms without a location last, as <see cref="NoLocation"/>).</summary>
        public static List<CeoSeatGroupDto> ByLocation(IReadOnlyCollection<CeoSeatRoomDto> rooms) =>
            rooms.GroupBy(r => r.LocationId)
                .Select(g => Summarize(g.ToList(), g.Key == null ? NoLocation : g.Select(r => r.Location).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"Location {g.Key}", g.Key))
                .OrderBy(x => x.LocationId == null)
                .ThenBy(x => x.Name, Natural)
                .ToList();

        /// <summary>One group per space type, most seats first (then by name).</summary>
        public static List<CeoSeatGroupDto> BySpaceType(IReadOnlyCollection<CeoSeatRoomDto> rooms) =>
            rooms.GroupBy(r => r.SpaceTypeId)
                .Select(g => Summarize(g.ToList(), g.Select(r => r.SpaceType).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Other", spaceTypeId: g.Key))
                .OrderByDescending(x => x.Seats)
                .ThenBy(x => x.Name, Natural)
                .ToList();

        /// <summary>A month of the 12-month series: rooms / seats in scope, rooms with a booking and seats sold at month end.</summary>
        public static CeoSeatMonthDto MonthOf(IDictionary<string, object?> r)
        {
            int rooms = Int(r, "Rooms"), occupied = Int(r, "RoomsOccupied"), seats = Int(r, "Seats"), sold = Math.Min(Int(r, "SeatsSold"), Int(r, "Seats"));
            return new CeoSeatMonthDto
            {
                Month = Str(r, "Month") ?? "",
                Rooms = rooms,
                RoomsOccupied = occupied,
                RoomOccupancyPct = Pct(occupied, rooms),
                Seats = seats,
                SeatsSold = sold,
                SeatOccupancyPct = Pct(sold, seats)
            };
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>By location (rooms without one last), then room code, then name; numbers in order (9 before 10).</summary>
        private static List<CeoSeatRoomDto> SortRooms(IEnumerable<CeoSeatRoomDto> rooms) =>
            rooms.OrderBy(r => string.IsNullOrWhiteSpace(r.Location))
                .ThenBy(r => r.Location ?? "", Natural)
                .ThenBy(r => r.Code ?? r.Name ?? "", Natural)
                .ThenBy(r => r.Name ?? "", Natural)
                .ThenBy(r => r.SpaceId)
                .ToList();

        /// <summary>Compares text with runs of digits taken as numbers ("Office 9" before "Office 10"), case-insensitive.</summary>
        private static readonly IComparer<string> Natural = Comparer<string>.Create((a, b) =>
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var x = a[si..i].TrimStart('0');
                    var y = b[sj..j].TrimStart('0');
                    var c = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
                    if (c != 0) return c;
                }
                else
                {
                    var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (c != 0) return c;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        });

        private static List<IDictionary<string, object?>> Set(List<List<IDictionary<string, object?>>> s, int i) => i < s.Count ? s[i] : new();
        private static object? Val(IDictionary<string, object?>? r, string key) => r != null && r.TryGetValue(key, out var v) ? v : null;
        private static int Int(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v, En) : 0;
        private static int? OptInt(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToInt32(v, En) : null;
        private static decimal Dec(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToDecimal(v, En) : 0m;
        private static string? Str(IDictionary<string, object?>? r, string key) => Val(r, key) is { } v ? Convert.ToString(v, En) : null;

        /// <summary>part / total as a percent with one decimal (0 when total is 0).</summary>
        private static decimal Pct(long part, long total) => total > 0 ? Math.Round(part * 100m / total, 1, MidpointRounding.AwayFromZero) : 0m;

        /// <summary>PKR, 2 decimals (half away from zero).</summary>
        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}

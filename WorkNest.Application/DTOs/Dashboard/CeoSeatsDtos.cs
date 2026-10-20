using System.Text.Json.Serialization;

namespace WorkNest.Application.DTOs.Dashboard
{
    /// <summary>
    /// GET api/ceo/seats (SEAT_01): the seats of the rooms sold by the month (private offices, shared spaces) where the
    /// dashboard's occupancy counts rooms, so a half-filled office shows. Seats sold now (a seat is sold when any shift on
    /// it is sold: 24/7 seats + the larger of Morning and Evening seats, at most the room's seats) against room occupancy,
    /// revenue per sold seat against the list price per seat, the rent the empty seats could earn a month, by location,
    /// by space type, per room, in the period and over the last 12 months. Before the database update SEAT_01:
    /// Available = false with Message, no summary, empty lists.
    /// </summary>
    public class CeoSeatsDto
    {
        public bool Available { get; set; } = true;
        /// <summary>Set when Available is false: which script to run (no word "SQL": the CMS and the app hide such messages).</summary>
        public string? Message { get; set; }
        /// <summary>month | quarter | year | custom</summary>
        public string Period { get; set; } = "month";
        /// <summary>e.g. "October 2026", "Q4 2026", "2026", "1 Oct - 15 Oct 2026".</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>Custom period: the first and last day of the range, yyyy-MM-dd; null for month / quarter / year.</summary>
        public string? From { get; set; }
        public string? To { get; set; }
        /// <summary>UTC, ISO 8601 with Z.</summary>
        public string GeneratedAt { get; set; } = "";
        /// <summary>The Pakistan day of the "now" figures, yyyy-MM-dd.</summary>
        public string AsOf { get; set; } = "";
        /// <summary>Every room in scope. Null when Available is false.</summary>
        public CeoSeatGroupDto? Summary { get; set; }
        /// <summary>Seats sold now per shift (the bookings' seats, before the room cap): always 24/7, Morning, Evening.</summary>
        public List<CeoSeatShiftDto> ByShift { get; set; } = new();
        /// <summary>By location name (rooms without a location last, LocationId null).</summary>
        public List<CeoSeatGroupDto> ByLocation { get; set; } = new();
        /// <summary>Most seats first.</summary>
        public List<CeoSeatGroupDto> BySpaceType { get; set; } = new();
        /// <summary>The last 12 months, oldest first (month end; this month: now).</summary>
        public List<CeoSeatMonthDto> Months { get; set; } = new();
        /// <summary>Every room in scope, by location then room code (9 before 10).</summary>
        public List<CeoSeatRoomDto> Rooms { get; set; } = new();
    }

    /// <summary>Seat and room figures of a set of rooms (all, one location or one space type).</summary>
    public class CeoSeatGroupDto
    {
        /// <summary>By location rows only (null there = rooms without a location).</summary>
        public int? LocationId { get; set; }
        /// <summary>By space type rows only.</summary>
        public int? SpaceTypeId { get; set; }
        /// <summary>Location / space type name from the database; "" for the summary.</summary>
        public string Name { get; set; } = "";
        public int Rooms { get; set; }
        /// <summary>Rooms with a confirmed booking now.</summary>
        public int RoomsOccupied { get; set; }
        public decimal RoomOccupancyPct { get; set; }
        public int Seats { get; set; }
        /// <summary>Seats sold now (the shift rule, at most each room's seats).</summary>
        public int SeatsSold { get; set; }
        public int EmptySeats { get; set; }
        public decimal SeatOccupancyPct { get; set; }
        /// <summary>Room occupancy % - seat occupancy % (points): how much fuller the rooms look than the seats are.</summary>
        public decimal GapPts { get; set; }
        /// <summary>Rooms with a booking now that still have empty seats, and those empty seats.</summary>
        public int PartlyFilledRooms { get; set; }
        public int PartlyFilledEmptySeats { get; set; }
        /// <summary>Seats sold for only one of Morning / Evening (the other shift is free to sell; counted as sold).</summary>
        public int OneShiftSeats { get; set; }
        /// <summary>Monthly rent of the bookings running now after the booking discount, PKR.</summary>
        public decimal SoldMonthly { get; set; }
        /// <summary>Sold monthly / seats sold (0 when no seat is sold).</summary>
        public decimal RevenuePerSeat { get; set; }
        /// <summary>Average list price per seat of the sold seats (weighted by seats sold, rooms with a list price); null when none.</summary>
        public decimal? ListSeatPriceSold { get; set; }
        /// <summary>Average list price per seat over every seat with a list price; null when none.</summary>
        public decimal? ListSeatPriceAll { get; set; }
        /// <summary>(Sold per seat / list per seat of the sold seats - 1) x 100, rooms with a list price only; null when none.
        /// CP_02: sold / expected rent of the bookings now - 1, where Morning / Evening seats are expected at the list less
        /// the location's shift discount cap (as the Rooms section).</summary>
        public decimal? RevenueVsListPct { get; set; }
        /// <summary>Empty seats x their room's list price per seat (rooms with a list price): rent the empty seats could earn a month.</summary>
        public decimal RentNotEarned { get; set; }
        /// <summary>Empty seats of rooms without a list price (not in <see cref="RentNotEarned"/>).</summary>
        public int EmptySeatsUnpriced { get; set; }
        /// <summary>Seat-days sold / seat-days of the period so far (each day: the shift rule over the bookings covering part of it).</summary>
        public decimal PeriodSeatOccupancyPct { get; set; }
        /// <summary>Room-days with a confirmed booking / room-days of the period so far (as the Rooms section's days occupied).</summary>
        public decimal PeriodRoomOccupancyPct { get; set; }
        /// <summary>Days of the period so far (custom: of the range, up to today).</summary>
        public int PeriodDays { get; set; }
    }

    public class CeoSeatShiftDto
    {
        /// <summary>24/7 | Morning | Evening</summary>
        public string Shift { get; set; } = "";
        /// <summary>Seats of the confirmed bookings of this shift running now.</summary>
        public int Seats { get; set; }
        public int Bookings { get; set; }
    }

    public class CeoSeatMonthDto
    {
        /// <summary>yyyy-MM</summary>
        public string Month { get; set; } = "";
        public int Rooms { get; set; }
        public int RoomsOccupied { get; set; }
        public decimal RoomOccupancyPct { get; set; }
        public int Seats { get; set; }
        public int SeatsSold { get; set; }
        public decimal SeatOccupancyPct { get; set; }
    }

    public class CeoSeatRoomDto
    {
        public int SpaceId { get; set; }
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int? LocationId { get; set; }
        public string? Location { get; set; }
        public int? SpaceTypeId { get; set; }
        public string? SpaceType { get; set; }
        /// <summary>PrivateOffice | SharedSpace</summary>
        public string? CategoryCode { get; set; }
        /// <summary>WN_Spaces.Capacity (1 when not set).</summary>
        public int Seats { get; set; }
        public int SeatsSold { get; set; }
        public int EmptySeats { get; set; }
        public decimal SeatOccupancyPct { get; set; }
        /// <summary>Seats of the bookings running now by shift (before the room cap).</summary>
        public int Seats247 { get; set; }
        public int SeatsMorning { get; set; }
        public int SeatsEvening { get; set; }
        public int OneShiftSeats { get; set; }
        public int BookingsNow { get; set; }
        /// <summary>Any confirmed booking now.</summary>
        public bool OccupiedNow { get; set; }
        /// <summary>The room's price per seat (WN_Spaces.Price, else the active monthly seat price); null when none.</summary>
        public decimal? ListSeatPrice { get; set; }
        public decimal SoldMonthly { get; set; }
        public decimal RevenuePerSeat { get; set; }
        /// <summary>CP_02: rent of the bookings running now at the list price (24/7 seats x list; Morning / Evening seats x list
        /// less the shift discount cap); null without a list price or before CP_02.</summary>
        public decimal? ExpectedMonthly { get; set; }
        /// <summary>Empty seats x list price per seat; null when the room has no list price.</summary>
        public decimal? RentNotEarned { get; set; }
        public decimal PeriodSeatOccupancyPct { get; set; }
        public decimal PeriodRoomOccupancyPct { get; set; }

        // Period figures behind the percentages (totals of a group are worked out from them; not sent).
        [JsonIgnore] public int PeriodSeatDaysSold { get; set; }
        [JsonIgnore] public int PeriodDaysWithBooking { get; set; }
        [JsonIgnore] public int PeriodDays { get; set; }
        [JsonIgnore] public int Bookings247 { get; set; }
        [JsonIgnore] public int BookingsMorning { get; set; }
        [JsonIgnore] public int BookingsEvening { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WorkNest.Application.DTOs.DayPass;

namespace WorkNest.Application.Helpers
{
    /// <summary>The pure rules of a Day Pass (docs/superpowers/specs/2026-11-26-day-pass-design.md): windows, seats, price and tax, validation, status.</summary>
    public static class DayPassRules
    {
        public const string Full = "full", Morning = "morning", Evening = "evening";
        public const string Active = "Active", Used = "Used", Cancelled = "Cancelled";
        public const string AccessCnic = "cnic", AccessReception = "reception";
        public const string PriceTotal = "total", PricePerSeat = "perSeat";
        public const string PayCash = "cash", PayBank = "bank";
        public const string KindDayPass = "DayPass";

        public const string NotInstalledMessage = "Day passes are not set up on this server yet";

        public static string? NormalizeWindow(string? window)
        {
            var w = (window ?? "").Trim().ToLowerInvariant();
            return w is Full or Morning or Evening ? w : null;
        }

        /// <summary>
        /// The window of a pass: StartsAt / EndsAt as shown to people (evening ends 06:00 the next day), and the span the booking row blocks:
        /// the same, but never past midnight (booking rows and their overlap checks are date based; the contract allows the 6 hours after midnight to be ignored).
        /// </summary>
        public static (DateTime StartsAt, DateTime EndsAt, DateTime BlockStart, DateTime BlockEnd) WindowTimes(DateTime date, string window)
        {
            var d = date.Date;
            switch (window)
            {
                case Morning: return (d.AddHours(6), d.AddHours(18), d.AddHours(6), d.AddHours(18));
                case Evening: return (d.AddHours(18), d.AddDays(1).AddHours(6), d.AddHours(18), d.AddDays(1));
                default: return (d, d.AddDays(1), d, d.AddDays(1));
            }
        }

        public static string WindowLabel(string window) => window switch
        {
            Morning => "Morning (6am - 6pm)",
            Evening => "Evening (6pm - 6am)",
            _ => "Full day (24 hours)"
        };

        /// <summary>The ShiftType the booking row carries (the shift words of monthly bookings).</summary>
        public static string ShiftOf(string window) => window switch { Morning => "morning", Evening => "evening", _ => "24_7" };

        /// <summary>Same normalisation as the booking procedures' @NormalizedShift (plus the "24/7" spelling stored by some rows).</summary>
        public static string NormalizeShift(string? shift)
        {
            var s = (shift ?? "").Trim();
            if (s == "" || s is "24_7" or "24/7" or "24-by-7" or "1") return "24_7";
            var l = s.ToLowerInvariant();
            if (l is "morning" or "morning_shift" or "shift_morning" or "2") return "morning";
            if (l is "evening" or "evening_shift" or "shift_evening" or "night" or "3") return "evening";
            return l;
        }

        /// <summary>24 hours overlaps everything; morning and evening only themselves (never each other).</summary>
        public static bool ShiftsConflict(string? a, string? b)
        {
            var x = NormalizeShift(a);
            var y = NormalizeShift(b);
            return x == "24_7" || y == "24_7" || x == y;
        }

        /// <summary>Booking rows are [start, end): they overlap when each starts before the other ends.</summary>
        public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) => aStart < bEnd && aEnd > bStart;

        /// <summary>One thing already on the space in the window (a monthly booking, a meeting-room booking, a day pass).</summary>
        public sealed record Occupant(DateTime Start, DateTime End, string? Shift, int? Seats, bool IsDayPass = false);

        /// <summary>Seats in use: the seats of every occupant that overlaps the window and shift (no seats recorded = the whole space).</summary>
        public static int SeatsInUse(int capacity, DateTime blockStart, DateTime blockEnd, string window, IEnumerable<Occupant> occupants)
        {
            var shift = ShiftOf(window);
            int used = 0;
            foreach (var o in occupants)
            {
                if (!Overlaps(blockStart, blockEnd, o.Start, o.End) || !ShiftsConflict(shift, o.Shift)) continue;
                used += o.Seats is > 0 ? Math.Min(o.Seats.Value, capacity) : capacity;
            }
            return Math.Min(used, capacity);
        }

        /// <summary>Seats free and whether the whole space is free. Private offices and meeting rooms are only ever whole.</summary>
        public static (int SeatsFree, bool WholeSpaceFree) Availability(string spaceType, int capacity, int seatsInUse)
        {
            capacity = Math.Max(capacity, 1);
            int free = Math.Max(capacity - Math.Max(seatsInUse, 0), 0);
            bool whole = seatsInUse <= 0;
            if (!IsShared(spaceType)) free = whole ? capacity : 0;
            return (free, whole);
        }

        public static bool IsShared(string? spaceType) => string.Equals(spaceType, "SharedSpace", StringComparison.OrdinalIgnoreCase);

        /// <summary>The category code of a space (WN_SpaceCategories.Code) as the contract's spaceType.</summary>
        public static string SpaceTypeOf(string? categoryCode)
        {
            var c = (categoryCode ?? "").Trim();
            if (c.Equals("SharedSpace", StringComparison.OrdinalIgnoreCase) || c.Equals("Shared", StringComparison.OrdinalIgnoreCase)) return "SharedSpace";
            if (c.Equals("MeetingRoom", StringComparison.OrdinalIgnoreCase) || c.Equals("Meeting", StringComparison.OrdinalIgnoreCase) || c.Equals("Conference", StringComparison.OrdinalIgnoreCase)) return "MeetingRoom";
            return "PrivateOffice";
        }

        /// <summary>
        /// The seats a pass takes (never more than free). Office / meeting room: the whole space, always. Shared space: the whole space
        /// (only when nothing else is on it) or N seats within what is free. Returns the seats and an error, null when fine.
        /// </summary>
        public static (int Seats, bool WholeSpace, string? Error) ResolveSeats(string spaceType, int capacity, int seatsFree, bool wholeSpaceFree, bool wholeSpace, int seats)
        {
            capacity = Math.Max(capacity, 1);
            if (!IsShared(spaceType))
                return wholeSpaceFree ? (capacity, true, null) : (capacity, true, "This space is no longer free for that date and time.");
            if (wholeSpace)
                return wholeSpaceFree ? (capacity, true, null) : (capacity, true, "Someone else already has seats in this shared space, so the whole space is not free. Choose a number of seats.");
            if (seats < 1) return (seats, false, "Seats must be at least 1.");
            if (seats > capacity) return (seats, false, $"This space has {capacity} seats.");
            if (seats > seatsFree) return (seats, false, seatsFree <= 0 ? "No seat is free in this space for that date and time." : $"Only {seatsFree} seat(s) are free in this space for that date and time.");
            return (seats, false, null);
        }

        public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Price and tax: total amount, or price per seat x seats; tax (when switched on) = tax percent of the sub total, rounded to 2 places.
        /// </summary>
        public static (decimal UnitPrice, decimal SubTotal, decimal Tax, decimal Total) Price(string? priceMode, decimal amount, int seats, bool applyTax, decimal taxPercent)
        {
            seats = Math.Max(seats, 1);
            amount = Round(amount);
            decimal sub, unit;
            if (string.Equals(priceMode, PricePerSeat, StringComparison.OrdinalIgnoreCase))
            {
                unit = amount;
                sub = Round(amount * seats);
            }
            else
            {
                sub = amount;
                unit = seats > 0 ? Round(amount / seats) : amount;
            }
            decimal tax = applyTax && taxPercent > 0 ? Round(sub * taxPercent / 100m) : 0m;
            return (unit, sub, tax, sub + tax);
        }

        /// <summary>Active until the window ends, then Used; a cancelled pass stays Cancelled.</summary>
        public static string StatusOf(bool cancelled, DateTime endsAt, DateTime now) =>
            cancelled ? Cancelled : now >= endsAt ? Used : Active;

        /// <summary>A pass can be cancelled the same day or before (by its date), never twice.</summary>
        public static bool CanCancel(bool cancelled, DateTime passDate, DateTime today) => !cancelled && passDate.Date >= today.Date;

        public static bool TryParseDate(string? text, out DateTime date) =>
            DateTime.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
        public static bool IsEmail(string? e) => !string.IsNullOrWhiteSpace(e) && e.Trim().Length <= 255 && EmailRegex.IsMatch(e.Trim());

        public static string CnicDigits(string? cnic) => new string((cnic ?? "").Where(char.IsDigit).ToArray());

        /// <summary>The first broken rule of a request, or none. Checks only what needs no database; availability and the bank account are checked by the service.</summary>
        public static List<string> Validate(DayPassRequest? r, DateTime today)
        {
            var errors = new List<string>();
            if (r == null) { errors.Add("The request is empty."); return errors; }

            bool hasCode = !string.IsNullOrWhiteSpace(r.CustomerCode);
            if (hasCode && r.WalkIn != null && !IsBlank(r.WalkIn)) errors.Add("Pick an existing customer or enter a walk-in customer, not both.");
            else if (!hasCode && (r.WalkIn == null || IsBlank(r.WalkIn))) errors.Add("Pick a customer or enter the walk-in customer's details.");
            else if (!hasCode)
            {
                var w = r.WalkIn!;
                if (string.IsNullOrWhiteSpace(w.Name) || w.Name!.Trim().Length < 2) errors.Add("Enter the customer's name.");
                if (string.IsNullOrWhiteSpace(w.Phone)) errors.Add("Enter the customer's phone number.");
                else if (Phone.Validate(w.Phone) is { } pe) errors.Add(pe);
                if (!Cnic.IsValid(w.Cnic)) errors.Add(Cnic.ErrorMessage);
                if (!IsEmail(w.Email)) errors.Add("Enter a valid email: the receipt is sent to it.");
            }

            if (r.LocationId <= 0) errors.Add("Choose the location.");
            if (r.SpaceId <= 0) errors.Add("Choose the space.");
            if (!TryParseDate(r.Date, out var date)) errors.Add("Choose the date (yyyy-MM-dd).");
            else if (date.Date < today.Date) errors.Add("A day pass cannot be for a day that has passed.");
            if (NormalizeWindow(r.Window) == null) errors.Add("Choose the hours: full, morning or evening.");
            if (!string.Equals(r.PriceMode, PriceTotal, StringComparison.OrdinalIgnoreCase) && !string.Equals(r.PriceMode, PricePerSeat, StringComparison.OrdinalIgnoreCase))
                errors.Add("Price mode must be total or perSeat.");
            if (r.Amount <= 0) errors.Add("Enter the amount (more than zero).");
            if (r.Seats < 1 && !r.WholeSpace) errors.Add("Seats must be at least 1.");
            var access = (r.AccessMode ?? "").Trim().ToLowerInvariant();
            if (access != AccessCnic && access != AccessReception) errors.Add("Door access must be cnic or reception.");
            var pay = (r.PaymentMethod ?? "").Trim().ToLowerInvariant();
            if (pay != PayCash && pay != PayBank) errors.Add("Payment must be cash or bank.");
            else if (pay == PayBank)
            {
                if (r.BankAccountId is not > 0) errors.Add("Choose the bank account.");
                if (string.IsNullOrWhiteSpace(r.PaymentReference)) errors.Add("Enter the payment reference.");
            }
            if (r.Note != null && r.Note.Length > 500) errors.Add("The note can be 500 characters at most.");
            if (r.PaymentReference != null && r.PaymentReference.Length > 100) errors.Add("The payment reference can be 100 characters at most.");
            return errors;
        }

        private static bool IsBlank(DayPassWalkIn w) =>
            string.IsNullOrWhiteSpace(w.Name) && string.IsNullOrWhiteSpace(w.Phone) && string.IsNullOrWhiteSpace(w.Cnic) && string.IsNullOrWhiteSpace(w.Email) && string.IsNullOrWhiteSpace(w.Company);

        public static string PassNumber(int id) => "DP-" + id.ToString("D5", CultureInfo.InvariantCulture);
    }
}

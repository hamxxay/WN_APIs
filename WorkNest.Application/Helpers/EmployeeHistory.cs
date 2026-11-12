using WorkNest.Application.DTOs.Attendant;

namespace WorkNest.Application.Helpers
{
    /// <summary>A person who is or was on the booking (WN_Persons + their machine user id from WN_HIK_PersonMap).</summary>
    public class EmployeeHistoryPerson
    {
        public int PersonId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public string? MachineId { get; set; }
    }

    /// <summary>WN_BookingEmployeeRequests row of the booking (times UTC; by = the adding user's e-mail).</summary>
    public class EmployeeHistoryRequest
    {
        public int PersonId { get; set; }
        public DateTime AddedAtUtc { get; set; }
        public string? AddedByEmail { get; set; }
        public DateTime? RemovedAtUtc { get; set; }
    }

    /// <summary>WN_AuditLog row of WN_BookingAttendants for this booking's room (operation I / U / D).</summary>
    public class EmployeeHistoryAssignmentAudit
    {
        public DateTime AtUtc { get; set; }
        public string Operation { get; set; } = string.Empty;
        public int PersonId { get; set; }
        public DateTime? OldAssignedTo { get; set; }
        public DateTime? NewAssignedTo { get; set; }
        public string? ByEmail { get; set; }
    }

    /// <summary>WN_AuditLog row of WN_AccessStatus (door access switched on / off) for this booking's room.</summary>
    public class EmployeeHistoryAccessAudit
    {
        public DateTime AtUtc { get; set; }
        public string Operation { get; set; } = string.Empty;
        public int PersonId { get; set; }
        public bool? OldEnabled { get; set; }
        public bool? NewEnabled { get; set; }
        public string? ByEmail { get; set; }
    }

    /// <summary>WN_AuditLog update of WN_Persons (only rows where the e-mail may have changed).</summary>
    public class EmployeeHistoryEmailAudit
    {
        public DateTime AtUtc { get; set; }
        public int PersonId { get; set; }
        public string? OldEmail { get; set; }
        public string? NewEmail { get; set; }
        public string? ByEmail { get; set; }
    }

    /// <summary>A fingerprint / face (vault audit) or card (WN_HIK_Cards) written for a machine user (employee_no).</summary>
    public class EmployeeHistoryCredential
    {
        public DateTime AtUtc { get; set; }
        /// <summary>fingerprint | face | card</summary>
        public string Kind { get; set; } = string.Empty;
        public string EmployeeNo { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public string? ByEmail { get; set; }
    }

    /// <summary>Everything read for one booking's employee history (see BookingEmployeeRepository.GetHistoryAsync).</summary>
    public class EmployeeHistoryRaw
    {
        public List<EmployeeHistoryPerson> People { get; set; } = new();
        public List<EmployeeHistoryRequest> Requests { get; set; } = new();
        public List<EmployeeHistoryAssignmentAudit> Assignments { get; set; } = new();
        public List<EmployeeHistoryAccessAudit> Access { get; set; } = new();
        public List<EmployeeHistoryEmailAudit> Emails { get; set; } = new();
        public List<EmployeeHistoryCredential> Credentials { get; set; } = new();
    }

    /// <summary>
    /// Merges the sources into the CMS "Employee history" list (pure, no database): the same thing seen by two sources is
    /// one line (a customer add = request row + assignment audit; a removal = request RemovedAt + assignment audit),
    /// door-access switches that are just the consequence of an add / remove are left out, repeated vault writes of one
    /// credential count once, and the list is newest first.
    /// </summary>
    public static class EmployeeHistory
    {
        /// <summary>Two records of one thing within this time are the same event.</summary>
        public static readonly TimeSpan SameEvent = TimeSpan.FromMinutes(5);
        /// <summary>A request removed this quickly after it was made never completed (the add failed and was rolled back).</summary>
        public static readonly TimeSpan RolledBack = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan CredentialWindow = TimeSpan.FromMinutes(10);

        public const string Customer = "Customer";
        public const string Staff = "Staff";

        private sealed class Ev
        {
            public DateTime At;
            public int PersonId;
            public string Action = "";
            public string? By;
            public string? Detail;
            public bool FromRequest;
        }

        public static List<BookingEmployeeHistoryItemDto> Build(EmployeeHistoryRaw raw, TimeZoneInfo zone)
        {
            var people = raw.People.GroupBy(p => p.PersonId).ToDictionary(g => g.Key, g => g.First());
            var byMachine = raw.People.Where(p => !string.IsNullOrWhiteSpace(p.MachineId))
                .GroupBy(p => p.MachineId!.Trim()).ToDictionary(g => g.Key, g => g.First().PersonId);

            // ---- added / removed -------------------------------------------------------------------------
            var added = new List<Ev>();
            var removed = new List<Ev>();
            foreach (var a in raw.Assignments)
            {
                if (a.Operation == "I")
                    added.Add(new Ev { At = a.AtUtc, PersonId = a.PersonId, Action = "added", By = Clean(a.ByEmail) });
                else if (a.Operation == "D" || (a.Operation == "U" && IsEnded(a, zone)))
                    removed.Add(new Ev { At = a.AtUtc, PersonId = a.PersonId, Action = "removed", By = Clean(a.ByEmail) });
            }
            foreach (var r in raw.Requests)
            {
                bool rolledBack = r.RemovedAtUtc.HasValue && r.RemovedAtUtc.Value - r.AddedAtUtc <= RolledBack
                    && !added.Any(x => x.PersonId == r.PersonId && Near(x.At, r.AddedAtUtc));
                if (rolledBack) continue; // never completed
                MergeIn(added, new Ev { At = r.AddedAtUtc, PersonId = r.PersonId, Action = "added", By = Clean(r.AddedByEmail), FromRequest = true });
                if (r.RemovedAtUtc is DateTime rm)
                    MergeIn(removed, new Ev { At = rm, PersonId = r.PersonId, Action = "removed", FromRequest = true });
            }
            var events = new List<Ev>();
            events.AddRange(added);
            events.AddRange(removed);

            // ---- door access on / off (not the consequence of an add / remove) -------------------------------
            foreach (var s in raw.Access)
            {
                bool? on = s.Operation == "I" ? s.NewEnabled
                    : s.Operation == "U" && s.OldEnabled != s.NewEnabled ? s.NewEnabled : null;
                if (on == null) continue;
                var action = on.Value ? "access_on" : "access_off";
                bool consequence = on.Value
                    ? added.Any(x => x.PersonId == s.PersonId && Near(x.At, s.AtUtc))
                    : removed.Any(x => x.PersonId == s.PersonId && Near(x.At, s.AtUtc));
                if (consequence) continue;
                events.Add(new Ev { At = s.AtUtc, PersonId = s.PersonId, Action = action, By = Clean(s.ByEmail) });
            }

            // ---- e-mail changes --------------------------------------------------------------------------------
            foreach (var e in raw.Emails)
            {
                var o = (e.OldEmail ?? "").Trim();
                var n = (e.NewEmail ?? "").Trim();
                if (string.Equals(o, n, StringComparison.OrdinalIgnoreCase)) continue;
                events.Add(new Ev
                {
                    At = e.AtUtc, PersonId = e.PersonId, Action = "email", By = Clean(e.ByEmail),
                    Detail = o.Length == 0 ? $"Set to {n}" : n.Length == 0 ? $"Removed {o}" : $"{o} to {n}"
                });
            }

            // ---- fingerprint / face / card ---------------------------------------------------------------------
            foreach (var g in raw.Credentials
                .Where(c => byMachine.ContainsKey((c.EmployeeNo ?? "").Trim()))
                .GroupBy(c => (Person: byMachine[c.EmployeeNo.Trim()], c.Kind)))
            {
                Ev? last = null;
                foreach (var c in g.OrderBy(c => c.AtUtc))
                {
                    if (last != null && c.AtUtc - last.At <= CredentialWindow)
                    {
                        last.By ??= Clean(c.ByEmail);
                        last.Detail ??= c.Detail;
                        continue;
                    }
                    last = new Ev { At = c.AtUtc, PersonId = g.Key.Person, Action = c.Kind, By = Clean(c.ByEmail), Detail = c.Detail };
                    events.Add(last);
                }
            }

            // ---- result ----------------------------------------------------------------------------------------
            var order = new[] { "added", "email", "fingerprint", "face", "card", "access_on", "access_off", "removed" };
            return events
                .Where(e => people.ContainsKey(e.PersonId))
                .OrderByDescending(e => e.At)
                .ThenByDescending(e => Array.IndexOf(order, e.Action))
                .Select(e =>
                {
                    var p = people[e.PersonId];
                    return new BookingEmployeeHistoryItemDto
                    {
                        At = ToIso(e.At, zone),
                        PersonId = e.PersonId,
                        PersonName = p.Name,
                        IdNumberMasked = MaskId(p.IdNumber),
                        Action = e.Action,
                        By = e.By ?? (e.FromRequest ? Customer : Staff),
                        Detail = string.IsNullOrWhiteSpace(e.Detail) ? null : e.Detail
                    };
                })
                .ToList();
        }

        /// <summary>An update of the assignment that ends it before the day it was made (the soft remove sets the last day to yesterday).</summary>
        internal static bool IsEnded(EmployeeHistoryAssignmentAudit a, TimeZoneInfo zone)
        {
            if (a.NewAssignedTo is not DateTime to) return false;
            if (a.OldAssignedTo.HasValue && a.OldAssignedTo.Value.Date == to.Date) return false; // another column changed
            var localDay = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(a.AtUtc, DateTimeKind.Utc), zone).Date;
            return to.Date < localDay;
        }

        /// <summary>Adds the event, or completes the same event of another source (same person within <see cref="SameEvent"/>).</summary>
        private static void MergeIn(List<Ev> list, Ev e)
        {
            var same = list.FirstOrDefault(x => x.PersonId == e.PersonId && Near(x.At, e.At));
            if (same == null) { list.Add(e); return; }
            same.By ??= e.By;
            same.FromRequest |= e.FromRequest;
        }

        private static bool Near(DateTime a, DateTime b) => (a - b).Duration() <= SameEvent;

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string ToIso(DateTime utc, TimeZoneInfo zone)
        {
            var u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var local = new DateTimeOffset(u).ToOffset(zone.GetUtcOffset(u));
            return local.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
        }

        /// <summary>All but the last 4 characters hidden (1234512345671 becomes *********5671).</summary>
        public static string MaskId(string? idNumber)
        {
            var v = (idNumber ?? "").Replace("-", "").Replace(" ", "").Trim();
            if (v.Length == 0) return "";
            return v.Length <= 4 ? new string('*', v.Length) : new string('*', v.Length - 4) + v[^4..];
        }
    }
}

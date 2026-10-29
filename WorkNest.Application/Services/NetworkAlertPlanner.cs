using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// The rules of the network alerts, with no I/O (so they are tested directly). From what is seen now and what was saved
    /// it decides: which items are newly down (first e-mail once down for <see cref="NetworkAlertSettings.DownFor"/>, 5
    /// minutes), which are due a reminder (every <see cref="NetworkAlertSettings.ReminderEvery"/>, 6 hours, while still
    /// down), which recovered (one "back online" e-mail, only for items that were alerted), and the state to save.
    /// One <see cref="NetworkAlertMessage"/> per location per check, however many items it covers.
    /// </summary>
    public static class NetworkAlertPlanner
    {
        /// <summary>Detail of an internet line that is down while another line carries the traffic (failover).</summary>
        public const string FailoverDetail = "Traffic is on the backup line";

        public sealed record NetworkAlertSettings(TimeSpan DownFor, TimeSpan ReminderEvery);

        /// <summary>An item in an e-mail; Since = DownSince; Duration = how long it was down (recovered items).</summary>
        public sealed record Entry(NetworkAlertState State, TimeSpan? Duration = null);

        public sealed class NetworkAlertMessage
        {
            /// <summary>Down for the threshold now: the first e-mail about them.</summary>
            public List<Entry> NewlyDown { get; } = new();
            /// <summary>Still down, alerted before: the 6-hourly reminder.</summary>
            public List<Entry> StillDown { get; } = new();
            public List<Entry> Recovered { get; } = new();
            /// <summary>alert | reminder | recovery (the log's Kind).</summary>
            public string Kind => NewlyDown.Count > 0 ? "alert" : StillDown.Count > 0 ? "reminder" : "recovery";
            public IEnumerable<Entry> Down => NewlyDown.Concat(StillDown);
        }

        public sealed class NetworkAlertPlan
        {
            /// <summary>State to save before anything is sent (new down items, silent recoveries, refreshed names).</summary>
            public List<NetworkAlertState> Immediate { get; } = new();
            /// <summary>State to save only once the e-mail went out (alert / reminder / recovery marks).</summary>
            public List<NetworkAlertState> AfterEmail { get; } = new();
            public NetworkAlertMessage? Message { get; set; }
        }

        /// <param name="now">Database clock, UTC.</param>
        /// <param name="observed">What is seen now (only the kinds in <paramref name="checkedKinds"/> were readable).</param>
        /// <param name="checkedKinds">Kinds read successfully this check; the others are left as they are.</param>
        /// <param name="fullKinds">Kinds whose list names every item, up and down: an item missing from it was removed or moved, so it closes without a "back online" e-mail.</param>
        /// <param name="saved">Saved state of the location's open items (RecoveredAt null).</param>
        public static NetworkAlertPlan Plan(DateTime now, IReadOnlyList<NetworkAlertItem> observed, ISet<string> checkedKinds,
            ISet<string> fullKinds, IReadOnlyList<NetworkAlertState> saved, NetworkAlertSettings settings)
        {
            var plan = new NetworkAlertPlan();
            var message = new NetworkAlertMessage();
            var open = saved.Where(s => s.RecoveredAt == null).ToDictionary(s => (s.Kind, s.ItemKey));
            var down = observed.Where(o => o.Down && checkedKinds.Contains(o.Kind)).GroupBy(o => (o.Kind, o.Key)).ToDictionary(g => g.Key, g => g.First());

            var remindersDue = false;
            var stillAlerted = new List<NetworkAlertState>();
            foreach (var (key, item) in down)
            {
                var state = open.TryGetValue(key, out var existing)
                    ? existing with { Name = item.Name, Detail = item.Detail }
                    : new NetworkAlertState(item.Kind, item.Key, item.Name, item.Detail, now, null, null, null);
                if (state.FirstAlertAt == null)
                {
                    if (now - state.DownSince >= settings.DownFor)
                    {
                        var marked = state with { FirstAlertAt = now };
                        message.NewlyDown.Add(new Entry(marked));
                        plan.AfterEmail.Add(marked);
                    }
                    else if (existing == null || state != existing) plan.Immediate.Add(state);
                }
                else
                {
                    stillAlerted.Add(state);
                    if (state != existing) plan.Immediate.Add(state);
                    if (now - (state.LastReminderAt ?? state.FirstAlertAt.Value) >= settings.ReminderEvery) remindersDue = true;
                }
            }
            if (remindersDue)
            {
                foreach (var s in stillAlerted)
                {
                    var marked = s with { LastReminderAt = now };
                    message.StillDown.Add(new Entry(marked));
                    plan.AfterEmail.Add(marked);
                    plan.Immediate.RemoveAll(i => i.Kind == s.Kind && i.ItemKey == s.ItemKey);
                }
            }

            foreach (var (key, state) in open)
            {
                if (!checkedKinds.Contains(key.Kind) || down.ContainsKey(key)) continue;
                var recovered = state with { RecoveredAt = now };
                if (state.FirstAlertAt == null || (fullKinds.Contains(key.Kind) && !observed.Any(o => o.Kind == key.Kind && o.Key == key.ItemKey)))
                    plan.Immediate.Add(recovered); // never alerted, or no longer listed: close quietly
                else
                {
                    message.Recovered.Add(new Entry(recovered, now - state.DownSince));
                    plan.AfterEmail.Add(recovered);
                }
            }

            if (message.NewlyDown.Count + message.StillDown.Count + message.Recovered.Count > 0) plan.Message = message;
            return plan;
        }

        public static bool IsFailover(NetworkAlertState s) => s.Kind == NetworkAlertKinds.Wan && s.Detail != null && s.Detail.StartsWith(FailoverDetail, StringComparison.Ordinal);
    }
}

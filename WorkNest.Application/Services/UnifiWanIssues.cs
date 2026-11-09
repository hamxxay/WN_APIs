using System.Globalization;
using System.Text.Json;

namespace WorkNest.Application.Services
{
    /// <summary>
    /// UniFi Site Manager's <c>statistics.wans.*.wanIssues</c>: each item is one 5-minute window in which the line had a
    /// problem. <c>index</c> counts 5-minute blocks since 1970 (UTC); <c>notReported: true</c> means the gateway sent nothing
    /// to UniFi in that window. UniFi keeps them in the list for a while after they end, so the dashboards merge back-to-back
    /// windows, say when each happened, and count one as cleared <see cref="ClearedAfter"/> after it ended.
    /// </summary>
    public static class UnifiWanIssues
    {
        /// <summary>An issue that ended this long ago is over: it no longer shows on the dashboards.</summary>
        public static readonly TimeSpan ClearedAfter = TimeSpan.FromMinutes(15);

        /// <summary>Issues older than this are not read at all (UniFi keeps them for a shorter time anyway).</summary>
        private const int MaxAgeDays = 30;

        private static readonly TimeSpan Block = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Pakistan = TimeSpan.FromHours(5);

        /// <summary>One issue: back-to-back 5-minute windows merged. Times are UTC.</summary>
        public sealed record Window(DateTime StartUtc, DateTime EndUtc, bool NotReported, bool Active);

        /// <summary>
        /// The issues, oldest first. Items without a usable index are skipped, duplicates count once, and windows that
        /// follow each other become one (not reported when any part was).
        /// </summary>
        public static List<Window> Windows(IEnumerable<JsonElement> items, DateTime nowUtc)
        {
            // Only indexes near now are read as 5-minute blocks: anything else (a value in seconds or milliseconds, a bad
            // item) is skipped, so it can never overflow a date and break the network summary.
            var nowIndex = (nowUtc - DateTime.UnixEpoch).Ticks / Block.Ticks;
            var minIndex = nowIndex - TimeSpan.FromDays(MaxAgeDays).Ticks / Block.Ticks;
            var maxIndex = nowIndex + TimeSpan.FromDays(1).Ticks / Block.Ticks;
            var blocks = new SortedDictionary<long, bool>();
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("index", out var ix) || ix.ValueKind != JsonValueKind.Number || !ix.TryGetInt64(out var index))
                    continue;
                if (index < minIndex || index > maxIndex) continue;
                var notReported = item.TryGetProperty("notReported", out var nr) && nr.ValueKind == JsonValueKind.True;
                blocks[index] = (blocks.TryGetValue(index, out var seen) && seen) || notReported;
            }

            var windows = new List<Window>();
            long? first = null, last = null;
            var anyNotReported = false;
            void Close()
            {
                if (first == null) return;
                var start = DateTime.UnixEpoch.AddTicks(Block.Ticks * first.Value);
                var end = DateTime.UnixEpoch.AddTicks(Block.Ticks * (last!.Value + 1));
                windows.Add(new Window(start, end, anyNotReported, nowUtc < end + ClearedAfter));
            }
            foreach (var (index, notReported) in blocks)
            {
                if (last != null && index == last + 1)
                {
                    last = index;
                    anyNotReported |= notReported;
                    continue;
                }
                Close();
                first = last = index;
                anyNotReported = notReported;
            }
            Close();
            return windows;
        }

        /// <summary>"not reported to UniFi, 10:05 AM–10:15 AM" (Pakistan time); the day is named when it was not today.</summary>
        public static string Describe(Window w, DateTime nowUtc)
        {
            var start = w.StartUtc + Pakistan;
            var end = w.EndUtc + Pakistan;
            var day = start.Date == (nowUtc + Pakistan).Date ? "" : start.ToString("d MMM ", CultureInfo.InvariantCulture);
            var what = w.NotReported ? "not reported to UniFi" : "connection issue";
            return $"{what}, {day}{Time(start)}–{Time(end)}";
        }

        private static string Time(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}

using System.Globalization;
using System.Net;
using System.Text;
using WorkNest.Application.Interfaces;
using static WorkNest.Application.Services.NetworkAlertPlanner;

namespace WorkNest.Application.Services
{
    /// <summary>The subject and the plain HTML body of a network alert e-mail (times in Pakistan time).</summary>
    public static class NetworkAlertEmail
    {
        public const string CmsBaseUrl = "https://cms.worknestpk.com";
        public const string MachinesUrl = CmsBaseUrl + "/admin/network/access-machines";
        public const string NetworkUrl = CmsBaseUrl + "/admin/network";

        /// <summary>"WorkNest I-8: 2 access machines offline, internet down" (a reminder / recovery says so).</summary>
        public static string Subject(string locationName, NetworkAlertMessage m)
        {
            var down = m.Down.Select(e => e.State).ToList();
            var parts = new List<string>();
            AddCount(parts, down, NetworkAlertKinds.Hik, "access machine", "access machines", "offline");
            var wan = down.Where(s => s.Kind == NetworkAlertKinds.Wan).ToList();
            if (wan.Count > 0) parts.Add(wan.All(IsFailover) ? "internet on backup line" : "internet down");
            AddCount(parts, down, NetworkAlertKinds.Unifi, "network device", "network devices", "offline");
            AddCount(parts, down, NetworkAlertKinds.Gwn, "Grandstream device", "Grandstream devices", "offline");

            var back = new List<string>();
            var rec = m.Recovered.Select(e => e.State).ToList();
            AddCount(back, rec, NetworkAlertKinds.Hik, "access machine", "access machines", null);
            if (rec.Any(s => s.Kind == NetworkAlertKinds.Wan)) back.Add("internet");
            AddCount(back, rec, NetworkAlertKinds.Unifi, "network device", "network devices", null);
            AddCount(back, rec, NetworkAlertKinds.Gwn, "Grandstream device", "Grandstream devices", null);

            var subject = $"WorkNest {locationName}: ";
            if (parts.Count == 0) return subject + "back online: " + string.Join(", ", back);
            if (m.NewlyDown.Count == 0) subject += "Reminder - ";
            subject += string.Join(", ", parts);
            if (back.Count > 0) subject += " (back online: " + string.Join(", ", back) + ")";
            return subject;
        }

        private static void AddCount(List<string> parts, List<NetworkAlertState> items, string kind, string one, string many, string? suffix)
        {
            var n = items.Count(s => s.Kind == kind);
            if (n == 0) return;
            parts.Add($"{n} {(n == 1 ? one : many)}{(suffix == null ? "" : " " + suffix)}");
        }

        /// <summary>One line for the log / recent alerts list.</summary>
        public static string Summary(NetworkAlertMessage m)
        {
            var sb = new List<string>();
            foreach (var e in m.NewlyDown) sb.Add($"down: {e.State.Name}");
            foreach (var e in m.StillDown) sb.Add($"still down: {e.State.Name}");
            foreach (var e in m.Recovered) sb.Add($"back: {e.State.Name}");
            return string.Join("; ", sb);
        }

        public static string Html(string locationName, NetworkAlertMessage m, DateTime nowUtc, Func<DateTime, DateTime> toLocal)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#1f2937;max-width:640px\">");
            sb.Append($"<h2 style=\"margin:0 0 4px;font-size:18px\">WorkNest {E(locationName)}: network alert</h2>");
            sb.Append($"<p style=\"margin:0 0 16px;color:#6b7280\">Checked {E(Time(toLocal(nowUtc)))} (Pakistan time)</p>");
            Section(sb, "Down", "#b91c1c", m.NewlyDown, toLocal, nowUtc, false);
            Section(sb, "Still down (reminder)", "#b45309", m.StillDown, toLocal, nowUtc, false);
            Section(sb, "Back online", "#15803d", m.Recovered, toLocal, nowUtc, true);

            var all = m.Down.Select(e => e.State).Concat(m.Recovered.Select(e => e.State)).ToList();
            sb.Append("<p style=\"margin:16px 0 0\">");
            if (all.Any(s => s.Kind == NetworkAlertKinds.Hik)) sb.Append($"Access machines: <a href=\"{MachinesUrl}\">{MachinesUrl}</a><br>");
            if (all.Any(s => s.Kind != NetworkAlertKinds.Hik)) sb.Append($"Network: <a href=\"{NetworkUrl}\">{NetworkUrl}</a><br>");
            sb.Append("</p>");
            sb.Append("<p style=\"margin:16px 0 0;color:#6b7280;font-size:12px\">You get this because you are on the network alert list of this location. A reminder follows every 6 hours while something stays down.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>The test e-mail of "Send test email".</summary>
        public static (string Subject, string Html) Test(string locationName, DateTime nowUtc, Func<DateTime, DateTime> toLocal) =>
            ($"WorkNest {locationName}: test network alert",
             "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#1f2937;max-width:640px\">" +
             $"<h2 style=\"margin:0 0 8px;font-size:18px\">WorkNest {E(locationName)}: test network alert</h2>" +
             "<p>This is a test. Network alerts for this location reach this address: you will get an e-mail here when an access machine, the internet line or a network device is down.</p>" +
             $"<p style=\"color:#6b7280\">Sent {E(Time(toLocal(nowUtc)))} (Pakistan time)</p></div>");

        private static void Section(StringBuilder sb, string title, string color, List<Entry> entries, Func<DateTime, DateTime> toLocal, DateTime nowUtc, bool recovered)
        {
            if (entries.Count == 0) return;
            sb.Append($"<h3 style=\"margin:16px 0 6px;font-size:15px;color:{color}\">{E(title)}</h3>");
            sb.Append("<table style=\"border-collapse:collapse;width:100%\">");
            sb.Append("<tr style=\"text-align:left;color:#6b7280;font-size:12px\"><th style=\"padding:4px 8px\">What</th><th style=\"padding:4px 8px\">Details</th>");
            sb.Append(recovered ? "<th style=\"padding:4px 8px\">Was down</th></tr>" : "<th style=\"padding:4px 8px\">Down since</th></tr>");
            foreach (var e in entries)
            {
                var s = e.State;
                sb.Append("<tr style=\"border-top:1px solid #e5e7eb\">");
                sb.Append($"<td style=\"padding:6px 8px\"><b>{E(s.Name)}</b><br><span style=\"color:#6b7280;font-size:12px\">{E(KindLabel(s.Kind))}</span></td>");
                sb.Append($"<td style=\"padding:6px 8px\">{E(s.Detail ?? "")}</td>");
                sb.Append($"<td style=\"padding:6px 8px\">{E(recovered ? Duration(e.Duration ?? TimeSpan.Zero) : Time(toLocal(s.DownSince)) + " (" + Duration(nowUtc - s.DownSince) + ")")}</td></tr>");
            }
            sb.Append("</table>");
        }

        public static string KindLabel(string kind) => kind switch
        {
            NetworkAlertKinds.Hik => "Access machine",
            NetworkAlertKinds.Wan => "Internet",
            NetworkAlertKinds.Unifi => "Network device",
            NetworkAlertKinds.Gwn => "Grandstream device",
            _ => kind
        };

        public static string Time(DateTime local) => local.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

        public static string Duration(TimeSpan d)
        {
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            if (d.TotalMinutes < 1) return "under a minute";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} h {d.Minutes} min";
            return $"{(int)d.TotalDays} d {d.Hours} h";
        }

        private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    }
}

using System.Globalization;
using System.Net;
using System.Text;

namespace WorkNest.Application.Services
{
    /// <summary>What an approvals e-mail says about a request. Everything typed by staff is HTML-escaped by the builders.</summary>
    public sealed record ApprovalEmailRequest(int Id, string? LocationName, string CategoryName, string Title, string Details, string? Reason,
        string? RequestedByName, DateTime RequestedAtUtc);

    /// <summary>
    /// The six approvals e-mails (spec "Emails"), each as (subject, html). Links use the CMS address from
    /// <c>Approvals:CmsBaseUrl</c> (default <see cref="DefaultCmsBaseUrl"/>), so local and live each point to their own CMS.
    /// </summary>
    public static class ApprovalEmail
    {
        public const string DefaultCmsBaseUrl = "https://cms.worknestpk.com";

        /// <summary>The configured CMS address without a trailing slash; the default when blank.</summary>
        public static string BaseUrl(string? configured)
        {
            var b = configured?.Trim().TrimEnd('/');
            return string.IsNullOrEmpty(b) ? DefaultCmsBaseUrl : b;
        }

        /// <summary>The approver's page. The token is base64url, so it needs no escaping in a URL.</summary>
        public static string ApprovalLink(string baseUrl, string token) => baseUrl + "/approval/" + Uri.EscapeDataString(token);

        /// <summary>The Approvals list of the CMS.</summary>
        public static string RequestsLink(string baseUrl) => baseUrl + "/admin/approvals";

        /// <summary>1. To the approver: the request and a Review request button.</summary>
        public static (string Subject, string Html) ToApprover(string baseUrl, string token, ApprovalEmailRequest r, DateTime expiresAtUtc)
        {
            var body = Card(r, null) +
                       Button(ApprovalLink(baseUrl, token), "Review request") +
                       Muted($"Opening the link changes nothing: you choose Approve or Reject on the page. The link works until {E(Time(expiresAtUtc))} (Pakistan time) and once.");
            return ($"{Subject(r.LocationName)}: approval needed - {Clean(r.Title)}", Wrap("Approval needed", body));
        }

        /// <summary>2. To the person who made the request: the decision and note.</summary>
        public static (string Subject, string Html) Decision(string baseUrl, ApprovalEmailRequest r, string decision, string? note)
        {
            var approved = decision == "Approved";
            var body = $"<p>Your request was <strong>{(approved ? "approved" : "rejected")}</strong>.</p>" + Card(r, null) +
                       (string.IsNullOrWhiteSpace(note) ? "" : $"<p><strong>Note from the approver:</strong><br>{Multi(note)}</p>") +
                       (approved ? Muted("The people set for this location and category have been told.") : "") +
                       Link(RequestsLink(baseUrl), "Open Approvals");
            return ($"{Subject(r.LocationName)}: request {(approved ? "approved" : "rejected")} - {Clean(r.Title)}", Wrap(approved ? "Request approved" : "Request rejected", body));
        }

        /// <summary>3. To an assigned person: the approved request and a link to it in the CMS.</summary>
        public static (string Subject, string Html) Assigned(string baseUrl, ApprovalEmailRequest r, string? decisionNote)
        {
            var body = "<p>This request was approved and is waiting for you. Mark it Done in the CMS when the work is finished.</p>" + Card(r, null) +
                       (string.IsNullOrWhiteSpace(decisionNote) ? "" : $"<p><strong>Note from the approver:</strong><br>{Multi(decisionNote)}</p>") +
                       Button(RequestsLink(baseUrl), "Open request");
            return ($"{Subject(r.LocationName)}: approved request to do - {Clean(r.Title)}", Wrap("Approved request", body));
        }

        /// <summary>5. To the super admins: approved, but nobody is assigned for that location and category.</summary>
        public static (string Subject, string Html) NobodyAssigned(string baseUrl, ApprovalEmailRequest r)
        {
            var body = $"<p>This request was approved, but <strong>nobody is set</strong> for {E(r.LocationName)} and {E(r.CategoryName)}. Add people in Approval Settings, then someone can mark it Done.</p>" +
                       Card(r, null) + Button(RequestsLink(baseUrl), "Open Approvals");
            return ($"{Subject(r.LocationName)}: approved request has nobody assigned - {Clean(r.Title)}", Wrap("Nobody assigned", body));
        }

        /// <summary>4. To the person who made the request: the work is Done, with the note.</summary>
        public static (string Subject, string Html) Done(string baseUrl, ApprovalEmailRequest r, string? doneByName, string? note)
        {
            var body = "<p>The work for your request is <strong>done</strong>.</p>" + Card(r, null) +
                       (string.IsNullOrEmpty(doneByName) ? "" : $"<p>Done by {E(doneByName)}.</p>") +
                       (string.IsNullOrWhiteSpace(note) ? "" : $"<p><strong>Note:</strong><br>{Multi(note)}</p>") +
                       Link(RequestsLink(baseUrl), "Open Approvals");
            return ($"{Subject(r.LocationName)}: request done - {Clean(r.Title)}", Wrap("Request done", body));
        }

        /// <summary>6. To the person who made the request: it expired, with a link to send it again.</summary>
        public static (string Subject, string Html) Expired(string baseUrl, ApprovalEmailRequest r)
        {
            var body = "<p>Nobody decided this request in time, so it <strong>expired</strong>. You can send it again from Approvals; a new link goes to the approver.</p>" +
                       Card(r, null) + Button(RequestsLink(baseUrl), "Send it again");
            return ($"{Subject(r.LocationName)}: request expired - {Clean(r.Title)}", Wrap("Request expired", body));
        }

        // ---- building blocks ---------------------------------------------------------------------------------------

        private static string Subject(string? location) => "WorkNest " + (string.IsNullOrWhiteSpace(location) ? "Approvals" : Clean(location));

        /// <summary>One line, no control characters: a title typed with a line break must not reach the subject header.</summary>
        private static string Clean(string? s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s ?? "") sb.Append(char.IsControl(ch) ? ' ' : ch);
            var t = sb.ToString().Trim();
            return t.Length > 120 ? t[..120] + "..." : t;
        }

        private static string Card(ApprovalEmailRequest r, string? extra)
        {
            var sb = new StringBuilder("<table style=\"border-collapse:collapse;width:100%;margin:12px 0\">");
            Row(sb, "Request", r.Title);
            Row(sb, "Location", r.LocationName);
            Row(sb, "Category", r.CategoryName);
            Row(sb, "Details", r.Details, true);
            if (!string.IsNullOrWhiteSpace(r.Reason)) Row(sb, "Reason", r.Reason, true);
            Row(sb, "Requested by", r.RequestedByName);
            Row(sb, "Requested", Time(r.RequestedAtUtc) + " (Pakistan time)");
            sb.Append(extra).Append("</table>");
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string label, string? value, bool multi = false) =>
            sb.Append("<tr style=\"border-top:1px solid #e5e7eb\"><td style=\"padding:6px 8px;color:#6b7280;width:110px;vertical-align:top\">").Append(E(label))
              .Append("</td><td style=\"padding:6px 8px\">").Append(multi ? Multi(value) : E(value)).Append("</td></tr>");

        private static string Button(string url, string text) =>
            $"<p style=\"margin:16px 0\"><a href=\"{E(url)}\" style=\"background:#2563eb;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none;display:inline-block\">{E(text)}</a></p>" +
            $"<p style=\"margin:0 0 12px;color:#6b7280;font-size:12px\">If the button does not work, copy this address: {E(url)}</p>";

        private static string Link(string url, string text) => $"<p><a href=\"{E(url)}\">{E(text)}</a></p>";

        private static string Muted(string htmlText) => $"<p style=\"color:#6b7280;font-size:12px\">{htmlText}</p>";

        private static string Wrap(string heading, string body) =>
            "<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#1f2937;max-width:640px\">" +
            $"<h2 style=\"margin:0 0 8px;font-size:18px\">{E(heading)}</h2>{body}</div>";

        /// <summary>Escaped, with line breaks kept.</summary>
        private static string Multi(string? s) => E(s).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");

        /// <summary>Pakistan time (UTC+5, no daylight saving).</summary>
        private static string Time(DateTime utc) => utc.AddHours(5).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

        private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    }
}

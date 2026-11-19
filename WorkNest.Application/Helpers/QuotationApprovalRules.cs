using System;
using System.Collections.Generic;
using System.Globalization;
using WorkNest.Application.DTOs.Quotation;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Quotation discount approval (script QAPR_01_quotation_approval): pure rules, no I/O.
    /// Only a MANUAL discount counts (percentage or fixed amount > 0); an automatic Offer (OFFER_01 snapshot) never does.
    /// Only a sales executive needs approval; admins, super admins and sales managers send directly.
    /// </summary>
    public static class QuotationApprovalRules
    {
        public const string PendingManager = "PendingManager";
        public const string Approved = "Approved";
        public const string SentBack = "SentBack";
        /// <summary>A sales executive's discounted quotation not sent to the sales manager yet (set when it is created): the
        /// customer cannot see or accept it and no agreement can be sent; "Send" routes it to the manager.</summary>
        public const string NotSubmitted = "NotSubmitted";

        // History actions (WN_QuotationHistory.Action).
        public const string ActionCreated = "created";
        public const string ActionSubmitted = "submitted";
        public const string ActionResubmitted = "resubmitted";
        public const string ActionDiscountEdited = "discount_edited";
        public const string ActionApproved = "approved";
        public const string ActionSentToCustomer = "sent_to_customer";
        public const string ActionSentBack = "sent_back";
        public const string ActionEdited = "edited";

        public const int ReasonMinLength = 5;
        public const int ReasonMaxLength = 1000;
        public const int NoteMaxLength = 1000;

        public const string ReasonRequiredMessage = "Enter the reason for the discount.";
        public const string ReasonTooLongMessage = "The reason for the discount can be at most 1000 characters.";
        public const string NoManagerMessage = "Ask a super admin to assign your sales manager.";
        public const string NotSetUpMessage = "Quotation approval is not set up on this server yet.";
        public const string NotPendingMessage = "This quotation is not waiting for approval.";
        public const string NoteRequiredMessage = "Enter a note for the sales executive.";
        public const string NoteTooLongMessage = "The note can be at most 1000 characters.";
        public const string NotYoursMessage = "This quotation was not sent to you for approval.";

        /// <summary>
        /// The lowest recurring fee a sales executive may put in an agreement: the quotation's monthly rent after its own
        /// (approved) manual discount, for one billing cycle. Null when the quotation has no monthly rent (e.g. a meeting room):
        /// no floor. A lower fee would be a discount nobody approved.
        /// </summary>
        public static decimal? AgreementFeeFloor(decimal monthlyRent, string? discountType, decimal discountPercentage, decimal discountValue, int billingMonths)
        {
            if (monthlyRent <= 0m) return null;
            decimal monthly = IsFixed(discountType)
                ? monthlyRent - Math.Max(0m, discountValue)
                : monthlyRent * (1m - Math.Clamp(discountPercentage, 0m, 100m) / 100m);
            return Math.Round(Math.Max(0m, monthly) * Math.Max(1, billingMonths), 2);
        }

        public static string AgreementFeeTooLowMessage(decimal floor, int billingMonths) =>
            "The recurring fee cannot be lower than PKR " + floor.ToString("N2", CultureInfo.InvariantCulture)
            + $" (the quotation's rent after its discount, for {Math.Max(1, billingMonths)} month(s)). "
            + "A lower fee needs a discount on the quotation, approved by your sales manager.";

        /// <summary>A discounted quotation its sales executive has not sent to the sales manager yet.</summary>
        public const string AgreementNotSubmittedMessage =
            "This quotation's discount has not been sent to the sales manager for approval yet. Send the quotation first; the agreement can be sent once the discount is approved.";

        /// <summary>The agreement of a quotation still with / returned by the sales manager cannot be sent.</summary>
        public const string AgreementHeldMessage =
            "This quotation's discount is waiting for the sales manager's approval (or was sent back). The agreement can be sent once it is approved.";

        /// <summary>A sales executive's agreement for a discounted quotation the sales manager has not approved.</summary>
        public static string AgreementNeedsApprovalMessage(string discount, string? managerName) =>
            $"This quotation has a {discount} discount that the sales manager has not approved yet. Send the quotation first: it goes to "
            + (string.IsNullOrWhiteSpace(managerName) ? "your sales manager" : managerName.Trim())
            + " for approval, and the agreement can be sent once it is approved.";

        /// <summary>A sales executive's agreement for a quotation with a rent schedule the sales manager has not approved.</summary>
        public static string AgreementScheduleNeedsApprovalMessage(string? managerName) =>
            "This quotation's rent schedule has not been approved by the sales manager yet. Send the quotation first: it goes to "
            + (string.IsNullOrWhiteSpace(managerName) ? "your sales manager" : managerName.Trim())
            + " for approval, and the agreement can be sent once it is approved.";

        public static string SentToManagerMessage(string? managerName) =>
            string.IsNullOrWhiteSpace(managerName)
                ? "Sent to your sales manager for approval."
                : $"Sent to your sales manager {managerName.Trim()} for approval.";

        public static bool IsFixed(string? type) =>
            string.Equals(type?.Trim(), "Amount", StringComparison.OrdinalIgnoreCase) || string.Equals(type?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase);

        /// <summary>"Amount" for a fixed amount, otherwise "Percentage" (the contract's two values).</summary>
        public static string NormalizeType(string? type) => IsFixed(type) ? "Amount" : "Percentage";

        /// <summary>The manual discount a request carries (the value the create path uses: DiscountValue, else DiscountPercentage).</summary>
        public static decimal ManualValueOf(QuotationRequest request) =>
            request.DiscountValue > 0 ? request.DiscountValue : request.DiscountPercentage;

        /// <summary>
        /// The reason rule: with a manual discount > 0 the reason is required, 5..1000 characters (trimmed). Returns the trimmed
        /// reason to store (null without a discount) or throws <see cref="ArgumentException"/> with the 400 message.
        /// </summary>
        public static string? ValidateReason(decimal manualDiscountValue, string? reason)
        {
            if (manualDiscountValue <= 0m) return string.IsNullOrWhiteSpace(reason) ? null : Clean(reason);
            var text = Clean(reason);
            if (text.Length < ReasonMinLength) throw new ArgumentException(ReasonRequiredMessage);
            if (text.Length > ReasonMaxLength) throw new ArgumentException(ReasonTooLongMessage);
            return text;
        }

        /// <summary>Plain text: trimmed, control characters (other than line breaks) removed.</summary>
        public static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (var ch in text.Trim())
                if (!char.IsControl(ch) || ch == '\n' || ch == '\r') sb.Append(ch);
            return sb.ToString().Trim();
        }

        /// <summary>The stored manual discount of a quotation row (WN_Quotations): (type, value). Percentage: DiscountPercentage; Amount: DiscountAmount.</summary>
        public static (string Type, decimal Value) ManualDiscountOf(IDictionary<string, object?> header)
        {
            string type = NormalizeType(header.TryGetValue("DiscountType", out var t) ? t?.ToString() : null);
            decimal pct = Dec(header, "DiscountPercentage");
            decimal amt = Dec(header, "DiscountAmount");
            return (type, type == "Amount" ? amt : pct);
        }

        private static decimal Dec(IDictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var v) && v is not null && v is not DBNull ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;

        public static bool HasManualDiscount(IDictionary<string, object?> header) => ManualDiscountOf(header).Value > 0m;

        /// <summary>True when sending this quotation must go to the sales manager first.</summary>
        public static bool NeedsApproval(bool actorIsSalesExecutive, bool hasManualDiscount) => actorIsSalesExecutive && hasManualDiscount;

        /// <summary>"10%" or "PKR 5,000" (history from / to).</summary>
        public static string DiscountLabel(string? type, decimal value) =>
            IsFixed(type)
                ? "PKR " + value.ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

        /// <summary>A quotation still waiting for / returned by the sales manager is not the customer's to see.</summary>
        public static bool IsHeldBack(string? approvalStatus) =>
            string.Equals(approvalStatus, PendingManager, StringComparison.OrdinalIgnoreCase)
            || string.Equals(approvalStatus, SentBack, StringComparison.OrdinalIgnoreCase)
            || string.Equals(approvalStatus, NotSubmitted, StringComparison.OrdinalIgnoreCase);

        /// <summary>Internal fields (reason, approval note, manager) are for staff only: cleared on what a customer reads.</summary>
        public static void Redact(QuotationResponse q)
        {
            q.DiscountReason = null;
            q.ApprovalStatus = null;
            q.ApprovalNote = null;
            q.ApprovalManagerName = null;
            q.ApprovalSubmittedAt = null;
        }
    }
}

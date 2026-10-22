using WorkNest.Application.Interfaces;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// The checks the API makes BEFORE calling a One Bill procedure (OB_06: the procedures trust what they get). A value
    /// that fails is never sent to the database.
    /// </summary>
    public static class OneBillRules
    {
        /// <summary>The bill number holds the invoice id in 8 digits (ref = biller code + 8, number = prefix + 8).</summary>
        public const int MaxInvoiceId = 99_999_999;

        public const int MaxBillNoLength = 30;
        public const int MaxViaLength = 50;
        public const int MaxRemoteIpLength = 64;
        public const int MaxErrorLength = 500;
        public const int MaxPendingTop = 200;

        /// <summary>Largest amount DECIMAL(18, 2) holds.</summary>
        public const decimal MaxAmount = 9_999_999_999_999_999.99m;

        /// <summary>An invoice id One Bill can send: 1 .. 99,999,999.</summary>
        public static bool IsSendableInvoiceId(int invoiceId) => invoiceId > 0 && invoiceId <= MaxInvoiceId;

        /// <summary>How many pending invoices one sync reads: 1 .. 200.</summary>
        public static int PendingTop(int top) => Math.Clamp(top, 1, MaxPendingTop);

        /// <summary>Attempts per invoice from the settings: 1 .. 50.</summary>
        public static int MaxAttempts(int configured) => Math.Clamp(configured, 1, 50);

        /// <summary>
        /// Why an EzPay payment notification cannot be recorded, or null when it can. Bill number: digits only, 1 .. 30;
        /// amount: above 0, at most 2 decimals, within DECIMAL(18, 2).
        /// </summary>
        public static string? NoticeError(OneBillPaymentNotice notice)
        {
            var bill = (notice.BillNo ?? "").Trim();
            if (bill.Length == 0) return "billno is required.";
            if (bill.Length > MaxBillNoLength) return $"billno can be at most {MaxBillNoLength} digits.";
            if (!bill.All(char.IsAsciiDigit)) return "billno must contain digits only.";
            if (notice.Amount <= 0) return "rcvdamount must be more than zero.";
            if (notice.Amount > MaxAmount) return "rcvdamount is too large.";
            if (decimal.Round(notice.Amount, 2) != notice.Amount) return "rcvdamount can have at most 2 decimals.";
            return null;
        }

        /// <summary>Trimmed, at most <paramref name="max"/> characters; null when blank.</summary>
        public static string? Cut(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = value.Trim();
            return v.Length <= max ? v : v[..max];
        }
    }
}

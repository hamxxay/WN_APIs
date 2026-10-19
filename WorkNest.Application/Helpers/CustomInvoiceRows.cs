using System.Globalization;
using WorkNest.Application.DTOs.CustomInvoice;

namespace WorkNest.Application.Helpers
{
    /// <summary>
    /// Rows of the CI_01 procedures (column name -> value, DBNull already null, names case-insensitive) as the custom
    /// invoice DTOs. A missing column reads as null / 0, so a procedure with fewer columns still maps. Dates become
    /// yyyy-MM-dd text.
    /// </summary>
    public static class CustomInvoiceRows
    {
        public static CustomInvoiceCreated Created(IDictionary<string, object?> r) => new()
        {
            InvoiceId = Int(r, "InvoiceId"),
            InvoiceNumber = Str(r, "InvoiceNumber"),
            PublicId = Str(r, "PublicId"),
            VoucherId = IntOrNull(r, "VoucherId"),
            VoucherCode = Str(r, "VoucherCode"),
            ReplacedInvoiceNumber = Str(r, "ReplacedInvoiceNumber"),
            ReversalVoucherCode = Str(r, "ReversalVoucherCode")
        };

        public static CustomInvoiceCustomer Customer(IDictionary<string, object?> r) => new()
        {
            UserId = Int(r, "UserId"),
            CustomerId = IntOrNull(r, "CustomerId"),
            Name = Str(r, "Name"),
            Email = Str(r, "Email"),
            Code = Str(r, "Code"),
            Phone = Str(r, "Phone")
        };

        public static CustomInvoiceBooking Booking(IDictionary<string, object?> r) => new()
        {
            BookingId = Int(r, "BookingId"),
            Label = Str(r, "Label"),
            SpaceName = Str(r, "SpaceName"),
            LocationId = IntOrNull(r, "LocationId"),
            LocationName = Str(r, "LocationName"),
            StartOn = DateText(r, "StartOn"),
            EndOn = DateText(r, "EndOn"),
            Status = Str(r, "Status")
        };

        public static CustomInvoiceReplaceable Replaceable(IDictionary<string, object?> r) => new()
        {
            InvoiceId = Int(r, "InvoiceId"),
            InvoiceNumber = Str(r, "InvoiceNumber"),
            IssuedOn = DateText(r, "IssuedOn"),
            PeriodStart = DateText(r, "PeriodStart"),
            PeriodEnd = DateText(r, "PeriodEnd"),
            GrandTotal = Dec(r, "GrandTotal"),
            InvoiceTypeId = Int(r, "InvoiceTypeId")
        };

        public static CustomInvoiceAccount Account(IDictionary<string, object?> r) => new()
        {
            AccountId = Int(r, "AccountId"),
            Code = Str(r, "Code"),
            Name = Str(r, "Name")
        };

        /// <summary>
        /// The one row of dbo.WN_CustomInvoice_GetLedgerDefaults: AccountReceivableId / Code / Name, RentAccountId /
        /// RentCode / RentName, ServicesIncomeId / ServicesIncomeCode / ServicesIncomeName, SalesTaxId / SalesTaxCode /
        /// SalesTaxName, SecurityReceivedId / SecurityReceivedCode / SecurityReceivedName. An account without an id is null.
        /// </summary>
        public static CustomInvoiceLedgerDefaults LedgerDefaults(IDictionary<string, object?>? r)
        {
            if (r == null) return new CustomInvoiceLedgerDefaults();
            return new CustomInvoiceLedgerDefaults
            {
                AccountReceivable = LedgerAccount(r, new[] { "AccountReceivableId" },
                    new[] { "AccountReceivableCode", "AccountReceivableIdCode", "ARCode", "ReceivableCode", "Code" },
                    new[] { "AccountReceivableName", "AccountReceivableIdName", "ARName", "ReceivableName", "Name" }),
                Rent = LedgerAccount(r, new[] { "RentAccountId", "RentId" },
                    new[] { "RentCode", "RentAccountCode" }, new[] { "RentName", "RentAccountName" }),
                ServicesIncome = LedgerAccount(r, new[] { "ServicesIncomeId", "ServicesIncomeAccountId" },
                    new[] { "ServicesIncomeCode" }, new[] { "ServicesIncomeName" }),
                SalesTax = LedgerAccount(r, new[] { "SalesTaxId", "SalesTaxAccountId" },
                    new[] { "SalesTaxCode" }, new[] { "SalesTaxName" }),
                SecurityReceived = LedgerAccount(r, new[] { "SecurityReceivedId", "SecurityReceivedAccountId" },
                    new[] { "SecurityReceivedCode" }, new[] { "SecurityReceivedName" })
            };
        }

        public static CustomInvoiceListItem ListItem(IDictionary<string, object?> r) => new()
        {
            InvoiceId = Int(r, "InvoiceId"),
            InvoiceNumber = Str(r, "InvoiceNumber"),
            CustomerName = Str(r, "CustomerName"),
            BookingLabel = Str(r, "BookingLabel"),
            GrandTotal = Dec(r, "GrandTotal"),
            IssuedOn = DateText(r, "IssuedOn"),
            StatusLabel = Str(r, "StatusLabel"),
            Reason = Str(r, "Reason"),
            CreatedBy = Str(r, "CreatedBy"),
            VoucherCode = Str(r, "VoucherCode"),
            ReplacedInvoiceNumber = Str(r, "ReplacedInvoiceNumber")
        };

        public static CustomInvoiceAccessItem AccessItem(IDictionary<string, object?> r) => new()
        {
            UserId = Int(r, "UserId"),
            Name = Str(r, "Name"),
            Email = Str(r, "Email"),
            LocationNames = SplitNames(Str(r, "LocationNames")),
            HasAccess = Bool(r, "HasAccess"),
            GrantedBy = Str(r, "GrantedBy"),
            GrantedAt = Value(r, "GrantedAt") switch
            {
                DateTime dt => dt,
                DateTimeOffset dto => dto.UtcDateTime,
                _ => null
            }
        };

        /// <summary>A permission row of dbo.WN_UserPermissions_GetByUser (column Permission, else the first column).</summary>
        public static string? Permission(IDictionary<string, object?> r) =>
            Str(r, "Permission") ?? r.Values.FirstOrDefault(v => v is string)?.ToString();

        /// <summary>"I-8, F-7" -> ["I-8", "F-7"].</summary>
        public static string[] SplitNames(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? Array.Empty<string>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // ---- values ----------------------------------------------------------------------------------------------

        private static CustomInvoiceAccount? LedgerAccount(IDictionary<string, object?> r, string[] idColumns, string[] codeColumns, string[] nameColumns)
        {
            var id = idColumns.Select(c => IntOrNull(r, c)).FirstOrDefault(v => v.HasValue);
            if (id is not > 0) return null;
            return new CustomInvoiceAccount
            {
                AccountId = id.Value,
                Code = codeColumns.Select(c => Str(r, c)).FirstOrDefault(v => v != null),
                Name = nameColumns.Select(c => Str(r, c)).FirstOrDefault(v => v != null)
            };
        }

        private static object? Value(IDictionary<string, object?> r, string key) =>
            r.TryGetValue(key, out var v) && v is not null and not DBNull ? v : null;

        private static int Int(IDictionary<string, object?> r, string key) => IntOrNull(r, key) ?? 0;

        private static int? IntOrNull(IDictionary<string, object?> r, string key)
        {
            var v = Value(r, key);
            if (v == null) return null;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
        }

        private static decimal Dec(IDictionary<string, object?> r, string key) =>
            Value(r, key) is { } v ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;

        private static string? Str(IDictionary<string, object?> r, string key) =>
            Value(r, key) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        private static bool Bool(IDictionary<string, object?> r, string key) =>
            Value(r, key) switch
            {
                null => false,
                bool b => b,
                string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
                var v => Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0
            };

        /// <summary>A date column as yyyy-MM-dd (null when empty).</summary>
        public static string? DateText(IDictionary<string, object?> r, string key) =>
            Value(r, key) switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset dto => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) =>
                    p.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                { } other => other.ToString(),
                _ => null
            };
    }
}

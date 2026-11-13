namespace WorkNest.Application.InvoiceContent
{
    public enum InvoiceContentFieldKind { Text, Textarea, List }

    public sealed record InvoiceContentFieldDef(
        string Key, string Label, InvoiceContentFieldKind Kind, bool Required, int MaxLength, string? Help,
        string[]? Tokens = null, bool AllowsDepositTag = false);

    public sealed record InvoiceContentGroupDef(string Key, string Title, IReadOnlyList<InvoiceContentFieldDef> Fields);

    /// <summary>Keys of the fields (the CMS never hardcodes them: it renders its form from GET api/admin/invoice-content/schema).</summary>
    public static class InvoiceContentKeys
    {
        // Payment instructions (statement invoice; the bank values are shared with the sales-tax invoice)
        public const string OneBillWithNumber = "oneBillLineWithNumber";
        public const string OneBillWithoutNumber = "oneBillLineWithoutNumber";
        public const string BankIntro = "bankIntro";
        public const string BankName = "bankName";
        public const string AccountTitle = "accountTitle";
        public const string AccountNumber = "accountNumber";
        public const string Iban = "iban";
        public const string BankBranch = "bankBranch";
        public const string BankAddress = "bankAddress";
        public const string SwiftCode = "swiftCode";
        public const string BranchCode = "branchCode";
        public const string ReceiptLeadIn = "receiptLeadIn";
        public const string ReceiptContact = "receiptContact";
        // Terms and labels (statement invoice)
        public const string TermsHeading = "termsHeading";
        public const string Terms = "terms";
        public const string WhtTerm = "whtTerm";
        public const string WhtDepositTerm = "whtDepositTerm";
        public const string HeadDescription = "tableHeadDescription";
        public const string HeadUnitPrice = "tableHeadUnitPrice";
        public const string HeadTotal = "tableHeadTotal";
        // Sales-tax invoice
        public const string StOneBillWithNumber = "stOneBillLineWithNumber";
        public const string StOneBillWithoutNumber = "stOneBillLineWithoutNumber";
        public const string StBankIntro = "stBankIntro";
        public const string StReceiptLeadIn = "stReceiptLeadIn";
        public const string StTerms = "stTerms";
        public const string StHeadDescription = "stTableHeadDescription";
        public const string StHeadExclusive = "stTableHeadExclusive";
        public const string StHeadTaxRate = "stTableHeadTaxRate";
        public const string StHeadTaxAmount = "stTableHeadTaxAmount";
        public const string StHeadLineTotal = "stTableHeadLineTotal";
    }

    /// <summary>
    /// The single list of configurable fields. Every one prints inside an existing area of the invoice PDFs; nothing here moves or
    /// restyles anything, and no amount, date, customer detail or tax value is configurable.
    /// </summary>
    public static class InvoiceContentSchema
    {
        public const int MaxListItems = 20;
        public const string DepositTag = "@deposit ";
        public const string OneBillNumberToken = "{OneBillNumber}";
        public const string AmountToken = "{Amount}";

        private static InvoiceContentFieldDef T(string key, string label, int max, string? help = null, bool required = false, string[]? tokens = null)
            => new(key, label, InvoiceContentFieldKind.Text, required, max, help, tokens);

        public static readonly IReadOnlyList<InvoiceContentGroupDef> Groups = new List<InvoiceContentGroupDef>
        {
            new("payment", "Payment instructions (invoice)", new[]
            {
                T(InvoiceContentKeys.OneBillWithNumber, "1Bill line (invoice has a 1Bill number)", 300,
                    "Printed as the first payment line, in bold, when the invoice has a 1Bill consumer number. Use {OneBillNumber} where the number goes. Leave empty for no 1Bill line.", tokens: new[] { OneBillNumberToken }),
                T(InvoiceContentKeys.OneBillWithoutNumber, "1Bill line (no 1Bill number yet)", 300,
                    "Printed in bold when the invoice has no 1Bill number yet. Leave empty for no 1Bill line.", tokens: Array.Empty<string>()),
                T(InvoiceContentKeys.BankIntro, "Bank deposit line", 300,
                    "Sentence that introduces the bank details, e.g. 'Please deposit this payment in the following bank account:'. Leave empty (with the bank fields) when this location has no bank account instructions."),
                T(InvoiceContentKeys.BankName, "Bank name", 100, "Printed as 'Bank Name: ...'. Empty = the line is left out."),
                T(InvoiceContentKeys.AccountTitle, "Account title", 150, "Printed as 'Account Title: ...'."),
                T(InvoiceContentKeys.AccountNumber, "Account number", 60, "Printed as 'Account Number: ...'."),
                T(InvoiceContentKeys.Iban, "IBAN", 60, "Printed as 'IBAN: ...'."),
                T(InvoiceContentKeys.BankBranch, "Branch", 100, "Printed as 'Branch: ...'. Empty = the line is left out."),
                T(InvoiceContentKeys.BankAddress, "Bank address", 200, "Printed as 'Address: ...'. Empty = the line is left out."),
                T(InvoiceContentKeys.SwiftCode, "SWIFT code", 40, "Printed as 'Swift code: ...'."),
                T(InvoiceContentKeys.BranchCode, "Branch code", 40, "Printed as 'Branch code: ...'."),
                T(InvoiceContentKeys.ReceiptLeadIn, "Receipt line wording", 100, "Start of the last payment line, e.g. 'And send the receipt on'. The receipt contact follows it."),
                T(InvoiceContentKeys.ReceiptContact, "Receipt contact", 100, "Phone number or e-mail the payment receipt is sent to."),
            }),
            new("terms", "Terms & conditions (invoice)", new[]
            {
                T(InvoiceContentKeys.TermsHeading, "Terms heading", 60, null, required: true),
                new InvoiceContentFieldDef(InvoiceContentKeys.Terms, "Terms (in order)", InvoiceContentFieldKind.List, true, 300,
                    "Numbered after the payment lines, in this order. Plain text only."),
                T(InvoiceContentKeys.WhtTerm, "Tax-withheld line", 300,
                    "Printed in bold after the terms (not on withholding-tax invoices). {Amount} is the invoice amount if tax is withheld, calculated by the system. Leave empty to leave the line out.", tokens: new[] { AmountToken }),
                T(InvoiceContentKeys.WhtDepositTerm, "Security-deposit tax line", 300,
                    "Printed in bold when the invoice has a security deposit (not on withholding-tax invoices). Leave empty to leave the line out."),
            }),
            new("table", "Invoice table headings", new[]
            {
                T(InvoiceContentKeys.HeadDescription, "Description column", 40, null, required: true),
                T(InvoiceContentKeys.HeadUnitPrice, "Unit price column", 40, null, required: true),
                T(InvoiceContentKeys.HeadTotal, "Total column", 40, null, required: true),
            }),
            new("salesTax", "Sales-tax invoice", new[]
            {
                T(InvoiceContentKeys.StOneBillWithNumber, "1Bill line (invoice has a 1Bill number)", 300,
                    "Same as for the invoice. Leave empty for no 1Bill line on the sales-tax invoice.", tokens: new[] { OneBillNumberToken }),
                T(InvoiceContentKeys.StOneBillWithoutNumber, "1Bill line (no 1Bill number yet)", 300, null, tokens: Array.Empty<string>()),
                T(InvoiceContentKeys.StBankIntro, "Bank deposit line", 300,
                    "Introduces the bank details above (shared with the invoice). Leave empty when there are no bank instructions."),
                T(InvoiceContentKeys.StReceiptLeadIn, "Receipt line wording", 100, "Start of the last payment line, e.g. 'And send the receipt to'."),
                new InvoiceContentFieldDef(InvoiceContentKeys.StTerms, "Terms (in order)", InvoiceContentFieldKind.List, true, 300,
                    "Numbered after the payment lines. Start a line with '@deposit ' to print it only when the invoice has a security deposit.", null, true),
                T(InvoiceContentKeys.StHeadDescription, "Description column", 40, null, required: true),
                T(InvoiceContentKeys.StHeadExclusive, "Exclusive amount column", 40, null, required: true),
                T(InvoiceContentKeys.StHeadTaxRate, "Tax rate column", 40, null, required: true),
                T(InvoiceContentKeys.StHeadTaxAmount, "Tax amount column", 40, null, required: true),
                T(InvoiceContentKeys.StHeadLineTotal, "Line total column", 40, null, required: true),
            }),
        };

        public static readonly IReadOnlyList<InvoiceContentFieldDef> AllFields = Groups.SelectMany(g => g.Fields).ToList();

        private static readonly Dictionary<string, InvoiceContentFieldDef> ByKey = AllFields.ToDictionary(f => f.Key, StringComparer.Ordinal);

        public static InvoiceContentFieldDef? Find(string key) => ByKey.TryGetValue(key, out var f) ? f : null;

        /// <summary>The shape of GET api/admin/invoice-content/schema.</summary>
        public static object ToApi() => new
        {
            groups = Groups.Select(g => new
            {
                key = g.Key,
                title = g.Title,
                fields = g.Fields.Select(f => new
                {
                    key = f.Key,
                    label = f.Label,
                    kind = f.Kind == InvoiceContentFieldKind.List ? "list" : f.Kind == InvoiceContentFieldKind.Textarea ? "textarea" : "text",
                    required = f.Required,
                    maxLength = f.MaxLength,
                    help = f.Help
                })
            })
        };
    }
}

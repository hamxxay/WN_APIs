using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WorkNest.Application.InvoiceContent;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    /// <summary>
    /// Turns the invoice content (<see cref="InvoiceContentFields"/>) into the numbered lines of the "Terms &amp; Conditions" box. Both invoice
    /// PDFs (invoice and sales-tax invoice) use this one routine, so the payment instructions cannot differ between them. It only
    /// decides WHICH text is printed: the box, its font size and colour stay in the generators.
    /// </summary>
    public static class InvoiceContentRenderer
    {
        public sealed record TermLine(string Text, bool Bold);

        /// <summary>The payment instruction lines: the 1Bill line, the bank deposit line, the bank details, the receipt line (numbering continues in <paramref name="itemNum"/>).</summary>
        public static List<TermLine> PaymentLines(InvoiceContentFields c, string? oneBillNumber, bool salesTax, ref int itemNum)
        {
            var lines = new List<TermLine>();
            bool hasNumber = !string.IsNullOrWhiteSpace(oneBillNumber);
            string oneBill = hasNumber
                ? c.Get(salesTax ? InvoiceContentKeys.StOneBillWithNumber : InvoiceContentKeys.OneBillWithNumber).Replace(InvoiceContentSchema.OneBillNumberToken, oneBillNumber)
                : c.Get(salesTax ? InvoiceContentKeys.StOneBillWithoutNumber : InvoiceContentKeys.OneBillWithoutNumber);
            if (!string.IsNullOrWhiteSpace(oneBill))
                lines.Add(new TermLine($"{itemNum++}. {oneBill}", true));

            string intro = c.Get(salesTax ? InvoiceContentKeys.StBankIntro : InvoiceContentKeys.BankIntro);
            if (!string.IsNullOrWhiteSpace(intro))
                lines.Add(new TermLine($"{itemNum++}. {intro}", false));

            void Detail(string label, string key) { var v = c.Get(key); if (!string.IsNullOrWhiteSpace(v)) lines.Add(new TermLine($"{label}: {v}", false)); }
            Detail("Bank Name", InvoiceContentKeys.BankName);
            Detail("Account Title", InvoiceContentKeys.AccountTitle);
            Detail("Account Number", InvoiceContentKeys.AccountNumber);
            Detail("IBAN", InvoiceContentKeys.Iban);
            Detail("Branch", InvoiceContentKeys.BankBranch);       // optional (Location 3)
            Detail("Address", InvoiceContentKeys.BankAddress);     // optional (Location 3)
            Detail("Swift code", InvoiceContentKeys.SwiftCode);
            Detail("Branch code", InvoiceContentKeys.BranchCode);

            string contact = c.Get(InvoiceContentKeys.ReceiptContact);
            if (!string.IsNullOrWhiteSpace(contact))
            {
                string lead = c.Get(salesTax ? InvoiceContentKeys.StReceiptLeadIn : InvoiceContentKeys.ReceiptLeadIn);
                lines.Add(new TermLine(string.IsNullOrWhiteSpace(lead) ? contact : $"{lead} {contact}", false));
            }
            return lines;
        }

        /// <summary>The numbered terms of a list field; a line starting with "@deposit " is printed only when the invoice has a security deposit.</summary>
        public static List<TermLine> TermLines(IReadOnlyList<string> terms, bool hasDeposit, ref int itemNum)
        {
            var lines = new List<TermLine>();
            foreach (var raw in terms)
            {
                var text = raw;
                if (text.StartsWith(InvoiceContentSchema.DepositTag, StringComparison.Ordinal))
                {
                    if (!hasDeposit) continue;
                    text = text.Substring(InvoiceContentSchema.DepositTag.Length);
                }
                if (string.IsNullOrWhiteSpace(text)) continue;
                lines.Add(new TermLine($"{itemNum++}. {text}", false));
            }
            return lines;
        }

        /// <summary>Same item, size and colour as the terms always had.</summary>
        public static void Write(ColumnDescriptor tc, IEnumerable<TermLine> lines)
        {
            foreach (var l in lines)
            {
                var t = tc.Item().Text(l.Text);
                if (l.Bold) t = t.Bold();
                t.FontSize(7.5f).FontColor("#000000");
            }
        }
    }
}

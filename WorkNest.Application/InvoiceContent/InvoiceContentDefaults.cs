using static WorkNest.Application.InvoiceContent.InvoiceContentKeys;

namespace WorkNest.Application.InvoiceContent
{
    /// <summary>
    /// The wording the invoice PDFs printed before the content became configurable: Locations 1 and 2 exactly as they were, Location 3
    /// from the hardcoded Location-3 wording of commit 6330879 (its bank details are "[...]" placeholders). This is (a) version 1 that
    /// script INVC_01 seeds per location and (b) what the PDFs print while that script has not been run. Keep both in step: a test
    /// compares this with the seed in the script. Text is kept character for character, including trailing blanks.
    /// </summary>
    public static class InvoiceContentDefaults
    {
        /// <summary>The locations the settings page offers (Location 3 needs no row in WN_Locations).</summary>
        public static readonly int[] KnownLocationIds = { 1, 2, 3 };

        /// <summary>Location 1's content is also what any other / unknown location prints (as the PDFs always did).</summary>
        public const int FallbackLocationId = 1;

        public static InvoiceContentFields ForLocation(int? locationId) => locationId switch
        {
            2 => Location2(),
            3 => Location3(),
            _ => Location1(),
        };

        private static InvoiceContentFields Common()
        {
            var c = new InvoiceContentFields();
            c.Text[TermsHeading] = "Terms & Conditions";
            c.Lists[Terms] = new List<string>
            {
                "Your access will be suspended if dues are not paid by the due date ",
                "WorkNest will charge Provincial Sales Tax (PST) on support services.",
                "Payment is due on or before the due date specified on this invoice.",
                "WorkNest reserves the right to modify pricing and terms with prior notice.",
            };
            c.Text[WhtTerm] = "If tax is withheld, the invoice will be PKR {Amount}.";
            c.Text[WhtDepositTerm] = "Withholding tax is not applicable on the Security Deposit.";
            c.Text[HeadDescription] = "Description";
            c.Text[HeadUnitPrice] = "Unit Price";
            c.Text[HeadTotal] = "Total";
            c.Lists[StTerms] = new List<string>
            {
                "Your access will be closed if dues are not paid within 5 days of the due date.",
                "Payment is due on or before the due date specified on this invoice.",
                "@deposit Security deposit is fully refundable upon termination of the agreement, subject to lease terms.",
                "Booking confirmation is subject to space availability at the time of payment.",
                "WorkNest reserves the right to modify pricing and terms with prior notice.",
            };
            c.Text[StHeadDescription] = "Description";
            c.Text[StHeadExclusive] = "Exclusive Amt";
            c.Text[StHeadTaxRate] = "Tax Rate";
            c.Text[StHeadTaxAmount] = "Tax Amount";
            c.Text[StHeadLineTotal] = "Line Total";
            foreach (var k in new[] { OneBillWithNumber, OneBillWithoutNumber, BankIntro, BankName, AccountTitle, AccountNumber, Iban, BankBranch, BankAddress, SwiftCode, BranchCode,
                                      ReceiptLeadIn, ReceiptContact, StOneBillWithNumber, StOneBillWithoutNumber, StBankIntro, StReceiptLeadIn })
                c.Text[k] = "";
            return c;
        }

        /// <summary>Location 1 (I-8): Bank of Punjab account; the 1Bill line only when the invoice has a 1Bill number.</summary>
        public static InvoiceContentFields Location1()
        {
            var c = Common();
            c.Text[OneBillWithNumber] = "Pay with 1Bill from any bank app, ATM or bank branch using consumer number {OneBillNumber}.";
            c.Text[BankIntro] = "Please deposit this payment in the following bank ";
            c.Text[BankName] = "Bank Of Punjab";
            c.Text[AccountTitle] = "WORKNEST PRIVATE LIMITED";
            c.Text[AccountNumber] = "5310449521900010";
            c.Text[Iban] = "PK24BPUN5310449521900010 ";
            c.Text[SwiftCode] = "BPUNPKKA ";
            c.Text[BranchCode] = "0839 ";
            c.Text[ReceiptLeadIn] = "And send the receipt on";
            c.Text[ReceiptContact] = "+923201809696";
            c.Text[StBankIntro] = "Please deposit this payment into the following bank account: ";
            c.Text[StReceiptLeadIn] = "And send the receipt to";
            return c;
        }

        /// <summary>Location 2 (F-7): 1Bill only, no bank account lines.</summary>
        public static InvoiceContentFields Location2()
        {
            var c = Common();
            c.Text[OneBillWithNumber] = "Please use the 1Bill number to pay: {OneBillNumber} (Pay with 1Bill from any bank app, ATM or bank branch).";
            c.Text[OneBillWithoutNumber] = "Please use the 1Bill number to pay from any bank app, ATM or bank branch.";
            c.Text[StOneBillWithNumber] = c.Text[OneBillWithNumber];
            c.Text[StOneBillWithoutNumber] = c.Text[OneBillWithoutNumber];
            return c;
        }

        /// <summary>Location 3: its own bank-account block (Habib Metro Bank, commit 6e4ebef); SWIFT code and receipt contact are still
        /// "[...]" placeholders to be replaced by staff.</summary>
        public static InvoiceContentFields Location3()
        {
            var c = Common();
            c.Text[BankIntro] = "Please deposit this payment in the following bank account: ";
            c.Text[BankName] = "HABIB METRO BANK";
            c.Text[AccountTitle] = "SHAZAD ADIL & CO";
            c.Text[AccountNumber] = "6-02-10-20396-333-251261";
            c.Text[Iban] = "PK68MPBL0210403330251261";
            c.Text[BankBranch] = "ISLAMABAD BRANCH";
            c.Text[BankAddress] = "COMMERCIAL PLOT # 24, WEST BLUE AREA, F-6/G-6, ISLAMABAD.";
            c.Text[SwiftCode] = "[Location 3 SWIFT code]";
            c.Text[BranchCode] = "10";
            c.Text[ReceiptLeadIn] = "And send the receipt to";
            c.Text[ReceiptContact] = "[Location 3 contact number]";
            c.Text[StBankIntro] = "Please deposit this payment into the following bank account: ";
            c.Text[StReceiptLeadIn] = "And send the receipt to";
            return c;
        }
    }
}

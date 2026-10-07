# Withholding Tax (WHT) invoices

## Run order (SAC400, each file as ONE statement in DBeaver: select all, execute)

1. `WHT_01_migration.txt`: columns + CHECK constraints (idempotent). Set `@WhtAccountId` to the new
   "Withholding Tax Receivable" GL account and run again to populate `WN_ChargeTypeAccountMapping.WhtAccountId`
   for locations 1 and 2.
2. `WHT_02` … `WHT_08`: procedures (02 needs the columns from 01, 06-08 need 04).
3. Optional: `WHT_09_check_formula.txt`, a read-only check of the SQL rounding against the worked example.

Before running 02-08, compare each against the live procedure (`EXEC sp_helptext 'dbo.<name>'`). They are built
from `Sp.sql`; if someone changed a procedure on the server since, merge the marked edits into the live version
instead of replacing it.

## What changes

| Object | Change |
|---|---|
| `WN_Quotations` | `SendWhtInvoice BIT NOT NULL DEFAULT 0`, `CK_WN_Quotations_Wht` (rate = existing `WithholdingTaxRate`) |
| `WN_Bookings` | `SendWhtInvoice BIT NOT NULL DEFAULT 0`, `WHTRate` (added only if missing), `CK_WN_Bookings_Wht` |
| `WN_Invoices` | none: `WHTRate` / `WHTAmount` already exist (added only if missing) |
| `WN_ChargeTypeAccountMapping` | `WhtAccountId INT NULL` |
| `WN_Invoices_Insert` | WHT gross-up (step 11b), WHT GL debit, AR/customer ledger = net, balance guard; deposit only on a booking's first invoice |
| `WN_UpdateInvoiceBreakdown` | keeps WHT invoices grossed up when the breakdown is rewritten (re-send) |
| `WN_InsertInvoiceLine` | grosses up non-deposit lines of WHT invoices (API-created invoices) |
| `WN_Invoice_ApplyWhtToLines` (new) | same for lines inserted directly by the three procedures below |
| `WN_CreateAdvanceInvoice`, `WN_Invoice_CreateRecurring`, `WN_CreateSurchargeInvoice` | one `EXEC WN_Invoice_ApplyWhtToLines` before COMMIT |

Formula (C# twin: `WorkNest.Application/Services/WhtCalculator.cs`, tests in `WorkNest.Tests`):
`Gross = ROUND(Net / (1 - rate/100), 2)` for rent, service charges and tax on service charges;
`WhtAmount = (RentGross + ServiceGross + TaxGross) - Net`; deposit not grossed up; `InvoiceTypeId = 6`.

Posting for a WHT invoice: Dr AR (net + deposit), Dr WHT account (WhtAmount); Cr Rent, Cr Services income,
Cr Sales tax (gross), Cr Security received (deposit). Customer ledger debit = net + deposit.

## Manual test steps

1. Run the scripts; run `WHT_09`: both rows must show the expected values.
2. Create Quotation: tick "Generate Withholding Tax (WHT) invoice"; the WHT Rate box appears. Try saving
   empty and 150: both are blocked. Enter 10 and save. Untick: the box hides and clears.
3. Convert that quotation to a booking: `SELECT SendWhtInvoice, WHTRate FROM WN_Bookings WHERE Id = <id>`
   returns `1, 10`.
4. Create Booking directly with the box ticked (10%): same result on `WN_Bookings`.
5. Send the first invoice for a WHT booking with rent 100,000 / service 10,000 / tax 1,600 (or any amounts):
   - `WN_Invoices`: `InvoiceTypeId = 6`, `RoomRentExclTax / ServiceCharges / TaxOnServiceCharges` grossed up,
     `WHTRate = 10`, `WHTAmount = total - net`, `GrandTotal = gross + deposit`.
   - Voucher: AR debit = GrandTotal - WHTAmount, WHT debit = WHTAmount, credits = rent + service + tax + deposit;
     debits = credits.
   - Invoice lines: rent/support lines grossed up, deposit line unchanged.
6. Generate the next invoice and let the recurring job run for a WHT booking: type 6, no deposit, balanced.
7. A booking WITHOUT the box: invoices identical to before (type 1/2, `WHTAmount = 0`, same postings).
8. Without `WhtAccountId` set, a WHT invoice fails with "No WHT GL account …" and nothing is saved.

## Assumptions / notes

- The UI is Angular (`WorkNest_FE`), not Razor: shared component `app-wht-invoice-fields`.
- Quotations reuse `WithholdingTaxRate` and bookings reuse `WHTRate` instead of adding second rate columns.
  Unticked quotations still store 15% (the PDF's withholding note uses it, as today).
- Converting a quotation copies the flag and rate on the server (there is no booking form in that flow).
- Editing a booking does not change WHT (the booking edit API only updates dates and notes).
- Normal invoices keep `WHTRate = 0` (not NULL) as today, so their rows are unchanged.
- Line-level gross-up rounds per line, so lines can differ from the header by a few paisa; the header and the
  voucher are exact.
- Balance due for a WHT invoice: the customer pays GrandTotal - WHTAmount. Invoice paid status / payment recording
  does not yet account for WHT (payment recording is a separate open item).
- `Ledgers_Insert` is called once per line under one voucher (as today for 5 lines); a WHT invoice with a deposit
  makes 6 calls. If `Ledgers_Insert` has its own line limit, the invoice rolls back with its error.
- The deposit fix: only a booking's first invoice adopts the booking's security deposit, and only invoices that
  carry a deposit re-point `WN_SecurityDeposits.RefId/RefNo`.

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
| `WN_Invoices_Insert` | WHT gross-up of room rent (step 11b), WHT GL debit, AR/customer ledger = net, balance guard; deposit only on a booking's first invoice |
| `WN_UpdateInvoiceBreakdown` | keeps WHT invoices grossed up when the breakdown is rewritten (re-send) |
| `WN_InsertInvoiceLine` | grosses up room rent lines (ChargeTypeId 1) of WHT invoices (API-created invoices) |
| `WN_Invoice_ApplyWhtToLines` (new) | same for lines inserted directly by the three procedures below |
| `WN_CreateAdvanceInvoice`, `WN_Invoice_CreateRecurring`, `WN_CreateSurchargeInvoice` | one `EXEC WN_Invoice_ApplyWhtToLines` before COMMIT |
| `WN_GetBookingBillingSummary` | WHT invoices (type 6) count as rent invoiced / paid (`WHT_10`, also in the all-in-one) |
| `WN_GetStatementInvoicePdfData` | also returns `InvoiceTypeId`; the PDF leaves out terms "If tax is withheld…" and "Withholding tax is not applicable on the Security Deposit" on WHT invoices (`WHT_11`, also in the all-in-one) |

Formula (C# twin: `WorkNest.Application/Services/WhtCalculator.cs`, tests in `WorkNest.Tests`).
Room rent (rent + support) is grossed up as ONE amount and the rent line absorbs the whole increase:

- `RoomRentNet   = RentExclSupport + SupportCharge`
- `GrossRoomRent = ROUND(RoomRentNet / (1 - rate/100), 2)`
- `RentLine      = GrossRoomRent - SupportCharge` (stored in `RoomRentExclTax`)
- `SupportLine   = SupportCharge` (fixed in the DB, never grossed up; `ServiceCharges`)
- `TaxLine       = existing tax` (16% of the support charge, not grossed up; `TaxOnServiceCharges`)
- `WhtAmount     = GrossRoomRent - RoomRentNet`; deposit not grossed up; `InvoiceTypeId = 6`.

Tax base check: tax is calculated on the support charge only (`InvoiceCalculationEngine`, `BookingService`, the
recurring / advance / surcharge procedures), never on rent, so it is unaffected.

Worked example (WHT 10%): RentExclSupport 33,000, Support 2,000, Tax 320 -> GrossRoomRent 38,888.89,
RentLine 36,888.89, Support 2,000, Tax 320, WHT 3,888.89, AR 35,320.00. Debits 35,320.00 + 3,888.89 = 39,208.89 =
credits 36,888.89 + 2,000 + 320.

Posting for a WHT invoice (same 5 entries, plus the deposit when the invoice carries one): Dr AR (RoomRentNet + Tax
+ deposit), Dr WHT account (WhtAmount); Cr Rent (RentLine), Cr Services income (SupportLine), Cr Sales tax (TaxLine),
Cr Security received (deposit). Customer ledger debit = AR debit. Debits must equal credits or the invoice rolls back.

The support charges (sales tax) invoice is unchanged: it is built from `ServiceCharges` / `TaxOnServiceCharges`,
which a WHT invoice no longer changes, and its link is embedded exactly as on a normal invoice.

## Manual test steps

1. Run the scripts; run `WHT_09`: both rows must show the expected values (TotalDebits = TotalCredits).
2. Create Quotation: tick "Generate Withholding Tax (WHT) invoice"; the WHT Rate box appears. Try saving
   empty and 150: both are blocked. Enter 10 and save. Untick: the box hides and clears.
3. Convert that quotation to a booking: `SELECT SendWhtInvoice, WHTRate FROM WN_Bookings WHERE Id = <id>`
   returns `1, 10`.
4. Create Booking directly with the box ticked (10%): same result on `WN_Bookings`.
5. Send the first invoice for a WHT booking (10%) with rent excl. support 33,000 / support 2,000 / tax 320:
   - `WN_Invoices`: `InvoiceTypeId = 6`, `RoomRentExclTax = 36888.89`, `ServiceCharges = 2000.00`,
     `TaxOnServiceCharges = 320.00`, `WHTRate = 10`, `WHTAmount = 3888.89`, `GrandTotal = 39208.89 + deposit`.
   - Voucher: AR debit = 35,320.00 (+ deposit), WHT debit = 3,888.89, credits rent 36,888.89 + services 2,000 +
     sales tax 320 (+ deposit); debits = credits.
   - Invoice lines: the room rent line is grossed up (rent + support), the deposit line is unchanged.
   - The invoice PDF shows the Sales Tax / support invoice link, and that invoice shows 2,000 + 320 as before.
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
  voucher are exact. Only room rent lines (ChargeTypeId 1, which carry rent + support) are grossed up; a separate
  support/services line (ChargeTypeId 4), tax line (3) or deposit line (2) is left as given.
- Balance due for a WHT invoice: the customer pays GrandTotal - WHTAmount. Invoice paid status / payment recording
  does not yet account for WHT (payment recording is a separate open item).
- `Ledgers_Insert` is called once per line under one voucher (as today for 5 lines); a WHT invoice with a deposit
  makes 6 calls. If `Ledgers_Insert` has its own line limit, the invoice rolls back with its error.
- The deposit fix: only a booking's first invoice adopts the booking's security deposit, and only invoices that
  carry a deposit re-point `WN_SecurityDeposits.RefId/RefNo`.

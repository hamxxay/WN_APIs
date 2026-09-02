# Invoice Generation Prompt Template
(Based on Regus / IWG "Statement of Account + Invoice" format)

Use this as a prompt to an AI, or as a fill-in-the-blank template, to produce
documents matching this multi-page invoice structure.

---

## PROMPT

### Step 0 — Align to actual database schema (do this first)
Before populating any fields below, open and read the schema file at:

    F:\WN_APIs\Schema.sql

Identify the actual table and column names relevant to invoicing/booking
(e.g. Booking, WN_Locations, and any rate/charge/tax tables). Then:
- Replace every `{{PLACEHOLDER}}` variable name below with the matching
  column name from Schema.sql (e.g. if the schema uses `InvoiceNo` instead
  of `invoice_number`, rename it throughout).
- If a placeholder has no corresponding column, flag it explicitly rather
  than guessing a name.
- Preserve the *shape* of the line_items array and glossary tables even if
  column names change — only the naming should be swapped, not the
  structure, unless the schema implies a structural difference (e.g. tax
  stored as a percentage per province rather than a flat VAT rate).

Once the schema check is done, generate the invoice packet using the
aligned names.

### Step 0a — Implementation location (strict constraint)
Implement the invoice/PDF generation logic described below as a **new
file** under:

    F:\WN_APIs\WorkNest.Infrastructure\ExternalServices\Pdf

**Do not modify, refactor, or touch any other existing file or
functionality in the codebase to accomplish this.** This is a strict
scope boundary:
- If the new invoice logic needs something from existing code (a shared
  model, a connection string, a rate lookup, etc.), reference/consume it
  as-is from its current location — do not move it, rename it, or
  "clean it up" as a side effect of this task.
- If accomplishing this genuinely cannot be done without changing
  something outside the new Pdf file/folder (e.g. a missing interface
  registration, a DI container entry needed to wire the new service in),
  stop and flag that specific, minimal necessary change explicitly for
  approval rather than making it silently alongside everything else.
- No unrelated formatting changes, renames, or "while I'm in here"
  edits to other files.

### Step 1 — Generate the document

You are generating a 5-page commercial office-space invoice packet in the
style of a Regus/IWG statement. Use the following data to populate it, and
preserve this exact page structure and section order:

### Input variables
- account_name: {{ACCOUNT_NAME}}
- attn_name: {{CONTACT_NAME}}
- billing_address: {{ADDRESS_LINES}}
- account_number: {{ACCOUNT_NUMBER}}
- invoice_number: {{INVOICE_NUMBER}}
- statement_date: {{STATEMENT_DATE}}
- invoice_date: {{INVOICE_DATE}}
- due_date: {{DUE_DATE}}
- sntn_ntn_nic: {{SNTN_NTN_NIC}}
- center_name: {{CENTER_NAME}}
- previous_outstanding_balance: {{PREV_BALANCE}}
- payment_received: {{PAYMENT_RECEIVED}}
- current_invoice_total: {{CURRENT_INVOICE_TOTAL}}
- total_outstanding_due: {{TOTAL_DUE}} (= prev_balance - payment_received + current_invoice_total)
- line_items: [
    { description, from_date, to_date, price_excl_vat, vat_amount, total_incl_vat },
    ...
  ]
- vat_rate: {{VAT_RATE}} (e.g. 15%)
- bank_name, bank_address, branch_code, bank_account_name,
  bank_account_number, swift_bic, iban
- vendor_legal_name, vendor_address, vendor_phone, vendor_fax, vendor_ntn

### Required page structure

**Page 1 — Statement of Account**
- Header: "STATEMENT OF ACCOUNT"
- Account name / Attn / billing address (left) | Account number, Invoice
  number, Statement date, Due date (right)
- "USEFUL INFORMATION" box (support links/phone)
- Center name
- Table: Date | Description | Amount
  - Rows: prior outstanding balance, payments received (negative), "This
    invoice" (current total)
- Bold total line: "Total outstanding balance due"
- Footer: vendor legal name, address, phone/fax, NTN, logo

**Page 2 — Invoice**
- Header: "INVOICE"
- Same account block, but right column adds SNTN/NTN/NIC
- Center name
- Table: Description of Charges | From Date | To Date | Price | VAT Amount | Total
  - Grouped under a tariff heading (e.g. "Business Support
    Services/Tariff Heading:XXXX.XXXX")
- Subtotal: Total (exc. VAT), VAT{{rate}}, "{{Month}} invoice total (inc. Tax)"
- Note: "See next page for an itemized breakdown of charges"
- Footer (same as page 1)

**Page 3 — Your Invoice Details**
- Header: "YOUR INVOICE DETAILS"
- Account block (no SNTN)
- "RECURRING CHARGES" table: Item Description | From Date | To Date |
  Price | VAT Amount | Total (inc. VAT) — one row per billable item/room
- Subtotal row, then bold "Total Charges" row
- Footer (same as page 1)

**Page 4 — Methods of Payment + Understanding Your Invoice (part 1)**
- Header: "METHODS OF PAYMENT"
- One-line note: online payment method update link
- "You may pay by Bank Transfer to:" block — bank name, address, branch
  code, account name, account number, BIC/Swift, IBAN
- Bold note: "Please provide your Invoice Number <{{invoice_number}}> as a
  payee reference on all payments made."
- Header: "UNDERSTANDING YOUR INVOICE"
- "INVOICE EXPLANATIONS" glossary table (term | definition) covering:
  Account adjustments/refunds, Account balance, Credits, Due date,
  Invoice, Late payment fees, One-off charges incurred, Payments received,
  Recurring charges, Total payment due
- Partial "RECURRING CHARGES" glossary begins (e.g. IT Services)
- Footer (same as page 1)

**Page 5 — Understanding Your Invoice (part 2)**
- Continuation of "RECURRING CHARGES" glossary: Kitchen Amenities, Office
  (and any other charge types)
- Footer (same as page 1)

### Formatting rules
- Currency format: "{{CURRENCY}} {{amount with thousands separator and 2 decimals}}"
- Negative amounts (payments) shown with a leading minus sign
- Bold: section totals, "Total outstanding balance due", "Total Charges",
  due date value
- Every page repeats the same footer block (vendor name, address,
  phone/fax, NTN, logo, page number)

---

## Notes for reuse
- Swap {{CENTER_NAME}}, {{line_items}}, and vendor/bank blocks to adapt this
  to a different office provider or client.
- Keep the four-document structure (Statement → Invoice → Details →
  Payment/Glossary) if the goal is a byte-for-byte format match; drop the
  glossary pages if you just need a functional invoice for your own
  business.
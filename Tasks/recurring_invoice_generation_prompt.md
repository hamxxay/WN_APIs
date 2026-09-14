# Recurring Invoice Generation & Delivery — Implementation Prompt

Use this as a prompt for an AI coding agent (Amazon Q, Claude Code, etc.)
working inside the SAC400 SQL Server booking project.

---

## PROMPT

### Step 0 — Align to actual schema (do this first)
**Naming convention: every table and stored procedure in this database
uses a `WN_` prefix** (e.g. `WN_Bookings`, `WN_Locations`,
`WN_AccessCards`). Any new table or stored procedure created for this
feature must follow the same convention — do not introduce unprefixed
names (`Invoices`, `usp_...`, etc.). This applies throughout Steps 2, 5,
and 6 below: `WN_Invoices`, `WN_InvoiceLineItems`,
`WN_GenerateAdvanceRentInvoices`, and any audit/sequence/status-code
tables should all carry the `WN_` prefix.

Open and read `F:\WN_APIs\Schema.sql`. Identify:
- The table(s) holding active bookings/contracts (e.g. Booking,
  WN_Locations) and which columns define the current paid period —
  specifically a `CurrentPeriodEnd` (or equivalent) date and a
  cycle-length field (1/2/3/6 months). If no such column exists yet, flag
  that one must be added — the trigger logic in Step 1 depends on it.
- A `ContractStart` / `ContractEnd` (or equivalent) at the booking level,
  distinct from the billing cycle. The billing cycle (1/2/3/6 months)
  auto-renews *within* the contract; it stops renewing once
  `ContractEnd` is reached. If this field doesn't exist yet, flag it —
  without it, the job has no way to know when to stop auto-invoicing.
- Whether a table already exists for storing generated invoices/invoice
  history. If not, note that one needs to be created (see Step 2).
- Rates and tax percentages are already stored as effective-dated
  history (confirmed) rather than flat current values — locate the
  actual table/columns in Schema.sql so Step 2's lookup logic references
  the real names.
- `WN_AccessCards` (confirmed): access restriction is handled by another
  system that reads this table — this job only needs to update its
  `Status` column via the existing `BookingId` foreign key. Still to
  confirm with that system's owner: the actual integer meaning of each
  `Status` code, and whether `EndDate` also needs adjusting (see Step 5).
- The customer/contact table with a valid billing email address column.

Flag any ambiguity (e.g. multiple "customer" tables, unclear which date
field marks an active lease) instead of guessing.

### Step 0a — Establish a single source of truth for status codes
**Resolved scope: full retrofit, documentation/reference approach (no
data migration).** Every existing table keeps storing its status value
exactly as it does today (same column, same `int`, same stored values) —
nothing about existing data or existing backend code changes. What's new
is a single reference table that documents what every one of those
values means, everywhere it's used.

Scan the entire schema in `F:\WN_APIs\Schema.sql` for every column that
stores a status-like value (commonly `Status`, `StatusId`, or similar,
typically `int`) across **all** `WN_` tables, not just the ones this
invoicing feature touches. For each one, determine its current distinct
values and what they mean — the DDL alone won't say this, so check the
backend codebase for hardcoded integer comparisons or enum definitions
tied to each column, since that's where the real meaning lives today.
Where meaning can't be determined with confidence, flag it rather than
guessing.

Create / reference `dbo.OrderStatus` view:
(Id, Description, Status), replacing legacy `WN_StatusLookUp`.

**Enforcement, since a true database-level FK isn't possible here**:
because this is a documentation table rather than a migrated FK
(Option A, not B), SQL Server cannot itself guarantee that a value
written into e.g. `WN_Invoices.Status` matches a row in
`WN_StatusLookUp`. Enforce correctness instead by:
- Having every stored procedure or backend code path that writes a
  status value validate it against `WN_StatusLookUp` before the write
  (a lookup, not a hardcoded literal), so new code is self-documenting
  and can't silently introduce an undocumented code.
- Optionally, a lightweight periodic audit query that flags any
  `DISTINCT` status value found live in a table but missing from
  `WN_StatusLookUp` for that `EntityName` — catching drift from code that
  wasn't updated to go through the lookup.
- For the columns this invoicing feature specifically introduces or
  touches (`WN_Invoices.Status`, `WN_AccessCards.Status`, any new
  `WN_InvoiceDeliveryQueue.Status`), all new code should read/write
  status values via `WN_StatusLookUp` lookups from day one, rather than
  hardcoded magic numbers — this feature should be the first thing built
  correctly against the new single source of truth, even if the rest of
  the codebase is retrofitted gradually.

### Step 1 — Trigger: SQL Server Agent Job on the 20th of each month
- Create a SQL Server Agent Job scheduled to run on the 20th of every
  month.
- The job calls a stored procedure, e.g. `WN_GenerateAdvanceRentInvoices`.
- **This is not "invoice every active booking every month."** Each
  booking has a cycle length (1/2/3/6 months) and a current paid period
  (`CurrentPeriodStart`, `CurrentPeriodEnd`). The job must only pick up
  bookings whose `CurrentPeriodEnd` falls within the current calendar
  month (i.e. the paid period is about to run out and the next cycle
  starts on the 1st of next month). A booking mid-cycle (e.g. month 2 of
  a 3-month period) is skipped — it has no invoice due yet.
- Include a re-run guard: check whether an invoice already exists for the
  computed next period (by BookingID + NextPeriodStart) before creating a
  duplicate, so re-running the job on the 21st doesn't double-invoice.
- **Contract boundary check**: if `NextPeriodEnd` would exceed
  `ContractEnd`, the booking has reached the end of its contract. Two
  sub-cases:
  - If `ContractEnd` falls exactly on a cycle boundary, this is simply
    the last invoice — generate it normally and do not schedule another.
  - If `ContractEnd` falls mid-cycle (e.g. a 3-month cycle would run
    past the contract's end), generate a **prorated final invoice**
    covering only `CurrentPeriodEnd + 1` through `ContractEnd`, billed at
    a proportional amount (see Step 2, point 3a). Do not silently bill
    for months beyond the contract.
  - Either way, flag the booking (e.g. `Status = 'ContractEndingSoon'`)
    so sales/ops gets a signal to follow up on renewal — this system
    should not silently let a customer's access lapse without a human
    knowing a renewal decision is needed.

### Step 2 — Generation logic (stored procedure)
`WN_GenerateAdvanceRentInvoices` should, per qualifying booking:
1. Confirm `CurrentPeriodEnd` falls within the current month (see Step 1
   filter). If not, skip.
2. Compute the next cycle:
   - `NextPeriodStart` = 1st of next calendar month
   - `NextPeriodEnd` = `NextPeriodStart` + booking's CycleLengthMonths − 1
     (i.e. covers the *entire* 1/2/3/6-month cycle in one invoice, not
     just one month)
3. Look up the monthly rate and any charge percentages, and multiply by
   CycleLengthMonths to get the lump subtotal for the whole period.
   - **3a — Effective-dated rates (confirmed to already exist)**: look up
     the rate/charge percentage that is effective as of `NextPeriodStart`,
     not "today's" rate, using the existing effective-dated rate/tax
     table identified in Step 0 — don't build a new one, reference the
     real table/column names once Schema.sql has been read.
   - **3b — Proration**: if this is a final, contract-truncated cycle
     (see Step 1), calculate the subtotal using exact days:
     `(monthly rate × charge %) × (prorated period days / total days in
     the calendar span that period would have covered)`. Use actual
     calendar days for both numerator and denominator (don't assume
     30-day months) so the fraction is accurate regardless of which
     months the truncated period falls in.
4. Look up the province-based tax percentage (via Booking → WN_Locations,
   per Step 0) — also effective-dated as of `NextPeriodStart`, same
   reasoning as 3a — and apply it to the lump subtotal.
5. Set `DueDate` = last day of the current month (i.e. before the 1st of
   the approaching month — the invoice must be settled before the new
   cycle begins).
6. Generate `InvoiceNumber` from a dedicated sequence/identity mechanism
   with proper locking (e.g. a `WN_InvoiceNumberSequence` SEQUENCE object
   or a single-row counter table, named per convention) — not a
   value computed by reading `MAX(InvoiceNumber)+1`, which will collide
   or skip under concurrent execution once volume grows.
7. Insert into `WN_Invoices`: InvoiceNumber, CustomerID, BookingID,
   BillingPeriodStart = NextPeriodStart, BillingPeriodEnd = NextPeriodEnd,
   CycleLengthMonths, IsProrated (bit), Subtotal, TaxAmount, Total,
   DueDate, Status, GeneratedDate.
   - **One invoice per booking, always** — even if a customer has
     multiple bookings, do not consolidate. Each booking's invoice is
     generated, numbered, and sent independently.
8. Insert line item(s) into `WN_InvoiceLineItems` reflecting the full-period
   charge (e.g. "Advance Rent — {{NextPeriodStart}} to {{NextPeriodEnd}}
   ({{CycleLengthMonths}} month(s))").
9. Do **not** advance the booking's `CurrentPeriodEnd` at generation time
   — only update it once the invoice is confirmed paid (or on the actual
   cycle-start date), so a generated-but-unpaid invoice doesn't silently
   roll the booking forward.
10. Process each qualifying booking in its own transaction (commit per
    row or small batch) rather than one transaction for the whole run —
    if booking #400 of 800 fails, the other 799 should still succeed, and
    the failure should be logged individually for retry (see Step 6).

### Step 2a — Invoice text requirement
The invoice/PDF must explicitly state the payment deadline in plain
language, not just a due-date field — e.g. "This invoice must be paid
before {{NextPeriodStart}} to avoid service interruption," matching how
you described the requirement (rent is due before the 1st of the
approaching month covered).

### Step 3 — PDF rendering & delivery trigger (resolved: direct call with
queued fallback)
PDF generation already happens in the backend (not in SQL Server). The
handoff mechanism is: **call the backend directly the moment an invoice
is generated; if that call fails, queue it for retry** rather than
letting it silently disappear.

- **Mechanics of the direct call**: pure T-SQL has no clean native way to
  make an HTTP call. The Agent Job step that runs
  `WN_GenerateAdvanceRentInvoices` should be followed by a second job
  step (PowerShell or CmdExec type, not T-SQL) that reads the
  newly-generated invoice IDs and calls the backend's render/send
  endpoint for each — or, if you'd rather keep it inside SQL Server, a
  CLR stored procedure that makes the HTTP call. Don't attempt this via
  `sp_OACreate`; it's deprecated and unreliable for this purpose.
- **On success**: backend renders the PDF, sends the email, and reports
  back (or the calling step itself) sets `WN_Invoices.Status = 'Sent'`.
- **On failure** (timeout, non-2xx response, backend unreachable):
  insert a row into a new table, `WN_InvoiceDeliveryQueue`
  (InvoiceId, Attempts, LastAttemptAt, NextRetryAt, Status, LastError),
  instead of leaving the invoice stuck with no record of the failed
  attempt.
- **Retry job**: a separate SQL Agent Job (e.g. every 5 minutes) runs
  `WN_RetryQueuedInvoiceDelivery`, which selects rows from
  `WN_InvoiceDeliveryQueue` where `NextRetryAt <= GETDATE()` and
  `Status = 'Pending'`, retries the same backend call, and either marks
  it `Status = 'Sent'` and removes/archives the queue row, or increments
  `Attempts` and sets the next backoff interval: 5 min after attempt 1,
  15 min after attempt 2, 1 hr after attempt 3.
- **Max-retry escalation**: after **3 failed attempts** (backoff:
  5 min → 15 min → 1 hr between attempts), stop auto-retrying and set
  `Status = 'FailedNeedsAttention'`, and alert a human (email/log) — an
  invoice that's failed 3 times in a row needs a person to look at it,
  not an infinite silent retry loop.

Whichever call path is used, the backend's rendering step should reuse
the exact invoice layout/fields defined in the existing invoice template
(account block, line items, totals, payment info) rather than re-deriving
it.

### Step 4 — Email delivery
- Email sending happens in the backend as part of the same call that
  renders the PDF (direct call from Step 3, or the retry job re-attempting
  it) — not via SQL Server Database Mail — keeping rendering and sending
  atomic in one place and avoiding passing a PDF binary back into SQL
  Server.
- Attach the rendered PDF.
- Subject line pattern: e.g. "Invoice {{InvoiceNumber}} — Due {{DueDate}}".
- On successful send, update `WN_Invoices.Status = 'Sent'` and `SentDate`.
- On failure, this is exactly the case Step 3's queue exists for — do not
  silently mark as sent; let it fall into `WN_InvoiceDeliveryQueue` for
  retry.

### Step 5 — Overdue escalation (daily job, 1st–7th, then restriction)
This is a separate scheduled process from generation — it acts on
invoices that already exist and are unpaid.

- **Daily reminder job**: SQL Agent Job (or backend scheduled task) calls
  a stored procedure, e.g. `WN_SendOverdueInvoiceReminders`, every day
  from the 1st through the 7th of the month. It selects
  invoices where `DueDate < today` (i.e. due date was end of prior month)
  and `Status != 'Paid'`, marks them `Status = 'Overdue'` if not already,
  and triggers a reminder email each day (via the same backend
  render/send path as Step 3–4, or a lighter reminder-only email
  template — decide which; a full re-rendered PDF every day is probably
  unnecessary, a reminder email referencing the existing invoice/PDF is
  likely sufficient).
- **Restriction job**: a stored procedure, e.g.
  `WN_RestrictOverdueAccess`, runs on the 8th. Selects invoices still
  `Status = 'Overdue'` as of that date, and for each, updates the
  corresponding row(s) in `WN_AccessCards` (joined via `BookingId`) by
  setting:
  - `Status` = the `WN_StatusLookUp`-defined code for "restricted" (see
    Step 0a), and
  - `EndDate` = today's date, pulling access validity back immediately.
  - `UpdatedOn = GETDATE()` and `UpdatedById` (a system/service account).
  - **Do not just overwrite `EndDate` and lose the original value** —
    the original `EndDate` represented the intended full period (tied to
    the booking's paid cycle) and is needed to restore access correctly
    once payment comes in. Rather than storing a duplicate "original"
    column, recompute the correct restore value at reinstatement time
    from `WN_Invoices.BillingPeriodEnd` for that booking's current paid
    cycle — the invoice record already has the authoritative end date, so
    `WN_AccessCards.EndDate` doesn't need its own backup copy as long as
    the reinstatement logic always looks it up from there rather than
    guessing.
  - Access enforcement itself is owned by another system that reads this
    table — this job's responsibility is only to set these fields
    correctly.
  - Note `WN_AccessCards.Status` currently has **no defined codes at
    all** — see the proposed values (Active / Restricted / Inactive) at
    the bottom of this document. Register the agreed-upon values in
    `WN_StatusLookUp` (Step 0a) before this stored procedure is written,
    since it needs a real integer to write, not a placeholder.
- **Reinstatement on payment**: when an invoice's `Status` becomes
  `'Paid'`, the same/adjacent process must update `WN_AccessCards` for
  that `BookingId` back to the active `WN_StatusLookUp` code, and set
  `EndDate = WN_Invoices.BillingPeriodEnd` for that booking's currently
  paid invoice — restoring the correct validity window rather than an
  arbitrary date.
- **Reminders stop / restriction is skipped automatically** once
  `Status` is set to `'Paid'` by whatever payment-recording process you
  use — both the daily reminder job and the 8th-of-month restriction job
  should filter on current status at run time, not on a decision made
  when the invoice was generated, so a payment made on the 6th correctly
  stops everything downstream.
- **Late fee: confirmed none.** No late fee is applied for overdue
  invoices — the escalation path is reminder emails then access
  restriction only, as described above.

### Step 6 — Error handling & auditability
- Log every run of `WN_GenerateAdvanceRentInvoices` (rows processed, rows
  skipped as duplicates, rows failed) to an audit table (e.g.
  `WN_InvoiceGenerationLog`).
- Ensure the job doesn't fail silently — Agent Job should alert (email or
  log) on step failure.
- Provide a way to manually re-trigger generation/sending for a single
  customer/period (for corrections), separate from the bulk monthly run.

### Step 7 — Testing
- Test against a non-production database first.
- Verify: no duplicate invoices on re-run, correct tax calculation per
  province, correct proration math on a contract-truncated final cycle,
  correct PDF rendering, correct recipient resolution, that a failed send
  doesn't mark the invoice as sent, that reminders stop the moment a
  payment is recorded, and that restriction only fires for invoices still
  unpaid on the 8th.

---

## Open decisions to resolve before implementation
1. **`WN_AccessCards` status codes — proposed, pending sign-off** (Step
   5): no codes are currently defined for this column, so rather than
   "confirm existing values," this is a "define and get agreement"
   decision. Proposed values to register in `WN_StatusLookUp`
   (EntityName = 'WN_AccessCards'):
   - `1` = Active
   - `2` = Restricted (payment overdue)
   - `3` = Inactive / Not Yet Issued
   These need sign-off from whoever owns the access-control system that
   actually reads this table, since they're the one enforcing behavior
   based on whatever value is stored — this system can set the flag, but
   the other system has to agree on what each number means before it
   will do anything meaningful with it. If they already have an informal
   convention in mind (even undocumented), use theirs instead of this
   proposal.

## Resolved decisions (locked in, for reference)
- Retry policy: 3 attempts, backoff 5min / 15min / 1hr, then flag
  `FailedNeedsAttention` for human review (Step 3).
- No late fee on overdue invoices (Step 5).
- Rate/tax percentages are already stored as effective-dated history in
  the schema — Step 2, 3a's rate-history lookup logic applies directly
  against the existing table (name/columns to be confirmed against
  Schema.sql, but the design pattern is confirmed correct, not a new
  table to build).
- Retrofit approach for `dbo.OrderStatus` view is documentation/reference only
  — no data migration, no true FK (Step 0a).
- PDF/email delivery: direct call from the Agent Job to the backend, with
  automatic fallback to a retry queue on failure (Step 3).
- Proration on contract-truncated final cycles: exact calendar days
  (Step 2, 3b).
- Access restriction integrates with the existing `WN_AccessCards` table;
  enforcement is owned by another system (Step 5).
- One invoice per booking, always — no consolidation across a customer's
  multiple bookings (Step 2).
- Renewal auto-continues within the booking's overall contract period
  until `ContractEnd` (Step 1).

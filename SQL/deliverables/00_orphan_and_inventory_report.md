# Stage 0 — Inventory & Assumption Confirmation Report

---

## 1. WN_Memberships Table Reference Audit

### Tables in schema.sql

| Table | Status |
|---|---|
| `dbo.WN_MemberShips_Temp` | **EXISTS** — the real table. Columns: Id, UserId, PlanId, StartDate, EndDate, Status, CreatedAt |
| `dbo.WN_Memberships` | **DOES NOT EXIST** — no such table anywhere in schema.sql |

### Stored Procedures referencing `dbo.WN_Memberships` (wrong table — broken)

| Procedure | Wrong reference | Runtime effect |
|---|---|---|
| `WN_Memberships_GetByPlanId` | `FROM dbo.WN_Memberships m` | Fails with "Invalid object name 'dbo.WN_Memberships'" on every call |
| `WN_Memberships_GetList` | `FROM dbo.WN_Memberships m` | Same — fails on every call |

### Secondary bug in WN_Memberships_GetList
Beyond the wrong table name, `WN_Memberships_GetList` references `m.IdGUID` and `m.UserGuid` — neither column exists on `WN_MemberShips_Temp` (which has `UserId INT`, not `UserGuid UNIQUEIDENTIFIER`, and no `IdGUID`). The fix in Stage 1 corrects both issues together.

### Application call sites (DbRepository.cs)
- `WN_Memberships_GetByPlanId` → called in `GetMembershipsByPlanIdAsync()`. No exception swallowing. `GET /api/pricingplan/{id}/summary` **fails in production with HTTP 500**.
- `WN_Memberships_GetList` → called in `GetAllMembershipsAsync()`. No exception swallowing. `GET /api/membership` **fails in production with HTTP 500**.

**Verdict:** Both procs are confirmed broken (not working). Safe to fix under the hard constraint — we are fixing a broken contract, not changing a working one.

### Missing procs (called from DbRepository.cs but absent from schema.sql)
`WN_Memberships_Insert`, `WN_Memberships_UpdateStatus`, `WN_Memberships_Delete` are called from DbRepository but not present in schema.sql. Flagged for awareness; not in scope for this task.

---

## 2. Non-Sargable CAST-based GUID Join Performance Issues

> **NOT fixed in this task — listed for awareness only per Stage 0 instructions.**

| Procedure | Non-sargable expression | Tables |
|---|---|---|
| `WN_Payments_GetMyList` | `CAST(b.IdGUID AS NVARCHAR(36)) = CAST(p.BookingId AS NVARCHAR(36))` | WN_Bookings ↔ WN_Payments |
| `WN_Payments_GetMyList` | `CAST(s.IdGUID AS NVARCHAR(36)) = CAST(b.SpaceGuid AS NVARCHAR(36))` | WN_Spaces ↔ WN_Bookings |
| `WN_Challan_Search` | `CAST(p.BookingId AS NVARCHAR(36)) = CAST(b.IdGUID AS NVARCHAR(36))` | WN_Payments ↔ WN_Bookings |
| `WN_Challan_ExtendValidity` | `CAST(BookingId AS NVARCHAR(36)) IN (SELECT CAST(IdGUID AS NVARCHAR(36)) ...)` | WN_Payments ↔ WN_Bookings |
| `WN_BookingDetails_GetByBooking` | `CAST(bd.BookingGuid AS NVARCHAR(36)) = @BookingGuid` | WN_BookingDetails (param is NVARCHAR, column is UNIQUEIDENTIFIER) |
| `WN_BookingDetails_GetList` | `CAST(bd.BookingGuid AS NVARCHAR(36)) LIKE '%' + @Search + '%'` | WN_BookingDetails |
| `WN_BookingDetails_GetTotalByBooking` | `CAST(BookingGuid AS NVARCHAR(36)) = @BookingGuid` | WN_BookingDetails |

Root cause: `WN_Payments.BookingId` and `WN_Payments.UserId` are `UNIQUEIDENTIFIER` but some join paths cast both sides to NVARCHAR, preventing index seeks. Fixing requires a separate task.

---

## 3. Company A / Company B Mapping — CONFIRMATION REQUIRED BEFORE BACKFILL

`WN_Locations` has `CityId INT` and `BranchId INT`. `WN_Users` has `CompanyId INT` defaulting to `484`. Branch/company data lives in `SAC400.dbo.Branches` and `SAC400.dbo.Company_GetCompanyList` — outside the WN_ tables.

**I cannot write the backfill UPDATE statements in Stage 3 step 6 until you confirm:**

> Which `CityId` values (from `dbo.WN_Cities`) or `BranchId` values (from `SAC400.dbo.Branches`) map to Company A vs Company B?
>
> Please provide a mapping such as:
> - `BranchId IN (X, Y) → CompanyId = 484` (Company A) and `BranchId IN (Z) → CompanyId = 485` (Company B)
> - Or the actual integer CompanyId values from `SAC400.dbo.Company_GetCompanyList`

The backfill UPDATE in `03_multicompany_schema.sql` contains a clearly marked `/* AWAITING CONFIRMATION — DO NOT RUN */` placeholder. All other DDL in that file (ALTER TABLE, trigger, index) is safe to run independently.

---

## 4. Additional Pre-existing Bugs (out of scope, flagged for awareness)

| Issue | Detail |
|---|---|
| `WN_Bookings_Cancel` parameter mismatch | SP takes `@BookingId INT, @UserId INT` but DbRepository calls it with `@BookingGuid NVARCHAR` and `@UserGuid NVARCHAR` |
| `WN_Spaces_Delete` parameter mismatch | SP takes `@SpaceId INT` but DbRepository calls it with `@IdGUID` |
| `WN_BookingDetails_Insert` (old flat SP) | References columns (`CustomerCode`, `RentAmount`, etc.) that no longer exist on `WN_BookingDetails` — would fail at runtime |

# Stage 5 — Application Layer Investigation Report

> **Scope:** Read-only analysis of `WorkNest.Infrastructure/Repositories/DbRepository.cs`.
> No source files were modified. All findings are advisory only.

---

## 1. Parameter Binding Style (write-path procs touched in Stage 4)

All calls in `DbRepository.cs` bind parameters using `cmd.Parameters.AddWithValue("@ParamName", value)` — named binding throughout. There is no positional `EXEC proc val1, val2` pattern anywhere in the file.

**Verdict: Safe.** Appending new parameters with defaults to the end of any proc's parameter list will not affect any existing caller.

| Proc | C# call site | Binding style |
|---|---|---|
| `WN_Booking_Create` | `CreateSmartBookingAsync` | Named (`AddWithValue`) |
| `WN_Bookings_Insert` | `CreateBookingAsync` | Named (`AddWithValue`) |
| `WN_CreateBookingWithAutoAssignment` | `CreateBookingWithAutoAssignmentAsync` | Named (`AddWithValue`) + named OUTPUT params |
| `WN_Booking_GetAvailableSpaces` | `GetAvailableSpacesV2Async` | Named (`AddWithValue`) |
| `WN_Spaces_GetList` | `GetAllSpacesAsync` | No params (SP takes none today) |
| `WN_Spaces_GetVacant` | `GetVacantSpacesAsync` | No params (SP takes none today) |
| `WN_Locations_GetList` | `GetAllLocationsAsync` | No params (SP takes none today) |
| `WN_Bookings_GetList` | `GetAllBookingsAsync` | No params (SP takes none today) |
| `WN_BookingDetails_GetList` | _(not called from DbRepository — no call site found)_ | N/A |

### Note on `WN_CreateBookingWithAutoAssignment` OUTPUT parameters

```csharp
var bookingIdParam    = new SqlParameter("@BookingId",        SqlDbType.Int) { Direction = ParameterDirection.Output };
var spaceIdParam      = new SqlParameter("@AssignedSpaceId",  SqlDbType.Int) { Direction = ParameterDirection.Output };
cmd.Parameters.Add(bookingIdParam);
cmd.Parameters.Add(spaceIdParam);
```

Both OUTPUT params are bound **by name**, not by ordinal position. Adding `CompanyId` as an internal local variable inside the proc (not as a new OUTPUT param) is fully safe — the caller never references it.

---

## 2. Result-Set Column Reading Style

`DbRepository` uses a single helper for all result-set reads:

```csharp
private static IDictionary<string, object?> RowToDictionary(SqlDataReader r)
{
    var d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < r.FieldCount; i++)
        d[r.GetName(i)] = Normalize(r.GetValue(i));
    return d;
}
```

This builds a **case-insensitive dictionary keyed by column name**. No ordinal reads (`reader[3]`) exist anywhere. No `SELECT *`-style fixed-column-count assumptions exist.

**Verdict: Safe.** Appending new columns (e.g. `companyId`) to the end of any result set will not break any existing caller — the new key simply appears in the dictionary and is ignored unless explicitly accessed.

---

## 3. Write-Path Security Check — CompanyId from HTTP Request Body

Searched all call sites of write-path procs for any `companyId` / `CompanyId` field being read from an HTTP request body or query string and forwarded to a stored procedure.

**Finding: No such pattern exists.** None of the following pass a client-supplied `CompanyId` to any SP:

- `CreateSmartBookingAsync` — parameters: `userEmail, spaceCategory, start, end, notes, amount, paymentMethod, paymentRef, capacity`
- `CreateBookingAsync` — parameters: `userGuid, spaceGuid, start, end, notes, amount, paymentMethod, paymentRef, customerCode`
- `CreateBookingWithAutoAssignmentAsync` — parameters: `userEmail, spaceType, start, end, notes, amount, paymentMethod, paymentRef`

**Verdict: Clean.** The write path is safe. `CompanyId` is not accepted from any client on any booking creation path. The Stage 4 design (derive server-side from `WN_Spaces.CompanyId`) is consistent with the current codebase.

---

## 4. WN_BookingDetails_GetList — No Call Site Found

`WN_BookingDetails_GetList` is listed in Stage 4 as a read-path proc to update. Searching `DbRepository.cs` reveals **no call site** for this proc. It is not called from the repository layer.

**Action required (future):** When a call site is added, the caller will automatically benefit from the `@CompanyId` filter added in Stage 4 if it passes the parameter. If it calls without `@CompanyId`, behaviour is unchanged (filter is `NULL`-guarded).

---

## 5. Existing CompanyId on WN_Users — Future Read-Scoping Anchor

`WN_Users` already has a `CompanyId INT NULL` column with a default of `484`. This value is:

- Populated at insert time in `WN_Users_Insert` (hardcoded to `484`)
- Returned by `WN_Users_GetByGuid`, `WN_Users_GetById`, and `WN_Users_GetList`
- Available in the JWT-authenticated user context via the existing `GET /api/auth/me` endpoint

**Implication for a future task:** A separate task could read `CompanyId` from the authenticated user's token/profile and pass it as `@CompanyId` to the read-path procs updated in Stage 4 (`WN_Spaces_GetList`, `WN_Bookings_GetList`, etc.) to automatically scope list endpoints by company. This requires no schema changes — only a service/controller change to extract `CompanyId` from the auth context and forward it to the repository call.

**This is not implemented here** — it touches live endpoint code and is out of scope for this task.

---

## 6. Additional Observations (no action required)

| Observation | Detail |
|---|---|
| `WN_Bookings_Cancel` parameter mismatch | DbRepository calls with `@BookingGuid` (NVARCHAR) and `@UserGuid` (NVARCHAR) but the SP signature takes `@BookingId INT` and `@UserId INT`. This call will fail at runtime. Flagged in Stage 0 — out of scope here. |
| `WN_Spaces_Delete` parameter mismatch | DbRepository calls with `@IdGUID` but SP takes `@SpaceId INT`. Will fail at runtime. Flagged in Stage 0 — out of scope here. |
| `InsertDepositPaymentAsync` uses raw SQL | One method constructs an inline `INSERT` SQL string rather than calling a stored proc. This is not affected by any Stage 4 changes. |

---

**No source files were modified in this stage.**

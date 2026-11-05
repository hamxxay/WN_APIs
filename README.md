# WorkNest API

ASP.NET Core Web API (.NET 10) for [WorkNest](https://worknestpk.com), coworking spaces and private offices in Islamabad. A single API with one SQL Server database serves:

| Client | Repo | Uses the API for |
|---|---|---|
| Staff CMS and customer web portal | `WN_CMS` | Everything below |
| Mobile app (React Native) | `WN_MobileApp` | Sign-in, customer features, announcements and push tokens, door unlock |
| Public website | `WorkNest-MainWeb` | `GET /api/gallery`, `GET /api/location/all`, `POST /api/book-tour` |

Production base URL: `https://aeo.eaccounting360.com.pk/WorkNest/api`. Swagger UI is served at `/swagger`.

## Main areas

| Area | Controllers | Routes (prefix) |
|---|---|---|
| Auth and users | `AuthController`, `AuthHandoffController`, `UserController` | `api/auth/*` (sync, register, login, google-login, me, logout, change-password), `api/user`. Customer logins created by staff start with a temporary password and must change it at first sign-in (`mustChangePassword`) |
| Customers | `CustomerController` | `api/customer` |
| Spaces and catalog | `LocationController`, `BranchController`, `FloorController`, `SpaceController`, `SpaceTypeController`, `SpaceConfigController`, `AmenityController`, `PricingPlanController`, `PlanFeatureController`, `MembershipController`, `GalleryController` | `api/location`, `api/branch`, `api/city`, `api/company`, `api/floor`, `api/space`, `api/spacetype`, `api/space-config`, `api/amenity`, `api/pricingplan`, `api/planfeature`, `api/membership`, `api/gallery` |
| Bookings | `BookingController` | `api/booking` (calendar, availability, smart, admin create, my) |
| Quotations | `QuotationController` | `api/quotation`, `api/quotations`, `api/offering-types` |
| Agreements and e-sign | `AgreementController`, `LeaseTemplatesController`, `LeasesController` | `api/agreement` (send, sign, e-signature, signed PDF upload), `api/lease-templates`, `api/leases` |
| Invoices, payments, challans | `InvoiceController`, `PaymentController`, `ChallanController`, `AmountFieldController`, `AccountCoaController`, `SecurityDepositReportController` | `api/invoice` (incl. advance, sales tax, PDFs), `api/payment` (PayFast, card, voucher), `api/challan`, `api/amount-fields` (incl. WHT rates), `api/account-coa`, `api/securitydepositreport` |
| KYC | `KycController` | `api/kyc` (upload, verify, reject, documents, history) |
| Contacts, tour inquiries, complaints | `ContactController`, `ComplaintController` | `api/contact`, `api/book-tour`, `api/complaints` |
| Attendants and Hikvision access control | `AttendantController`, `AccessCardController`, `HikDeviceController`, `HikStaffController`, `HikAccessController`, `HikSyncController` | `api/attendants`, `api/access-status`, `api/access-suspensions`, `api/capacity-requests`, `api/access-card`, `api/hik/*`, `api/devices` |
| Mobile door unlock | `MobileAccessController` | `api/mobile/access` (verify, confirm, doors, open) |
| Announcements | `AdminAnnouncementController`, `MobileAnnouncementController` | `api/admin/announcements`, `api/mobile/announcements`, `api/mobile/device-token` |
| UniFi network | `UnifiController` | `api/unifi/*` (summary, clients, devices, Wi-Fi, WAN, ISP, usage, logs) |
| Grandstream network (F-7) | `GwnController` | `api/gwn/*` (networks, overview, clients, devices, Wi-Fi, statistics; block / unblock and reboot for super admins, logged in `WN_GWN_ActionLog`). Contract: `Database/Gwn/GWN_CONTRACT.md` |
| Attendance | `AttendanceController`, `AdminAttendanceController` | `api/attendance/my/*` (customer: status, report, employees, shifts, holidays), `api/admin/attendance/*` and `api/admin/customers/{id}/attendance/*` (staff) |
| Room report | `RoomReportController` | `api/admin/room-report` (admin and super admin) |
| Network alerts and machines | `NetworkAlertsController`, `NetworkAccessMachinesController` | `api/admin/network-alerts` (super admin) |
| Background service switches | `ServiceSwitchController` | `api/admin/services` (list and switch background services on or off; needs `SS_01_service_switches`) |
| Dashboards and reports | `DashboardController`, `ReportsController` | `api/dashboard/*` (overview, summary, role, forecast), `api/admin/nav-badges`, `api/reports/weekly` |
| WhatsApp | `WhatsAppWebhookController`, `WhatsAppInboxController` | `api/whatsapp/webhook` (Meta Cloud API), `api/whatsapp/conversations`, `contacts`, `broadcast` |

## Architecture

Clean Architecture, five projects plus tests:

```
WorkNest.API             Controllers, filters, middleware, background services, Program.cs
WorkNest.Application     Services, interfaces, DTOs, FluentValidation validators
WorkNest.Infrastructure  Repositories (SQL), JWT, encryption, email, PDF, PayFast, Hikvision, UniFi, WhatsApp
WorkNest.Domain          Entities, enums
WorkNest.Common          ApiResponse, constants (roles), configuration, helpers
WorkNest.Tests           xUnit tests (references WorkNest.Application)
```

Patterns to follow when adding endpoints:

- **Data access**: plain ADO.NET (`Microsoft.Data.SqlClient`, `SqlCommand`) calling stored procedures, mainly through `DbRepository` (`IDbRepository`). No Entity Framework, no migrations.
- **Response envelope**: every endpoint returns `ApiResponse<T>` serialized in camelCase: `{ isSuccessful, data, message }` (plus `errors` on failure). `FriendlyErrorResultFilter` replaces raw SQL/.NET error text with a friendly message; `ExceptionMiddleware` handles unhandled exceptions.
- **Roles**: `[Authorize(Roles = ...)]`. Role names are in `WorkNest.Common/Constants/Roles.cs`: `super_admin`, `admin`, `sales_executive`, `sales_manager`, `ceo` (read-only dashboard), `access_officer` (door access only), `network_engineer` (UniFi and GWN). Customers are `general`. Only a super admin assigns `ceo`, `sales_manager`, `access_officer` and `network_engineer`. KYC uses the `KycAccessPolicy` and `KycVerifyPolicy` policies.
- **Location scoping**: `admin` and `sales_executive` are limited to their assigned location(s); `super_admin` sees everything. Enforced by `ValidateLocationScope` (request payload and query), `RecordScope` (record ids in the route, other locations answer 404) and `UnifiLocationScopeFilter`.
- **Rate limiting**: built-in ASP.NET Core rate limiter with `auth`, `public-forms` and `pdf` policies (`RateLimiting` section).
- **Dates**: business dates are Pakistan time (`IBusinessClock`, `Business:TimeZone`, default `Asia/Karachi`). Wall-clock values are written without an offset; only true UTC values get a `Z`.
- **PDFs**: QuestPDF and PdfSharpCore, plus HTML-to-PDF through PuppeteerSharp (headless Chromium, downloaded on first use and pre-warmed at startup).
- **Logging**: Serilog, configured from the `Serilog` section (console and rolling files under `Logs/`).

### Background services

Registered in `Program.cs` (`WorkNest.API/Services`). Several can also be switched on or off at runtime through `api/admin/services` (table `WN_ServiceSwitches`):

| Service | What it does |
|---|---|
| `BillingAutomationService` | Generates recurring invoices 5 days before each billing cycle ends |
| `InvoiceDeliveryRetryService` | Retries failed invoice emails (backoff, max 3 attempts) |
| `AccessCardRestrictionService` | Daily: re-restricts access cards when a partial payment period lapses unpaid |
| `AnnouncementDeliveryService` | Sends announcements by push and email, with retries |
| `ChallanAccessSuspensionService` | Hourly: suspends door access on Hikvision devices for overdue unpaid challans (`HikAccessSuspension:Enabled`) |
| `HikSyncBackgroundService` | Hikvision device and database sync (`HikSync:Enabled`; enable on one server only, never on a dev machine pointed at the live database) |
| `BookingAutoConfirmService` | Every 5 minutes: confirms Pending bookings whose first invoice is paid (`BookingAutoConfirm:Enabled`) |
| `AgreementReminderService` | Hourly: emails reminders for agreements awaiting signature (`AgreementReminders`) |
| `WeeklyReportService` | Emails the weekly report to super admins (`Reports:Weekly`, default Monday 09:00 Pakistan time) |
| `EmailOutboxService` | Sends pending emails once Email Sending is switched on |
| `WhatsAppFollowUpService` | WhatsApp follow-up messages (`WhatsAppFollowUp` switch, `WhatsApp:FollowUp*` settings) |
| `NetworkAlertService` | Raises and reminds on network-down alerts (`NetworkAlerts` switch; `DownMinutes`, `MachineDownMinutes`, `ReminderHours`, `TickSeconds`) |
| `OneBillSyncService` | One Bill (EzPay / 1-Link) sync (`OneBill:Enabled`, off by default) |
| `MonthlyFeeReceiptService` | Monthly fee receipts (`FeeReceipts:MonthlyEnabled`, on by default) |
| `UnifiPollingService` | Keeps UniFi dashboard data fresh (`Unifi:Enabled`, `Unifi:RefreshSeconds`) |
| `GwnPollingService` | Keeps the Grandstream (GWN) figures fresh (`GwnPolling` switch, `Gwn:Enabled`, `Gwn:RefreshSeconds`) |

## Authentication

1. The client signs in with Firebase Authentication (email/password or Google) and sends the Firebase ID token to `api/auth/login`, `api/auth/google-login`, `api/auth/register` or `api/auth/sync`.
2. The API verifies the Firebase token (`FirebaseTokenVerifier`, `Firebase:ProjectId`) and takes the email from it. With `Firebase:RequireIdToken = true`, requests without a valid token are refused.
3. The API issues its own JWT (`JwtSettings`). Clients send it as `Authorization: Bearer <token>` (a `token` or `access_token` query parameter is also accepted).

Sign-in can never set a role or company; roles are assigned by staff.

## Configuration

Settings live in `WorkNest.API/appsettings.json` and `appsettings.Development.json`. Both are gitignored and hold credentials, so get them from the team (see `SECRETS.md`). Never commit them.

| Section | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection |
| `Cors:AllowedOrigins` | Allowed browser origins (production only; Development allows any origin) |
| `JwtSettings` | API token signing, issuer, audience, expiry |
| `Firebase` | `ProjectId`, `RequireIdToken` |
| `Encryption` | Field encryption key and IV |
| `Email` | SMTP sender and recipients |
| `PayFast` | Payment gateway |
| `FileStorage`, `KycStorage` | Upload folders for signed agreements and KYC documents |
| `Unifi` | UniFi network dashboard |
| `OneBill`, `FeeReceipts`, `NetworkAlerts` | One Bill integration, monthly fee receipts, network alert timings |
| `Gwn` | Grandstream GWN Cloud: `Enabled`, `Domain`, `AppId`, `SecretKey`, `Username`, `Password`, `RefreshSeconds`, `DefaultNetworkLocations` (local appsettings only; without them every `api/gwn/*` answers `available: false`) |
| `HikSync`, `HikAccessSuspension`, `Hikvision` | Hikvision sync, access suspension, API key for the HIK backend |
| `WhatsApp` | WhatsApp Cloud API bot and inbox |
| `RateLimiting` | Rate limit policies |
| `AgreementReminders`, `BookingAutoConfirm`, `Reports` | Background job switches and schedules |
| `Application`, `Business`, `PathBase` | Public base URL, business time zone, optional path base |
| `Serilog` | Logging |

The real values are not in git. `SECRETS.md` lists the sections with placeholders (check it against this table; the SQL connection is `ConnectionStrings:DefaultConnection`).

> CORS origins are read once at startup. After changing `Cors:AllowedOrigins` on the server, recycle the IIS app pool.

## Run locally

Requires the .NET 10 SDK and access to the SQL Server database.

```bash
dotnet run --project WorkNest.API --launch-profile WorkNest.API
```

This runs in Development on http://localhost:5200 (Swagger at http://localhost:5200/swagger). Front-end dev setups (for example the public site's `proxy.conf.json`) point at this port.

> If the local connection string points at the live database, everything you do locally (bookings, invoices, emails) is real. Keep `HikSync:Enabled` off locally.

There is no solution file; build or run a project directly (`dotnet build WorkNest.API`).

**Build breaks with a strange folder name.** On macOS/Linux a Windows upload path from config can create an empty folder named like `D:\WorkNest_Uploads...` inside `WorkNest.API`, which breaks the build. Remove it:

```bash
find WorkNest.API -maxdepth 1 -name 'D:*' -type d -empty -exec rmdir {} \;
```

## Database

- SQL Server database `SAC400`. Tables use the `WN_` prefix (`WN_HIK_*` for Hikvision). The code only calls stored procedures; no inline schema.
- The app does not create or alter tables at startup. A feature whose script has not been run usually answers 503 "Database update needed" instead of failing.
- Changes are numbered scripts under `Database/` (`.txt`, grouped by feature: `Agreements`, `Attendance`, `Auth`, `Booking`, `Gwn`, `HikSync`, `Invoicing`, `Network`, `Reports`, `Security`, `Unifi`, `Wht` and others; `Database/Live/` holds scripts and checks written for the live database). Name pattern: `<AREA>_<NN>_<description>.txt`.
- `Database/*` is in `.gitignore`, so new scripts are added with `git add -f`. Files already tracked update normally.
- Scripts are written to be re-runnable (`CREATE OR ALTER`, one transaction, non-destructive). Run each script on the test database first, check it, then on live. Read the header comment for run order and what it touches.
- Recent examples: `HikSync/HIK_EVT_01_event_retention.txt` (purges old door events so `WN_HIK_Events` keeps the previous and current month), `Attendance/ATT_02_attendance_people.txt` (run after `ATT_01`; attendance people function and procedures), `Auth/AUTH_TEMP_01_customer_temp_passwords.txt` (temporary-password columns on `WN_Users` and their procedures; also run on both databases).
- `Sp.sql` is a local reference dump of stored procedures (gitignored). Compare with the live procedure before replacing one.

## Deploy (IIS on Windows)

```bash
dotnet publish WorkNest.API -c Release -o publish
```

1. Copy the `publish` folder to the IIS site folder (the API runs as the `/WorkNest` application). The server needs the ASP.NET Core 10 Hosting Bundle.
2. Keep the server's own `appsettings.json`; do not overwrite it with a local copy.
3. `web.config` (in-process hosting, `AspNetCoreModuleV2`) is included in the publish output.
4. The server needs outbound internet access the first time a PDF is rendered (Chromium download) and for Firebase, WhatsApp, email and PayFast.
5. Recycle the app pool, then check `https://aeo.eaccounting360.com.pk/WorkNest/swagger`.

## Tests

```bash
dotnet test WorkNest.Tests
```

xUnit tests covering services and business rules in `WorkNest.Application` (billing periods, withholding tax, attendance, room report, temporary passwords, network alerts, location scope, Hikvision events and more). `WorkNest.Tests/` is listed in `.gitignore` and not tracked, so it exists only in local checkouts.

## Project structure

```
WorkNest.API/
  Controllers/      one controller per area (see Main areas)
  Services/         background (hosted) services
  Filters/          location scope, record scope, friendly errors
  Middleware/       exception handling, request logging
  Configurations/   CORS, rate limiting, Swagger
  Security/         Firebase token verification
  Program.cs        DI registration and pipeline
WorkNest.Application/   Services/, Interfaces/, DTOs/, Validators/, Helpers/
WorkNest.Infrastructure/ Repositories/, ExternalServices/, Security/
WorkNest.Domain/        Entities/, Enums/
WorkNest.Common/        Responses/, Constants/, Configurations/, Helpers/
WorkNest.Tests/         xUnit tests
Database/               DDL and stored procedure scripts (.txt), run manually
Forms/, Tasks/          reference documents (agreement and KYC forms, invoice prompts)
```

## Related repos

- `WN_CMS`: staff and customer web portal
- `WN_MobileApp`: React Native mobile app
- `WorkNest-MainWeb`: public website

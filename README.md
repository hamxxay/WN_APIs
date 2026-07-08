# WorkNest API

WorkNest is a **Coworking Space Management REST API** built with **ASP.NET Core 8**. It handles space bookings, user management, payments, memberships, and more for coworking facilities.

---

## Table of Contents

- [Architecture](#architecture)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Authentication](#authentication)
- [API Reference](#api-reference)
  - [Auth](#auth)
  - [User](#user)
  - [Space](#space)
  - [Booking](#booking)
  - [Payment](#payment)
  - [Membership](#membership)
  - [Pricing Plan](#pricing-plan)
  - [Plan Feature](#plan-feature)
  - [Location](#location)
  - [Branch / Company / City](#branch--company--city)
  - [Floor](#floor)
  - [Space Type](#space-type)
  - [Space Config](#space-config)
  - [Amenity](#amenity)
  - [Gallery](#gallery)
  - [Contact / Book Tour](#contact--book-tour)
  - [Dashboard](#dashboard)
- [Response Format](#response-format)
- [Pagination](#pagination)
- [Error Handling](#error-handling)
- [Logging](#logging)
- [CORS](#cors)

---

## Architecture

The solution follows **Clean Architecture** with 5 projects:

```
WorkNest.API             → Presentation layer (Controllers, Middleware, Config)
WorkNest.Application     → Business logic (Services, Interfaces, DTOs, Validators)
WorkNest.Infrastructure  → Data access (DB Repository, JWT, Encryption, Email, PayFast)
WorkNest.Domain          → Core domain (Enums, Constants)
WorkNest.Common          → Shared utilities (Responses, Helpers, Constants)
```

**Dependency flow:**
```
API → Application → Infrastructure
         ↓
       Domain
         ↓
       Common
```

---

## Tech Stack

| Component        | Technology                          |
|-----------------|-------------------------------------|
| Framework        | ASP.NET Core 8                      |
| Language         | C# 12                               |
| Database         | Microsoft SQL Server                |
| ORM / Data       | ADO.NET (raw stored procedures)     |
| Authentication   | JWT Bearer Tokens                   |
| Validation       | FluentValidation                    |
| Documentation    | Swagger / OpenAPI (Swashbuckle)     |
| Logging          | Serilog (Console + File sinks)      |
| Payments         | PayFast integration                 |
| Email            | Gmail SMTP (App Password)           |
| Encryption       | AES (custom EncryptionService)      |

---

## Project Structure

```
WN_APIs/
├── WorkNest.API/
│   ├── Controllers/         # 17 API controllers
│   ├── Configurations/      # Swagger & CORS setup
│   ├── Middleware/          # ExceptionMiddleware, RequestLoggingMiddleware
│   ├── Properties/          # launchSettings.json
│   ├── Program.cs           # App bootstrap & DI registration
│   └── appsettings.json     # App configuration
│
├── WorkNest.Application/
│   ├── DTOs/                # Request/Response models per feature
│   ├── Interfaces/          # Service contracts (IAuthService, IBookingService, etc.)
│   ├── Services/            # Business logic implementations
│   └── Validators/          # FluentValidation validators
│
├── WorkNest.Infrastructure/
│   ├── Database/            # DatabaseSettings, connection string builder
│   ├── Repositories/        # DbRepository (ADO.NET stored procedure calls)
│   ├── Security/            # JwtService, EncryptionService
│   └── ExternalServices/    # EmailService, PayFastService
│
├── WorkNest.Domain/
│   └── Enums/               # Shared enumerations
│
├── WorkNest.Common/
│   ├── Responses/           # ApiResponse<T>, PaginatedResponse<T>
│   ├── Helpers/             # PaginationHelper, DateHelper, GuidHelper
│   └── Constants/           # AppConstants, Roles
│
└── SQL/
    ├── SchemaDiscovery.sql
    └── MissingStoredProcedures.sql
```

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server instance (local or remote)
- Gmail account with App Password (for email)
- PayFast merchant account (for payments)

### Run the API

```bash
cd WN_APIs
dotnet run --project WorkNest.API/WorkNest.API.csproj
```

The API will be available at:
- HTTPS: `https://localhost:7200`
- HTTP:  `http://localhost:5200`
- Swagger UI: `https://localhost:7200/swagger`

### Health Check

```
GET /
```
Returns: `{ "app": "WorkNest ASP.NET Core API", "status": "healthy" }`

---

## Configuration

All settings live in `WorkNest.API/appsettings.json`. For local development, override values in `appsettings.Development.json`.

```json
{
  "DatabaseSettings": {
    "Server": "<sql-server-host>",
    "Port": 1433,
    "User": "<db-user>",
    "Password": "<db-password>",
    "Database": "<db-name>"
  },

  "JwtSettings": {
    "SecretKey": "<min-32-char-secret>",
    "Issuer": "WorkNestAPI",
    "Audience": "WorkNestClients",
    "ExpiryMinutes": 1440
  },

  "Encryption": {
    "Key": "<aes-key>",
    "IV": "<aes-iv>"
  },

  "Email": {
    "FromEmail": "<sender@gmail.com>",
    "ToEmail": "<admin@example.com>",
    "GmailAppPassword": "<gmail-app-password>"
  },

  "PayFast": {
    "MerchantId": "<merchant-id>",
    "SecuredKey": "<secured-key>",
    "Sandbox": true,
    "SandboxUrl": "https://sandbox.payfast.pk/v2/hosted_payment",
    "ReturnUrl": "<frontend-return-url>",
    "NotifyUrl": "<api-notify-url>"
  },

  "Cors": {
    "AllowedOrigins": [
      "http://localhost:4200",
      "http://localhost:5173"
    ]
  }
}
```

---

## Authentication

The API uses **JWT Bearer authentication**.

- Most endpoints require a valid JWT token in the `Authorization` header.
- Several endpoints also require the user's email in the `x-user-email` custom header.
- Some public endpoints are marked `[AllowAnonymous]` (see table below).

### Getting a Token

```
POST /api/auth/login
```

Use the returned token in subsequent requests:

```
Authorization: Bearer <your-jwt-token>
x-user-email: user@example.com
```

In Swagger UI, click **Authorize** and enter: `Bearer <your-token>`

---

## API Reference

> **Base URL:** `https://localhost:7200`
> 
> **Auth required:** All endpoints require `Authorization: Bearer <token>` unless marked 🔓 (public)

---

### Auth

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/sync` | 🔓 Public | Sync user from external provider (e.g. Firebase) |
| POST | `/api/auth/register` | 🔓 Public | Register a new user |
| POST | `/api/auth/login` | 🔓 Public | Login with email & password, returns JWT |
| POST | `/api/auth/google-login` | 🔓 Public | Login with Google OAuth token |
| GET | `/api/auth/me` | 🔒 JWT + `x-user-email` | Get current authenticated user profile |
| POST | `/api/auth/logout` | 🔒 JWT | Logout current user |

---

### User

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/user` | 🔒 JWT | List all users (paginated) |
| GET | `/api/user/{id}` | 🔒 JWT | Get user by ID |
| GET | `/api/user/{id}/history` | 🔒 JWT | Get user booking/activity history |
| POST | `/api/user` | 🔒 JWT | Create a new user |
| PUT | `/api/user/{id}` | 🔒 JWT | Update user details |
| DELETE | `/api/user/{id}` | 🔒 JWT | Delete a user |
| PATCH | `/api/user/{id}/activate` | 🔒 JWT | Activate a user account |
| PATCH | `/api/user/{id}/deactivate` | 🔒 JWT | Deactivate a user account |
| PATCH | `/api/user/{id}/role` | 🔒 JWT | Update user role |

**Query params for GET `/api/user`:** `page`, `limit`, `search`

---

### Space

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/space/available` | 🔓 Public | Get all currently available spaces |
| GET | `/api/space/available-by-type` | 🔓 Public | Get available spaces filtered by type and date range |
| GET | `/api/space/availability-counts` | 🔓 Public | Get availability counts per space type |
| GET | `/api/space` | 🔒 JWT | List all spaces (paginated) |
| GET | `/api/space/{id}/summary` | 🔒 JWT | Get detailed space summary |
| POST | `/api/space` | 🔒 JWT | Create a new space |
| PUT | `/api/space/{id}` | 🔒 JWT | Update a space |
| DELETE | `/api/space/{id}` | 🔒 JWT | Delete a space |

**Query params for GET `/api/space/available-by-type`:** `spaceType`, `startDateTime`, `endDateTime`

---

### Booking

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/booking/available-spaces` | 🔓 Public | Get spaces available for a given type and time range |
| GET | `/api/booking/available-spaces-reassignment` | 🔓 Public | Get spaces available for reassignment (excludes a booking) |
| GET | `/api/booking/smart/available` | 🔓 Public | Smart availability search by category and capacity |
| GET | `/api/booking/my` | 🔒 JWT + `x-user-email` | Get bookings for the current user |
| GET | `/api/booking/recent` | 🔒 JWT | Get most recent bookings |
| GET | `/api/booking/calendar` | 🔒 JWT | Get booking calendar for a space by month |
| GET | `/api/booking` | 🔒 JWT | List all bookings (paginated) |
| GET | `/api/booking/{id}` | 🔒 JWT + `x-user-email` | Get booking by ID |
| POST | `/api/booking` | 🔒 JWT + `x-user-email` | Create a new booking |
| POST | `/api/booking/smart` | 🔒 JWT + `x-user-email` | Create a smart booking (auto-assigns best space) |
| PUT | `/api/booking/{id}` | 🔒 JWT | Update a booking |
| PATCH | `/api/booking/{id}/cancel` | 🔒 JWT + `x-user-email` | Cancel a booking |
| PATCH | `/api/booking/{id}/status` | 🔒 JWT | Update booking status |
| PATCH | `/api/booking/{id}/reassign` | 🔒 JWT + `x-user-email` | Reassign booking to a different space |

**Query params for calendar:** `spaceId`, `year`, `month`
**Query params for smart available:** `spaceCategory`, `startDateTime`, `endDateTime`, `capacity`

---

### Payment

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/payment/my` | 🔒 JWT + `x-user-email` | Get payments for the current user |
| GET | `/api/payment` | 🔒 JWT | List all payments (paginated) |
| GET | `/api/payment/{id}/summary` | 🔒 JWT | Get payment summary by ID |
| POST | `/api/payment` | 🔒 JWT + `x-user-email` | Create a payment record |
| POST | `/api/payment/card` | 🔒 JWT + `x-user-email` | Process a card payment |
| POST | `/api/payment/voucher/generate` | 🔒 JWT + `x-user-email` | Generate a payment voucher |
| POST | `/api/payment/payfast/initiate` | 🔒 JWT + `x-user-email` | Initiate a PayFast hosted payment |
| POST | `/api/payment/payfast/notify` | 🔓 Public | PayFast IPN (webhook) callback |
| PATCH | `/api/payment/{id}/status` | 🔒 JWT | Update payment status |
| DELETE | `/api/payment/{id}` | 🔒 JWT | Delete a payment record |

---

### Membership

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/membership` | 🔒 JWT | List all memberships (paginated) |
| GET | `/api/membership/{id}/summary` | 🔒 JWT | Get membership summary |
| POST | `/api/membership` | 🔒 JWT | Create a new membership |
| PATCH | `/api/membership/{id}/status` | 🔒 JWT | Update membership status |
| DELETE | `/api/membership/{id}` | 🔒 JWT | Delete a membership |

---

### Pricing Plan

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/pricingplan/all` | 🔓 Public | Get all pricing plans (no pagination) |
| GET | `/api/pricingplan` | 🔓 Public | List pricing plans (paginated) |
| GET | `/api/pricingplan/{id}/summary` | 🔓 Public | Get pricing plan summary with features |
| POST | `/api/pricingplan` | 🔒 JWT | Create a pricing plan |
| PUT | `/api/pricingplan/{id}` | 🔒 JWT | Update a pricing plan |
| DELETE | `/api/pricingplan/{id}` | 🔒 JWT | Delete a pricing plan |

---

### Plan Feature

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/planfeature/by-plan/{planId}` | 🔒 JWT | Get all features for a pricing plan |
| POST | `/api/planfeature` | 🔒 JWT | Create a plan feature |
| PUT | `/api/planfeature/{id}` | 🔒 JWT | Update a plan feature |
| DELETE | `/api/planfeature/{id}` | 🔒 JWT | Delete a plan feature |

---

### Location

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/location/all` | 🔓 Public | Get all locations (no pagination) |
| GET | `/api/location` | 🔓 Public | List locations (paginated) |
| POST | `/api/location` | 🔒 JWT | Create a location |
| PUT | `/api/location/{id}` | 🔒 JWT | Update a location |
| DELETE | `/api/location/{id}` | 🔒 JWT | Delete a location |

---

### Branch / Company / City

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/branch` | 🔓 Public | Get all branches |
| GET | `/api/company` | 🔓 Public | Get all companies |
| GET | `/api/city` | 🔓 Public | Get all cities |

---

### Floor

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/floor` | 🔒 JWT | List floors (optionally filtered by `locationId`) |
| POST | `/api/floor` | 🔒 JWT | Create a floor |

---

### Space Type

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/spacetype/all` | 🔓 Public | Get all space types (no pagination) |
| GET | `/api/spacetype` | 🔓 Public | List space types (paginated) |
| POST | `/api/spacetype` | 🔒 JWT | Create a space type |
| PUT | `/api/spacetype/{id}` | 🔒 JWT | Update a space type |
| DELETE | `/api/spacetype/{id}` | 🔒 JWT | Delete a space type |

---

### Space Config

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/space-config` | 🔓 Public | Get all space configuration settings |
| GET | `/api/space-config/deposit/{category}` | 🔓 Public | Get security deposit amount for a space category |
| PUT | `/api/space-config/{category}` | 🔒 JWT + `x-user-email` | Update space config for a category |
| POST | `/api/space-config/generate-inventory` | 🔒 JWT | Auto-generate space inventory |

---

### Amenity

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/amenity` | 🔓 Public | Get all amenities |
| POST | `/api/amenity` | 🔒 JWT | Create an amenity |

---

### Gallery

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/gallery/all` | 🔓 Public | Get all gallery images (no pagination) |
| GET | `/api/gallery` | 🔓 Public | List gallery images (paginated) |
| POST | `/api/gallery` | 🔒 JWT | Upload/create a gallery image |
| PUT | `/api/gallery/{id}` | 🔒 JWT | Update a gallery image |
| DELETE | `/api/gallery/{id}` | 🔒 JWT | Delete a gallery image |

---

### Contact / Book Tour

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/contact/recent` | 🔒 JWT | Get recent contact submissions |
| GET | `/api/contact` | 🔒 JWT | List all contacts (paginated) |
| POST | `/api/contact` | 🔓 Public | Submit a contact form |
| POST | `/api/book-tour` | 🔓 Public | Submit a book-a-tour request (same handler as contact) |
| PATCH | `/api/contact/{id}/status` | 🔒 JWT | Update contact status |
| DELETE | `/api/contact/{id}` | 🔒 JWT | Delete a contact entry |

---

### Dashboard

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/dashboard/summary` | 🔒 JWT | Get dashboard summary stats |
| GET | `/` | 🔓 Public | Health check |

---

## Response Format

All endpoints return a consistent JSON envelope:

### Success
```json
{
  "isSuccessful": true,
  "message": "Success",
  "data": { ... }
}
```

### Failure
```json
{
  "isSuccessful": false,
  "message": "Error description",
  "errors": ["field error 1", "field error 2"]
}
```

### HTTP Status Codes

| Code | Meaning |
|------|---------|
| 200 | OK — successful GET, PUT, PATCH, DELETE |
| 201 | Created — successful POST |
| 400 | Bad Request — validation failure |
| 401 | Unauthorized — missing or invalid token |
| 404 | Not Found — resource does not exist |
| 500 | Internal Server Error — unhandled exception |

---

## Pagination

Paginated list endpoints accept these query parameters:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `page` | int | 1 | Page number (1-based) |
| `limit` | int | 10 | Items per page |
| `search` | string | `""` | Search/filter term |

Paginated response shape:
```json
{
  "data": [ ... ],
  "total": 42
}
```

---

## Error Handling

A global `ExceptionMiddleware` catches all unhandled exceptions and returns:

```json
{
  "isSuccessful": false,
  "message": "An unexpected error occurred. Please try again later."
}
```

Stack traces are never exposed to the client. All exceptions are logged via Serilog.

---

## Logging

Serilog is configured with two sinks:

- **Console** — structured logs during development
- **File** — rolling daily logs at `WorkNest.API/Logs/worknest-YYYYMMDD.log`, retained for 7 days

Log levels:
- Default: `Information`
- Microsoft / System namespaces: `Warning`

---

## CORS

Allowed origins are configured in `appsettings.json` under `Cors:AllowedOrigins`:

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:4200",
    "http://localhost:5173",
    "https://worknest.vercel.app",
    "https://worknestpk.com",
    "https://www.worknestpk.com"
  ]
}
```

All methods, headers, and credentials are allowed. Wildcard subdomains are supported.

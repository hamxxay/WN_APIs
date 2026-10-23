# Grandstream GWN in WorkNest: contract (API agent ↔ CMS agents)

Grandstream GWN Cloud (GDMS Networking) network "WorkNest F-7 Third Floor" (GWN network id 347087) belongs to
WorkNest location **2 (F-7)**. The UniFi network stays I-8 (location 1). Built the same way as the UniFi integration
(`UnifiService`, `UnifiController` with `UnifiLocationScopeFilter`, `NetworkDashboardService`, CMS `pages/admin/network/*`,
CMS `pages/admin/ceo/dash-network`). A working standalone version (same GWN calls) is in `/Users/ahmad/Documents/WN/WN_Grandstream`
(`server.js` = the GWN client logic to port to C#; `API.md` = the data shapes; `public/*` = the page ideas).

## GWN Cloud (proven)
- Token: POST https://{Domain}/oauth/token (form): grant_type=password, username, password = sha256hex(md5hex(password)),
  client_id = AppId, client_secret = SecretKey → { access_token, refresh_token, expires_in 3599 }. Refresh before expiry
  (grant_type=refresh_token); on failure log in again. On HTTP 401 / retCode 50003: new token, retry once.
- Call: https://{Domain}/oapi/{path}?access_token=T&appID=AppId&timestamp=TS&signature=SIG; JSON body;
  SIG = sha256hex("&access_token=T&appID=AppId&secretKey=SecretKey&timestamp=TS&" + (body ? sha256hex(body) + "&" : "")).
  Answer { data, msg, retCode } (0 = OK). Paths and quirks: see WN_Grandstream/server.js (statistics times are UTC;
  bandwidth arrays are bits/s; client firstSeen/lastSeen are network-local; router `ip` = WAN, `ipv4` = LAN;
  ssid/configuration is POST {id}).
- Actions (super admin only): POST v1.0.0/client/block { networkId, clientId (MAC), block: 1|0 } (check exact fields in
  WN_Grandstream's api_data.json at /private/tmp/claude-501/-Users-ahmad-Documents-WN/3fc2a943-b9bf-407e-b48b-e6319e81ebb5/scratchpad/gwn/api_data.json),
  POST v1.0.0/ap/reboot { mac / macs as the spec says }.

## Settings (WN_APIs appsettings.json, gitignored; never committed)
`"Gwn": { "Enabled": true, "Domain": "www.gwn.cloud", "AppId": "", "SecretKey": "", "Username": "", "Password": "",
"RefreshSeconds": 60 }` — the API agent copies the values from /Users/ahmad/Documents/WN/WN_Grandstream/.env into the LOCAL
WorkNest.API/appsettings.json and appsettings.Development.json only (both gitignored; check `git check-ignore`), never into a
tracked file. Without settings: every endpoint answers `available: false` with "Grandstream is not set up on this server."

## Database (script WN_APIs/Database/Gwn/GWN_01_grandstream.txt; same style as the other scripts: one statement, one
transaction, TRY/CATCH, safe to run again, header explaining it)
- `dbo.WN_GWN_NetworkLocations (NetworkId BIGINT, LocationId INT, NetworkName NVARCHAR(200), UpdatedBy NVARCHAR(256),
  UpdatedAt DATETIME2 DEFAULT SYSUTCDATETIME(), PK (NetworkId, LocationId))`; seed (347087, 2, 'WorkNest F-7 Third Floor').
- `dbo.WN_GWN_ClientAliases (Mac CHAR(17) PK, Alias NVARCHAR(100), UpdatedBy, UpdatedAt)`.
- `dbo.WN_GWN_ActionLog (Id IDENTITY PK, NetworkId BIGINT, Action NVARCHAR(30) /* block|unblock|reboot */, Target NVARCHAR(64),
  ByEmail NVARCHAR(256), ByUserId INT NULL, Ok BIT, Message NVARCHAR(500), CreatedAt DATETIME2 DEFAULT SYSUTCDATETIME())`.
- Service switch row `GwnPolling` in dbo.WN_ServiceSwitches (IsEnabled 1) — add `GwnPolling` to BackgroundServiceCatalog
  ("Grandstream Polling", keeps the Grandstream network figures fresh) with config key `Gwn:Enabled`; update the catalog
  tests' expected list.
- Plain T-SQL; no procedures needed unless the API agent prefers them (then CREATE OR ALTER inside the same script).

## API (WorkNest.API/Controllers/GwnController.cs) — same roles and location scoping as UnifiController
Read: `[Authorize(Roles = "admin,Admin,super_admin,SuperAdmin,sales_executive,SalesExecutive")]`; location-bound staff only
see networks mapped to one of their locations (403 otherwise), like UnifiLocationScopeFilter. Actions: super admin only.
Responses use the API's usual wrapper `ApiResponse.Ok(data)` → `{ isSuccessful, message, data }` (camelCase).
`net` = GWN network id, optional (default = the first network the caller may see). `locationId` optional alternative.
- `GET  api/gwn/networks` → `[{ id, name, locationIds: [2] }]`
- `GET  api/gwn/overview?net=` → `{ available, message, updatedAt, network: { id, name, country, timezone, locationIds },
  devices: { total, online, offline, list: [device] }, clients: { total, band24, band5, band6, wired, weak, deviceReported },
  usage: { since, download, upload, total }, trafficNow: { downloadBps, uploadBps }, ssids: [ssid], alerts: [{ level, text, where }] }`
- `GET  api/gwn/clients?net=` → `[client]`, client = `{ mac, name, alias, ip, ssid, ssidId, apMac, band, channel, rssi,
  connectedFor, connectedSeconds, download, upload, total, online, wired, blocked, manufacturer, os, host, firstSeen }`
- `GET  api/gwn/clients/history?net=&from=&to=` → `[{ mac, name, alias, ip, ssid, download, upload, total, firstSeen, lastSeen }]`
- `GET  api/gwn/client?net=&mac=` → `{ ...client, raw: {…} }`
- `GET  api/gwn/client/usage?net=&mac=&range=` → `{ times, download, upload, downloadTotal, uploadTotal }`
- `GET  api/gwn/devices?net=` → `[device]`, device = `{ mac, name, model, type, status, ip, publicIp, ipv6, firmware,
  latestFirmware, uptimeSeconds, clients, download, upload, total, today: { download, upload, total }, temperature, memory,
  firstSeen, lastSeen }`
- `GET  api/gwn/device?net=&mac=` → `{ ...device, info: [{ key, label, value }], clientList: [client] }`
- `GET  api/gwn/device/stats?net=&mac=&range=` → `{ times, clients, download, upload, downloadBps, uploadBps, downloadTotal, uploadTotal }`
- `GET  api/gwn/ssids?net=` → `[{ id, name, enabled, clients, security, schedule, portal, isolation, vlan, macFiltering, bands }]`
- `GET  api/gwn/stats/clients?net=&range=2h|1d|1w|1m` → `{ times, counts }`
- `GET  api/gwn/stats/bandwidth?net=&range=` → `{ times, download, upload, downloadBps, uploadBps, downloadTotal, uploadTotal }`
- `GET  api/gwn/stats/ssid?net=&range=` → `{ times, counts, series: [{ name, counts }] }`
- `POST api/gwn/alias` `{ mac, name }` → `{ mac, alias }` (empty name removes; WN_GWN_ClientAliases)
- `POST api/gwn/client/block` `{ net, mac, block: true|false }` → `{ mac, blocked }` (super admin; WN_GWN_ActionLog)
- `POST api/gwn/device/reboot` `{ net, mac }` → `{ mac, rebooting: true }` (super admin; WN_GWN_ActionLog)
- `GET  api/gwn/actions?net=&top=50` → recent WN_GWN_ActionLog rows (super admin)
- `GET  api/gwn/network-locations` / `PUT api/gwn/network-locations/{networkId}` `{ locationIds: [2] }` (super admin to edit)
- Times ISO-8601 UTC strings; mac "AA:BB:CC:DD:EE:FF"; bytes as numbers; never return secrets.
- Background: `GwnPollingService` (hosted, gated by the `GwnPolling` switch) refreshes the cache every RefreshSeconds;
  statistics every 5 minutes.

## CEO / Sales Manager dashboards (Network section)
`NetworkDashboardDto` (api/ceo/network, api/sales-manager/network) gets an optional `Gwn` property
(`GwnDashboardSectionDto`): `{ available, message, updatedAt, networks: [{ id, name, locationIds }], clients: { total,
band24, band5, wired, weak, deviceReported }, devices: { total, online, offline, list: [{ mac, name, model, status, clients,
temperature }] }, usageToday: { download, upload, total }, trafficNow: { downloadBps, uploadBps }, clientHistory: { times,
counts } (last 24 h), bandwidth: { times, downloadBps, uploadBps } (last 24 h), topClients: [{ mac, name, download, upload,
total }] (top 5 by total today), alerts: [{ level, text }] }`. Filled when the chosen location is mapped to a GWN network
(F-7 = 2), or for All locations (every network the caller may see); null otherwise (I-8). The UniFi part is unchanged
(after NET_02 it is empty for F-7). Same role / location rules as the rest of that endpoint.

## CMS (WN_CMS, Angular, same theme as the existing Network pages: reuse `network-shared.css` / `network-shared.ts`)
- `src/app/services/gwn.service.ts`: typed calls to every endpoint above (unwrap `data`, readable errors).
- A location switch at the top of the Network pages: "I-8 · UniFi" | "F-7 · Grandstream". UniFi pages stay as they are at
  `/admin/network/*`; Grandstream pages live at `/admin/network/gwn`, `/admin/network/gwn/clients`, `/admin/network/gwn/devices`,
  `/admin/network/gwn/device/:mac`, `/admin/network/gwn/wifi` (same guards as the UniFi network routes). The switch only
  shows a network the user may see.
- Grandstream pages mirror the UniFi pages' look: Overview (health, KPIs, traffic + clients charts 2h/1d/1w/1m, devices,
  SSIDs, top clients), Clients (live + history, search, sort, signal colours, detail drawer with rename; Block / Unblock for
  super admin with a confirm), Devices + Device detail (info, charts, connected clients; Reboot for super admin with a
  confirm), Wi-Fi (SSID cards + clients-per-SSID chart). Excel export like the other CMS lists.
- Dashboards: `pages/admin/ceo/dash-network` renders the `gwn` section (title "Grandstream · F-7") with the same cards /
  chart style it already uses for UniFi, when present.

## File ownership (agents must not edit each other's files)
- API agent: everything in WN_APIs (controller, service, DTOs, hosted service, catalog, Program.cs registration,
  NetworkDashboardService/DTO changes, the GWN_01 script, tests, local appsettings). Also adds `Gwn` to API docs.
- CMS network agent: `src/app/services/gwn.service.ts`, `src/app/pages/admin/network/gwn-*/**`, the location switch inside
  `pages/admin/network/*` (shared header), `app.routes.ts` network routes, the sidebar entry if needed.
- CMS dashboard agent: `pages/admin/ceo/dash-network/**` and `src/app/services/network-dashboard.service.ts` (the `gwn` field).

## As implemented by the API agent (clarifications; shapes above unchanged, only extra fields added)
- Not set up / `Gwn:Enabled` false: EVERY `api/gwn/*` endpoint answers 200 `{ isSuccessful: true, message, data: { available: false,
  message } }` (also the list endpoints — check `data.available === false` before treating `data` as an array).
- Errors: 400 bad input (`Invalid MAC address.`), 403 network of another location / not super admin, 404 unknown network /
  client / device, 502 GWN error (plain message), 503 the GWN_01 tables are missing (alias save, network-location save).
  `overview` never 502s: a GWN failure gives `data: { available: false, message }`.
- `net` may also be sent as `locationId` (query, or in the body for block / reboot). Location-bound staff: a network linked to
  no location is not theirs. Until GWN_01 has run, the mapping falls back to `Gwn:DefaultNetworkLocations` (default `347087:2`).
- `client/block` body `block: true` = block. The API sends GWN `{ block: 0 (block) | 1 (unblock), clientId: "AA:BB:..", networkId }`
  (GWN: 0 = Block, 1 = Unblock). `device/reboot` sends GWN `{ mac: ["ec74d79df768"] }`; 404 when the MAC is not a device of `net`.
- Extra fields: client `gwnName, ipv6, apName, guest, vlan, lastSeen`; client detail `lastActive, raw, infoError`; device
  `channel24, channel5, remarks`; device detail `radios: [{ band, channel, clients, power }], infoError`; overview
  `network.timezoneDisplay`; ssid `remark, devices` (+ on `api/gwn/ssids`: `bands: { "2.4G": n, "5G": n, "wired"/"other": n }`
  (counts of connected clients; camelCase keys), `weak, download, upload, config: { bands: ["2.4 GHz"], hidden, clientLimit,
  vlanId, roaming80211r/k/v, idleTimeout, mlo }`). `client/usage` also has `downloadBps, uploadBps`. `clients/history`
  rows are full client objects (+ `online`); `from` / `to` = epoch ms or ISO date-time.
- `GET api/gwn/network-locations` → `{ available, message, canEdit, networks: [{ id, name, locationIds }] }` (only the networks
  the caller may see). `PUT` answers the saved network `{ id, name, locationIds }`.
- `GET api/gwn/actions` rows: `{ id, networkId, action, target, byEmail, byUserId, ok, message, createdAt }`.
- `POST api/gwn/alias`: admin / super admin (as UniFi's alias).
- Dashboards: `gwn` is null when no linked network matches (I-8); for F-7 / All locations while GWN is not set up: `{ available: false, message: "Grandstream is not set up on this server." }`; while GWN has not
  answered within 8 s: `available: false` + "No Grandstream figures yet...". Alerts there are critical / serious only.

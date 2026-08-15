# MobilePaymentWeb

ASP.NET Core MVC web app (.NET 8) for paying parking fees from a phone, via QR-code
scan or license-plate lookup. Integrates three external providers:

| Provider | Role | Protocol |
|----------|------|----------|
| **Skidata** (SwebMobile) | Parking management — look up session, get rate, book payment | SOAP / WCF |
| **iPay.ua** / **Monobank** | Payment gateway (per-facility) — create / authorize / complete / reverse | iPay: XML/HTTP HMAC-SHA512 · Monobank: JSON/HTTP, `X-Token` |
| **CheckBox** | Ukrainian fiscal receipts (ПРРО) | REST / JSON |

UI is entirely in Ukrainian.

## User flow

QR URL like `/{facilityId}/{ticketNumber}` (e.g. `/0330142/29031864033014200000630`)
→ look up the session in Skidata → show price → user pays via iPay → on success the app
**books the payment back into Skidata** (to open the gate) and **generates a fiscal receipt
PNG** from CheckBox, shown on screen.

### Routes (all in `ParkingSessionController`)

| URL | Action |
|-----|--------|
| `/{facilityId}` | License plate entry form |
| `/{facilityId}/{ticketNumber}` | Parking session lookup (from QR code) |
| `/Pay` (POST) | Create iPay payment, redirect to gateway |
| `/status` | Payment status polling (JSON) — the subtle part, see below |
| `/success` | Success page (polls `/status`, shows fiscal receipt PNG) |
| `/fail` | Payment failure page |
| `/receipt-image` | Fiscal receipt PNG (session-scoped) |

Unknown facility IDs return the `InvalidFacility` view ("Невірне посилання").

## Architecture

- **Multi-tenant by facility.** `appsettings.json` has a `Facilities` dictionary keyed by
  the URL segment. Each facility bundles 4 config sections: `Skidata`, `Payment`, `Fiscal`,
  `Display` (see `FacilityOptions`). Adding a facility = config + logo file, no code changes.
  - Note: `Skidata.FacilityID` (internal Skidata id, e.g. `330084`) differs from the URL key
    (e.g. `0330142`). API calls use `APT.FAC.{Skidata.FacilityID}`.
- **Single controller** — `Controllers/ParkingSessionController.cs` holds all routes.
- **Factory + interface pattern** for providers. `IPaymentProviderFactory` /
  `IFiscalProviderFactory` build a per-facility provider dictionary at startup.
  - `IPaymentProvider` → `IPay/IPayClient.cs` (XML) or `Monobank/MonobankClient.cs` (JSON);
    `PaymentProviderFactory` switches on `Payment.Provider` (`"IPay"` default | `"Monobank"`).
    Monobank uses two-phase `hold`: create → book into Skidata → `finalize` (or `cancel` on
    booking failure), mapping onto the same authorize/complete/reverse interface as iPay.
  - `IFiscalProvider` → implemented by `Fiscal/CheckBox/CheckBoxFiscalClient.cs`
    (factory only wires it up when `Fiscal.Provider == "CheckBox"`; otherwise null → no receipt)
  - These abstractions localize the cost of swapping a vendor later.
- **State via session + `IMemoryCache`.** iPay `pid`, amount, ticket id/type live in the
  session; `booked_{pid}` and `receipt_png_{pid}` (5-min TTL) live in the cache to make
  status-polling idempotent.

## Payment / status flow (the tricky part)

`/status` is polled every 2s by the success page and handles iPay's two-phase auth:

1. **Authorized** → book into Skidata (`BookPaymentAsync`) → `CompletePaymentAsync`
   (or `ReversePaymentAsync` if booking fails). Guarded by the `booked_{pid}` cache key so
   repeated polls don't double-book.
2. **Success** (iPay status 5) → generate the CheckBox fiscal receipt, cache the PNG.

**Price-drift protection** in `BookPaymentAsync`: books the amount actually paid if the
Skidata rate is still valid (`rate_end` in the future); otherwise re-fetches the current
rate and logs a warning on mismatch.

### CheckBox receipt sub-flow (`CreateReceiptAsync`)

signin (PIN code) → ensure shift open (open one if none) → `receipts/sell` → poll receipt
until `DONE` → fetch PNG. Auth token is cached on the client and refreshed on 401.

## Project layout

```
MobilePaymentWeb/
├── Controllers/ParkingSessionController.cs   — all routes
├── Models/
│   ├── Fiscal/            — FiscalRequest, FiscalResponse
│   ├── Payment/           — PaymentRequest, PaymentResponse, PaymentStatus, PaymentStatusResponse
│   ├── ParkingSessionViewModel.cs
│   └── ParkingSessionErrorModel.cs
├── Services/
│   ├── Fiscal/
│   │   ├── CheckBox/CheckBoxFiscalClient.cs + Models/   — CheckBox REST client
│   │   └── FiscalOptions.cs
│   ├── IPay/IPayClient.cs + PaymentOptions.cs + Models/ — iPay XML client
│   ├── SkidataMobilePay/
│   │   ├── SwebMobileService.cs   — generated WCF SOAP client
│   │   ├── SkidataOptions.cs, SkidataFault.cs, MessageLogger.cs
│   ├── FacilityOptions.cs, DisplayOptions.cs
│   ├── IPaymentProvider.cs / IPaymentProviderFactory.cs / PaymentProviderFactory.cs
│   └── IFiscalProvider.cs / IFiscalProviderFactory.cs / FiscalProviderFactory.cs
├── Views/ParkingSession/   — LicensePlateForm, ParkingSession, ParkingSessionExit,
│                             PaymentSuccess, PaymentFail, ParkingSessionError, InvalidFacility
└── wwwroot/images/         — logo files (referenced by Display.Logo)
```

## Deployment

Docker (ARM64) → GitHub Container Registry (`ghcr.io/mykolakolbun/mobilepaymentweb:latest`)
→ SSH into a **Raspberry Pi** running `docker compose pull && up -d`, via GitHub Actions
(`.github/workflows/deploy.yml`) on push to `main`.

**Credentials must not be committed.** Override per-environment via `appsettings.Production.json`
(git-ignored) or Docker env vars using `__` nesting, e.g.
`Facilities__0330142__Payment__SignKey=...`.

## Local dev

```bash
cd MobilePaymentWeb
dotnet run
```

iPay runs in sandbox by default (`Payment.Sandbox: true`). Test URL:
`https://localhost:{port}/0330142/29031864033014200000630`.

See **SETUP.md** for full per-field config reference and how to add a facility.

## Notable conventions

- Kyiv timezone (`Europe/Kiev`) is used for parking-duration math.
- iPay amounts are in **kopecks** (`Price * 100`); so are CheckBox prices.
- License plates are converted Cyrillic→Latin (`Helpers.ConvertToLatin`) and validated
  against `^[A-Z0-9]{3,10}$`; tickets against `^\d{1,30}$`.
- WCF clients are created per-request and always `Close()`/`Abort()`ed in a `finally`.

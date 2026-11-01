# MobilePaymentWeb — Setup Guide

Web application for parking payment via QR code or license plate lookup.
Integrates with **Skidata** parking management, **iPay.ua** payment gateway, and **CheckBox** fiscal provider.

---

## URL Structure

| URL | Action |
|-----|--------|
| `/{facilityId}` | License plate entry form |
| `/{facilityId}/{ticketNumber}` | Parking session lookup (from QR code) |
| `/success` | Payment success page (shows fiscal receipt PNG) |
| `/fail` | Payment failure page |
| `/status` | Payment status polling (JSON) |
| `/receipt-image` | Fiscal receipt PNG (session-scoped) |

**Example QR code URL:** `https://paypark.alternatiview.com.ua/0330142/29031864033014200000630`
- `0330142` — facility ID (must match a key in `Facilities` config)
- `29031864033014200000630` — parking ticket number

Unknown facility IDs return the "Невірне посилання" page.

---

## Configuration (`appsettings.json`)

All configuration lives under `Facilities`, keyed by the URL facility ID segment.
Each facility has four independent sections:

```json
{
  "Facilities": {
    "<facilityId>": {
      "Skidata":  { ... },
      "Payment":  { ... },
      "Fiscal":   { ... },
      "Display":  { ... }
    }
  }
}
```

### Skidata (parking management SOAP service)

```json
"Skidata": {
  "Endpoint":           "https://car.webhost.skidata.com/bei/ParkingTicketManagementServiceV4",
  "FacilityID":         "330084",
  "ExternalDeviceID":   80,
  "ParkingDeviceID":    80,
  "Login":              "your-login",
  "Password":           "your-password",
  "BookingPaymentType": "WEB",
  "BookingEnabled":     true
}
```

| Field | Description |
|-------|-------------|
| `Endpoint` | Skidata SOAP service URL |
| `FacilityID` | Internal Skidata facility ID used in API calls (`APT.FAC.{FacilityID}`) |
| `ExternalDeviceID` | Device ID sent in Skidata requests |
| `ParkingDeviceID` | Parking device ID sent in Skidata requests |
| `Login` / `Password` | Basic auth credentials for the SOAP service |
| `BookingPaymentType` | Name of the manual payment method configured on the Skidata server, sent when booking the payment (`ManualPaymentItem.PaymentType`). Default `"WEB"` |
| `BookingEnabled` | `true` (default) books the payment into Skidata on success. Set `false` for **testing** to exercise the payment/fiscal flow without registering real bookings (BookPayment is skipped and logged) |

> `FacilityID` in config is the Skidata internal ID and may differ from the URL key.
> Example: URL key `0330142` → Skidata `FacilityID` `330084`.

### Payment

Selected per facility by `Provider`. Two gateways are supported: **iPay.ua** and **Monobank**.

#### iPay.ua (`"Provider": "IPay"`, default)

```json
"Payment": {
  "Provider": "IPay",
  "MchId":    "your-merchant-id",
  "SignKey":  "your-sign-key",
  "Sandbox":  true
}
```

| Field | Description |
|-------|-------------|
| `Provider` | `"IPay"` (default if omitted) |
| `MchId` | iPay merchant ID |
| `SignKey` | HMAC-SHA512 signing key |
| `Sandbox` | `true` = sandbox endpoint, `false` = production |

Switch to production: set `"Sandbox": false`.

#### Monobank (`"Provider": "Monobank"`)

```json
"Payment": {
  "Provider": "Monobank",
  "Token":    "your-merchant-x-token"
}
```

| Field | Description |
|-------|-------------|
| `Provider` | `"Monobank"` |
| `Token` | Merchant token sent as the `X-Token` header. A **test token** targets the sandbox; a production token targets live — same base URL (`https://api.monobank.ua`), so there is no `Sandbox` flag |

Monobank uses **two-phase `hold` payments**: the invoice is created with `paymentType: hold`,
the amount is only captured (`finalize`) after the Skidata gate booking succeeds, and released
(`cancel`) if booking fails — the same authorize → complete / reverse flow as iPay. Status is
polled via `GET /api/merchant/invoice/status`; webhooks are not used.

### Fiscal (CheckBox)

```json
"Fiscal": {
  "Provider":   "CheckBox",
  "ApiUrl":     "https://api.checkbox.in.ua/",
  "LicenseKey": "your-license-key",
  "PinCode":    "your-pin-code",
  "TaxCode":    0
}
```

| Field | Description |
|-------|-------------|
| `Provider` | `"CheckBox"` to enable, `"None"` to disable |
| `ApiUrl` | CheckBox API base URL (default is production) |
| `LicenseKey` | Cash register license key (`X-License-Key` header) |
| `PinCode` | Cashier PIN for `signinPinCode` |
| `TaxCode` | Tax ID integer from your CheckBox cashier tax configuration. Use `0` if no tax applies |

**Fiscal receipt flow (automatic):**
1. User pays → redirected to `/success`
2. Page polls `/status` every 2 seconds
3. On first confirmed success (iPay status 5): CheckBox signin → shift check/open → sell receipt → poll DONE → fetch PNG
4. Receipt PNG cached in memory for 5 minutes, displayed on screen

> The cashier's shift is managed automatically. If no shift is open, one is opened before creating the receipt.

### Display

```json
"Display": {
  "Logo": "alternatiview_logo_big.svg",
  "Name": "Паркінг"
}
```

| Field | Description |
|-------|-------------|
| `Logo` | Logo filename from `wwwroot/images/`. Leave empty to show no logo |
| `Name` | Facility name (used as alt text and can be referenced in views) |

Logo files must be placed in `MobilePaymentWeb/wwwroot/images/`.

---

## Adding a New Facility

1. Add a new entry to `Facilities` in `appsettings.json`:

```json
"Facilities": {
  "0330142": { ... },
  "0330200": {
    "Skidata": {
      "Endpoint":         "https://...",
      "FacilityID":       "330200",
      "ExternalDeviceID": 80,
      "ParkingDeviceID":  80,
      "Login":            "",
      "Password":         ""
    },
    "Payment": {
      "MchId":   "",
      "SignKey":  "",
      "Sandbox":  true
    },
    "Fiscal": {
      "Provider":   "CheckBox",
      "ApiUrl":     "https://api.checkbox.in.ua/",
      "LicenseKey": "",
      "PinCode":    "",
      "TaxCode":    0
    },
    "Display": {
      "Logo": "facility2_logo.svg",
      "Name": "Паркінг 2"
    }
  }
}
```

2. Drop the logo file into `wwwroot/images/`
3. Restart the application — no code changes needed

---

## Deployment

### Docker / Raspberry Pi (current setup)

CI/CD runs on push to `main` via GitHub Actions (`.github/workflows/deploy.yml`):

1. Builds ARM64 Docker image
2. Pushes to GitHub Container Registry (`ghcr.io/mykolakolbun/mobilepaymentweb:latest`)
3. SSH into Raspberry Pi → `docker compose pull && docker compose up -d`

**Required GitHub Secrets:**

| Secret | Description |
|--------|-------------|
| `PI_SSH_PRIVATE_KEY` | SSH private key (ed25519) for Raspberry Pi |
| `PI_HOST` | Raspberry Pi IP or hostname |
| `PI_USER` | SSH username |

### Environment variables (production credentials)

Do not commit credentials to `appsettings.json`. Override per-environment using:

**Option A — `appsettings.Production.json`** (excluded from source control):
```json
{
  "Facilities": {
    "0330142": {
      "Skidata":  { "Login": "...", "Password": "..." },
      "Payment":  { "MchId": "...", "SignKey": "...", "Sandbox": false },
      "Fiscal":   { "LicenseKey": "...", "PinCode": "..." }
    }
  }
}
```

**Option B — Docker environment variables** (nested config with `__`):
```
Facilities__0330142__Payment__MchId=...
Facilities__0330142__Payment__SignKey=...
Facilities__0330142__Skidata__Login=...
Facilities__0330142__Fiscal__LicenseKey=...
```

---

## Local Development

```bash
cd "C:\My Programming\MobilePaymentWeb\MobilePaymentWeb"
dotnet run
```

Application starts at `https://localhost:{port}`.

Test with a known ticket:
```
https://localhost:{port}/0330142/29031864033014200000630
```

iPay runs in sandbox mode by default (`"Sandbox": true`). CheckBox API also has a sandbox:
```json
"ApiUrl": "https://api.checkbox.in.ua/"
```
Use `https://dev-api.checkbox.in.ua/` for CheckBox sandbox if available.

---

## Project Structure

```
MobilePaymentWeb/
├── Controllers/
│   └── ParkingSessionController.cs   — all routes
├── Models/
│   ├── Fiscal/                        — FiscalRequest, FiscalResponse
│   ├── Payment/                       — PaymentRequest, PaymentResponse, PaymentStatus
│   └── ParkingSessionViewModel.cs
├── Services/
│   ├── Fiscal/
│   │   ├── CheckBox/
│   │   │   ├── Models/                — CheckBox API request/response models
│   │   │   └── CheckBoxFiscalClient.cs
│   │   └── FiscalOptions.cs
│   ├── IPay/
│   │   ├── IPayClient.cs              — iPay HTTP client
│   │   └── PaymentOptions.cs
│   ├── SkidataMobilePay/
│   │   ├── SwebMobileService.cs       — generated WCF SOAP client
│   │   └── SkidataOptions.cs
│   ├── FacilityOptions.cs             — per-facility config wrapper
│   ├── IFiscalProvider.cs / IFiscalProviderFactory.cs
│   ├── IPaymentProvider.cs / IPaymentProviderFactory.cs
│   └── DisplayOptions.cs
├── Views/ParkingSession/
│   ├── LicensePlateForm.cshtml        — entry form
│   ├── ParkingSession.cshtml          — session details + pay button
│   ├── ParkingSessionExit.cshtml      — already-exited state
│   ├── PaymentSuccess.cshtml          — polls status, shows fiscal receipt
│   ├── PaymentFail.cshtml
│   ├── ParkingSessionError.cshtml
│   └── InvalidFacility.cshtml         — unknown facility ID
└── wwwroot/images/                    — logo files go here
```

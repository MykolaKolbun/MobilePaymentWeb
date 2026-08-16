# Production Deployment (Docker, x64 Linux)

Step-by-step guide to deploy MobilePaymentWeb to a customer's **x86-64 Linux** server
with Docker. Assumes one facility (`0330142`, Silver Breeze). See **SETUP.md** for the
full per-field config reference.

> The same container image is used everywhere; a host differs only by its `.env`
> (secrets/config), its compose file, and its public domain.

---

## 0. Prerequisites

- x86-64 Linux server with **Docker** + **Docker Compose v2** (`docker compose version`).
- A **public domain** for the app (e.g. `park.customer.com`) with TLS. This domain must be:
  - what the **QR codes on the tickets encode** (`https://<domain>/{facilityId}/{ticket}`), and
  - where iPay redirects back after payment (`/success`, `/fail`).
- **Real production credentials**: Skidata login/password, iPay **production** MchId/SignKey,
  CheckBox **real** license key + PIN, (optional) Telegram bot token + chat id.
- Read access to the image registry (`ghcr.io/mykolakolbun/mobilepaymentweb`).

---

## 1. Build the production (amd64) image

Branch model: **`stage`** builds `linux/arm64` as `:latest` for the Pi
(`deploy-stage.yml`); **`silverbreeze`** builds `linux/amd64` as `:prod` for the
x64 production host (`deploy-silverbreeze.yml`). Each environment builds only its
own architecture — no multi-arch needed.

So to publish a production image: merge/push your release to the **`silverbreeze`**
branch → the workflow builds and pushes `ghcr.io/mykolakolbun/mobilepaymentweb:prod`.
Confirm it's amd64:

```bash
docker buildx imagetools inspect ghcr.io/mykolakolbun/mobilepaymentweb:prod
```

> The `silverbreeze` workflow builds on every push, but only **deploys** once the
> `PROD_SSH_HOST` (and other `PROD_*`) repo secrets are set — see step 9.

---

## 2. Registry login on the prod server

GHCR packages are private by default. On the prod server, log in once with a
Personal Access Token that has `read:packages`:

```bash
echo <GHCR_PAT> | docker login ghcr.io -u MykolaKolbun --password-stdin
```

(Alternatively make the package public in GitHub → Packages → package settings.)

---

## 3. Create the app directory and compose file

```bash
mkdir -p ~/mobilepaymentweb && cd ~/mobilepaymentweb
```

`~/mobilepaymentweb/docker-compose.yml`:

```yaml
services:
  app:
    image: ghcr.io/mykolakolbun/mobilepaymentweb:prod
    container_name: mobilepaymentweb
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"   # localhost only — the reverse proxy reaches it, the internet does NOT
    env_file:
      - .env
    volumes:
      - dpkeys:/keys            # persist DataProtection keys across redeploys

volumes:
  dpkeys:
```

> **Security:** bind to `127.0.0.1:8080`, never `0.0.0.0:8080`. The app trusts
> `X-Forwarded-Proto`/`For` from any proxy (see `UseForwardedHeaders`), which is only
> safe when the port is *not* directly reachable from the internet.

---

## 4. Production secrets & config (`.env`)

Create `~/mobilepaymentweb/.env` with the **real** values (env-var form, `__` = nesting).
This file stays only on the server — never commit it.

```dotenv
# Skidata (production)
Facilities__0330142__Skidata__Login=REPLACE
Facilities__0330142__Skidata__Password=REPLACE

# iPay — PRODUCTION merchant, sandbox OFF  ⚠️
Facilities__0330142__Payment__MchId=REPLACE
Facilities__0330142__Payment__SignKey=REPLACE
Facilities__0330142__Payment__Sandbox=false

# CheckBox (production license)
Facilities__0330142__Fiscal__LicenseKey=REPLACE
Facilities__0330142__Fiscal__PinCode=REPLACE

# Telegram alerting (optional)
Telegram__Enabled=true
Telegram__BotToken=REPLACE
Telegram__ChatId=REPLACE
```

```bash
chmod 600 ~/mobilepaymentweb/.env
```

**Critical production switches:**
- `Payment__Sandbox=false` — moves iPay from the sandbox to the **live** endpoint (real money).
- Use the **customer's real** Skidata / iPay / CheckBox credentials (not the test merchant `5680`
  or the test CheckBox license used on the Pi).
- Confirm the facility key `0330142` and `Skidata.FacilityID` match the real facility and the
  QR codes. `Skidata.BookingPaymentType` defaults to `WEB` — override here only if the Skidata
  server uses a different manual-method name.

---

## 5. Reverse proxy + TLS

The app serves plain **HTTP on 127.0.0.1:8080** and expects the proxy to terminate TLS and set
`X-Forwarded-Proto`. Pick one:

**Caddy (simplest — automatic Let's Encrypt TLS):** `/etc/caddy/Caddyfile`

```
park.customer.com {
    reverse_proxy 127.0.0.1:8080
}
```

Caddy sets `X-Forwarded-Proto` automatically. Point the domain's DNS at the server first.

**Cloudflared tunnel (no open inbound ports, like the Pi):** add an ingress rule

```yaml
ingress:
  - hostname: park.customer.com
    service: http://localhost:8080
  - service: http_status:404
```

**nginx + certbot** also works — ensure it forwards `X-Forwarded-Proto $scheme` and
`X-Forwarded-For`.

---

## 6. Pull and start

```bash
cd ~/mobilepaymentweb
docker compose pull
docker compose up -d
docker compose ps            # expect: Up, healthy
docker compose logs -f app
```

Healthy startup shows:
- `Now listening on: http://[::]:8080`
- **no** "Failed to determine the https port" warning (forwarded headers working)
- **no** "keys ... may not be persisted" warning (dpkeys volume mounted)
- if Telegram enabled: a `✅ … alerting online` message in the chat

---

## 7. Verify end-to-end

```bash
# facility renders (on the server)
curl -s http://127.0.0.1:8080/0330142 | grep -o 'Silver Breeze'
```

Then, in a browser via the public domain, with a **real active ticket**:

1. Open `https://park.customer.com/0330142/<ticket>` → session details + price.
2. Pay (real card, since sandbox is off) → land on `/success`.
3. Confirm: **receipt renders**, and in the logs the iPay request shows
   `<good>https://park.customer.com/success</good>` (https), a clean **`BookPayment`**
   (no fault), and `CheckBox … receipt DONE`.
4. Re-lookup the plate → should read `0 грн` (payment booked in Skidata).

> Start with one real low-cost transaction to confirm live iPay + fiscal before going public.

---

## 8. Updates / redeploys

```bash
cd ~/mobilepaymentweb && docker compose pull && docker compose up -d
```

- DataProtection keys persist via the `dpkeys` volume, so cookies survive redeploys.
- Session **data** is in-memory, so an in-flight payment session breaks on restart —
  deploy during quiet periods.
- To roll back, set a pinned version tag in `docker-compose.yml`
  (e.g. `:1.0.0`) instead of `:prod`, then `docker compose up -d`.

---

## 9. CI auto-deploy to production

Pushing to the **`silverbreeze`** branch runs `deploy-silverbreeze.yml` — it builds the
`:prod` image and, once secrets are set, deploys to the prod host. Add these repo secrets
in GitHub → Settings → Secrets and variables → Actions:

| Secret | Purpose |
|---|---|
| `PROD_SSH_HOST` | prod server hostname/IP — **its presence enables the deploy step** |
| `PROD_SSH_USER` | SSH user |
| `PROD_SSH_PRIVATE_KEY` | base64-encoded SSH private key |

Until `PROD_SSH_HOST` is set, the workflow only builds/pushes the image (deploy is skipped),
so the **first** deploy is manual (steps 2–6). If the prod host uses a cloudflared tunnel
instead of direct SSH, adjust the SSH block in the workflow to match the stage workflow.

---

## Go-live checklist

- [ ] Image is **amd64** (multi-arch) and pulls on the x64 host
- [ ] `Payment__Sandbox=false` and **real** iPay / Skidata / CheckBox credentials in `.env`
- [ ] `.env` is `chmod 600`, not in git
- [ ] Port bound to `127.0.0.1` only; TLS proxy in front; domain DNS set
- [ ] QR codes on tickets encode the prod domain + correct facility id
- [ ] One real test transaction: payment → booking (WEB) → receipt all verified
- [ ] Telegram invite link revoked / join-approval on (if alerting is used)
- [ ] Backup of `.env` and the compose file stored securely off-server

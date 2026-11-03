# ControlDesk VPS Deployment Guide

**Version**: 1.0.0  
**Target Environment**: Linux VPS (Ubuntu 22.04 LTS / Ubuntu 24.04 LTS / Debian 12)  
**Architecture**: Docker Compose (ASP.NET Core Signaling Server + coturn TURN/STUN + Caddy TLS Reverse Proxy)

---

## 1. Architecture & Infrastructure Overview

To bridge the cross-border network path between India (Controller) and Dubai (Host) across strict corporate firewalls, symmetric NATs, and carrier-grade NATs (CGNAT), ControlDesk relies on a lightweight cloud VPS orchestrating:

1. **Signaling Server (`ControlDesk.Server`)**: ASP.NET Core WebSocket hub managing device presence, SDP negotiation, and dynamic coturn ephemeral token generation.
2. **coturn TURN/STUN Relay**: High-throughput media relay implementing RFC 5766 / RFC 8656 over UDP, TCP, and TLS.
3. **Caddy Reverse Proxy**: Reverse proxy providing automatic TLS certificate provisioning via Let's Encrypt for the signaling WebSocket domain.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             LINUX VPS (Public Static IP)                    │
│                                                                             │
│   ┌────────────────────────────────┐     ┌──────────────────────────────┐   │
│   │             Caddy              │     │            coturn            │   │
│   │   :80 (HTTP) -> Let's Encrypt  │     │   :3478 (STUN/TURN UDP/TCP)  │   │
│   │   :443 (HTTPS / WSS Proxy)     │     │   :5349 (TURNS over TLS)     │   │
│   └───────────────┬────────────────┘     │   :49152-65535 (Relay UDP)   │   │
│                   │ Reverse Proxy        └──────────────────────────────┘   │
│                   ▼                                                         │
│   ┌────────────────────────────────┐                                        │
│   │      Signaling Server          │                                        │
│   │   ASP.NET Core (.NET 10.0)     │                                        │
│   │   Internal Port :5000          │                                        │
│   └────────────────────────────────┘                                        │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Server Sizing & Geographical Selection

### 2.1 Geographic Placement for India ↔ Dubai

For minimal transit latency between India and Dubai, optimal VPS datacenter locations include:
- **UAE / Dubai / Bahrain (AWS me-central-1, Oracle Middle East, DigitalOcean / Linode)**: ~20–40 ms to Dubai, ~35–55 ms to Mumbai/Delhi.
- **India / Mumbai (AWS ap-south-1, GCP asia-south1, DigitalOcean BLR1)**: ~5–15 ms to India, ~35–55 ms to Dubai.

### 2.2 Hardware Requirements

| Parameter | Minimum | Recommended (Multi-session) |
|-----------|---------|-----------------------------|
| **vCPU** | 1 Core | 2 Cores |
| **RAM** | 1 GB | 2 GB to 4 GB |
| **Disk** | 20 GB SSD | 40 GB NVMe SSD |
| **Network Port** | 1 Gbps | 1 Gbps to 10 Gbps |
| **Monthly Bandwidth** | 1 TB | 3 TB+ |

---

## 3. Prerequisites & DNS Configuration

### 3.1 DNS Records

Configure two DNS `A` records pointing to your VPS public IPv4 address:

| Hostname | Type | Target | Purpose |
|----------|------|--------|---------|
| `signaling.yourdomain.com` | A | `YOUR_VPS_PUBLIC_IP` | Secure WebSocket signaling API |
| `turn.yourdomain.com` | A | `YOUR_VPS_PUBLIC_IP` | STUN and TURN relay traffic |

*(Optional)* If your VPS has an IPv6 address, create matching `AAAA` records for dual-stack operation.

---

## 4. VPS Firewall & Port Forwarding

Ensure the following inbound ports are open in your cloud provider's Security Group (AWS Security Group, Linode Firewall, Oracle VCN Security List, DigitalOcean Cloud Firewall) as well as the local VPS firewall:

| Port | Protocol | Service | Direction | Purpose |
|------|----------|---------|-----------|---------|
| **22** | TCP | SSH | Inbound | Remote administration |
| **80** | TCP | Caddy | Inbound | HTTP-01 ACME Let's Encrypt TLS challenge |
| **443** | TCP | Caddy | Inbound | WSS signaling (WebSocket over TLS) |
| **3478** | UDP & TCP | coturn | Inbound | STUN binding & TURN UDP/TCP relay |
| **5349** | UDP & TCP | coturn | Inbound | TURNS (TURN over TLS/DTLS) |
| **49152–65535** | UDP | coturn | Inbound | WebRTC dynamic media relay ports |

### 4.1 Configuring UFW (Ubuntu/Debian)

```bash
# Set default policies
sudo ufw default deny incoming
sudo ufw default allow outgoing

# Allow administrative access
sudo ufw allow 22/tcp

# Allow web & signaling
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Allow coturn STUN/TURN listening ports
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 5349/udp

# Allow coturn dynamic media relay port range
sudo ufw allow 49152:65535/udp

# Enable firewall
sudo ufw --force enable
sudo ufw status verbose
```

---

## 5. Deployment Step-by-Step

### 5.1 Step 1: Install Docker & Docker Compose

```bash
# Update system packages
sudo apt update && sudo apt upgrade -y

# Install Docker dependencies
sudo apt install -y ca-certificates curl gnupg lsb-release

# Add Docker's official GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Add Docker repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker Engine and Compose plugin
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Verify installation
docker --version
docker compose version
```

### 5.2 Step 2: Transfer and Unpack the Deployment Package

On your development machine, package the deployment bundle:
```powershell
.\scripts\package.ps1 -SkipClient
```

Upload the package to your VPS:
```bash
scp artifacts/Release/packages/ControlDesk-VPS-Deployment-*.zip user@your-vps-ip:/opt/
```

On your VPS:
```bash
cd /opt
sudo apt install -y unzip
sudo unzip ControlDesk-VPS-Deployment-*.zip -d controldesk-vps
cd controldesk-vps
```

### 5.3 Step 3: Configure Environment Variables

Create and configure your production `.env` file:
```bash
cp .env.template .env
nano .env
```

Fill in your actual deployment values:
```dotenv
# Domain Configuration
SIGNALING_DOMAIN=signaling.yourdomain.com
TURN_DOMAIN=turn.yourdomain.com

# Let's Encrypt Email
LETSENCRYPT_EMAIL=admin@yourdomain.com

# TURN Shared Secret
# Generate a cryptographically secure 32-byte base64 string using: openssl rand -base64 32
TURN_SECRET=dGhpcy1pcy1hLXNhbXBsZS0zMi1ieXRlLXNlY3JldC1rZXk=

# External VPS Public IPv4 (optional: coturn auto-detects if omitted)
EXTERNAL_IP=203.0.113.42
```

### 5.4 Step 4: Validate Configuration Files

Verify that `deployment/docker-compose.yml`, `deployment/caddy/Caddyfile`, and `deployment/coturn/turnserver.conf` correctly reference your environment variables.

### 5.5 Step 5: Launch Services

```bash
# Build and run containers in detached mode
docker compose up -d --build

# View container status
docker compose ps
```

All three containers (`controldesk-caddy`, `controldesk-signaling`, and `controldesk-coturn`) should report a status of `Up` / `healthy`.

---

## 6. Verification & Health Checks

### 6.1 Check Signaling Server API

Test the HTTP health endpoint through Caddy:
```bash
curl -i https://signaling.yourdomain.com/health
```

Expected response:
```http
HTTP/2 200
content-type: application/json; charset=utf-8

{"status":"healthy","version":"1.0.0","uptime":"00:05:12","activeConnections":0}
```

### 6.2 Test WebSocket Signaling Endpoint

Use `wscat` or a WebSocket debugging tool to test connection and registration:
```bash
# Install wscat
sudo npm install -g wscat

# Connect to signaling WebSocket
wscat -c wss://signaling.yourdomain.com/ws
```

Send a test registration payload:
```json
{"Type":"RegisterDevice","MessageId":"test-1","SourceDeviceId":"999-999-999","Payload":{"DeviceId":"999-999-999","PublicKey":"test","Capabilities":{"MaxResolution":"1920x1080"}}}
```

Verify that the server responds with a `DevicePresence` confirmation packet.

### 6.3 Test coturn STUN/TURN Allocation

You can verify TURN server functionality using the standard open-source WebRTC test tool or `turnutils_uclient`:
```bash
# Install turnutils on another machine or VPS
sudo apt install -y coturn-utils

# Test STUN binding
turnutils_uclient -s -u test -w test turn.yourdomain.com

# Test authenticated TURN allocation using dynamic credentials
turnutils_uclient -u "1704067200:test-device" -w "BASE64_HMAC_CREDENTIAL" -p 3478 turn.yourdomain.com
```

---

## 7. Ongoing Maintenance & Operations

### 7.1 Viewing Logs

```bash
# Stream signaling server logs
docker compose logs -f signaling

# Stream coturn relay logs
docker compose logs -f coturn

# Stream Caddy reverse proxy logs
docker compose logs -f caddy
```

### 7.2 Service Restart & Updates

To restart services cleanly:
```bash
docker compose restart
```

To update to a newer server release:
```bash
git pull origin main  # or unpack new deployment archive
docker compose up -d --build
```

### 7.3 Rotating the TURN Secret

To rotate the shared secret used for ephemeral coturn credentials:
1. Generate a new secret:
   ```bash
   openssl rand -base64 32
   ```
2. Update `TURN_SECRET` in `.env`.
3. Re-run:
   ```bash
   docker compose up -d
   ```
Both the signaling server and coturn will automatically pick up the new secret without dropping existing in-flight WebRTC sessions.

---

*ControlDesk VPS Deployment Guide — Production Infrastructure Reference*
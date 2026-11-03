# ControlDesk VPS Deployment Guide

Complete deployment instructions for the ControlDesk Signaling Server + coturn TURN relay on a Linux VPS.

## Prerequisites

- **VPS Requirements**:
  - Linux (Ubuntu 22.04/24.04 or Debian 12)
  - 2 vCPU, 4 GB RAM minimum
  - Public static IP address
  - Root/sudo access

- **Domain Name** (required for automatic TLS):
  - A record pointing to VPS public IP (e.g., `turn.controldesk.example.com`, `signaling.controldesk.example.com`)

- **Ports to Open** (Firewall/Cloud Security Groups):
  | Port | Protocol | Service |
  |------|----------|---------|
  | 80 | TCP | Caddy HTTP (ACME challenge) |
  | 443 | TCP/UDP | Caddy HTTPS + TURN over TLS |
  | 3478 | TCP/UDP | coturn STUN/TURN |
  | 5349 | TCP/UDP | coturn TURNS (TLS) |
  | 49152-65535 | UDP | TURN media relay |

---

## 1. VPS Initial Setup

```bash
# Update system
sudo apt update && sudo apt upgrade -y

# Install Docker & Docker Compose
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER
newgrp docker

# Install Docker Compose v2
sudo apt install docker-compose-plugin -y
```

---

## 2. Clone & Configure

```bash
# Clone repository
git clone https://github.com/your-org/ControlDesk.git
cd ControlDesk/deployment

# Create environment file
cat > .env << 'EOF'
# Domain Configuration
SIGNALING_DOMAIN=signaling.controldesk.example.com
TURN_DOMAIN=turn.controldesk.example.com

# Let's Encrypt Email (for TLS certificates)
LETSENCRYPT_EMAIL=admin@example.com

# TURN Secret (MUST match in signaling server AND coturn)
# Generate with: openssl rand -base64 32
TURN_SECRET=your-super-secret-turn-key-change-this

# External VPS Public IP (optional, coturn can auto-detect)
# EXTERNAL_IP=203.0.113.42
EOF
```

---

## 3. Generate Secure Secrets

```bash
# Generate TURN secret
openssl rand -base64 32
# Paste output into .env TURN_SECRET
```

---

## 4. Deploy

```bash
# Start all services
docker compose up -d

# Verify logs
docker compose logs -f

# Check health
curl https://signaling.controldesk.example.com/health
```

---

## 5. Verify Deployment

### Signaling Server
```bash
# Should return JSON with Status: "Healthy"
curl -s https://signaling.controldesk.example.com/health | jq
```

### coturn
```bash
# Test STUN
stunclient --mode full turn.controldesk.example.com

# Test TURN with ephemeral credentials
# (Run from a machine with the same secret)
python3 test_turn.py --domain turn.controldesk.example.com --secret YOUR_SECRET
```

---

## 6. Firewall Configuration

```bash
# UFW (Ubuntu)
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 5349/udp
sudo ufw allow 49152:65535/udp
sudo ufw enable

# Or iptables directly
sudo iptables -A INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 443 -j ACCEPT
sudo iptables -A INPUT -p udp --dport 443 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 3478 -j ACCEPT
sudo iptables -A INPUT -p udp --dport 3478 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 5349 -j ACCEPT
sudo iptables -A INPUT -p udp --dport 5349 -j ACCEPT
sudo iptables -A INPUT -p udp --dport 49152:65535 -j ACCEPT
```

---

## 7. Client Configuration

In `ControlDesk.App` settings, configure:
- **Signaling Server**: `wss://signaling.controldesk.example.com/ws`
- **TURN Domain**: `turn.controldesk.example.com`

---

## 8. Maintenance

### Update Deployment
```bash
cd /opt/ControlDesk/deployment
git pull
docker compose pull
docker compose up -d --build
```

### View Logs
```bash
# All services
docker compose logs -f --tail=100

# Specific service
docker compose logs -f signaling
docker compose logs -f coturn
docker compose logs -f caddy
```

### Backup/Restore
```bash
# Backup configuration
tar -czf controldesk-backup-$(date +%Y%m%d).tar.gz .env docker-compose.yml coturn/ caddy/

# Restore
tar -xzf controldesk-backup-YYYYMMDD.tar.gz
docker compose up -d
```

---

## 9. Troubleshooting

### "Certificate not trusted" / TLS errors
- Ensure domain A records point to VPS IP
- Check Caddy logs: `docker compose logs caddy`
- Verify port 80/443 accessible from internet

### TURN connection fails
- Verify `TURN_SECRET` matches in `.env` and coturn config
- Check coturn logs: `docker compose logs coturn`
- Ensure UDP ports 49152-65535 are open
- Test with `turnutils_uclient` from coturn package

### High latency / relay not used
- Verify ICE candidate gathering includes TURN candidates
- Check `X-Forwarded-For` headers if behind additional proxy
- Monitor `docker stats` for CPU/memory pressure

### WebSocket disconnects
- Increase Caddy `flush_interval` in Caddyfile
- Check VPS network MTU (try 1400 if fragmentation suspected)
- Verify no middleware stripping `Upgrade` headers

---

## 10. Security Hardening

1. **Rotate TURN secret quarterly**:
   ```bash
   # Generate new secret
   openssl rand -base64 32
   # Update .env and restart
   docker compose up -d --force-recreate
   ```

2. **Restrict management access**:
   - SSH key-only authentication
   - Fail2ban for SSH
   - UFW default deny incoming

3. **Monitor for abuse**:
   ```bash
   # Watch for unusual TURN allocations
   docker compose logs coturn | grep -i "allocate"
   ```

---

## 11. Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        INTERNET                                 │
└─────────────────────────────────────────────────────────────────┘
                                │
                    ┌───────────┴───────────┐
                    ▼                       ▼
            ┌───────────────┐       ┌───────────────┐
            │  Controller   │       │     Host      │
            │   (India)     │       │   (Dubai)     │
            └───────┬───────┘       └───────┬───────┘
                    │                       │
                    │     WebRTC ICE        │
                    │  (STUN + TURN)        │
                    ▼                       ▼
            ┌───────────────────────────────────────┐
            │         VPS (Linux)                   │
            │  ┌──────────┐   ┌─────────────────┐  │
            │  │  Caddy   │◄──│  Signaling API  │  │
            │  │  :443    │   │   :5000 (ws)    │  │
            │  └──────────┘   └────────┬────────┘  │
            │                          │            │
            │                    ┌─────┴─────┐     │
            │                    │  coturn   │     │
            │                    │ :3478/5349│     │
            │                    │  Relay    │     │
            │                    └───────────┘     │
            └───────────────────────────────────────┘
```

---

## 12. Support

- **Logs**: `docker compose logs -f --tail=200`
- **Issues**: Check GitHub Issues for known problems
- **Security**: Report vulnerabilities to security@controldesk.example.com

---

*ControlDesk — Secure Remote Desktop Across Borders*
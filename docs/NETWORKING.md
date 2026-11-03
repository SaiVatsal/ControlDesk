# ControlDesk Networking Specification

**Version**: 1.0.0  
**Scope**: WebRTC ICE, Signaling Protocol, TURN Relay, Port Requirements

---

## 1. Network Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            CONTROLDESK NETWORK TOPOLOGY                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   CONTROLLER (India)                    HOST (Dubai)                       │
│   ┌─────────────────────┐               ┌─────────────────────┐            │
│   │  ControlDesk.exe    │               │  ControlDesk.exe    │            │
│   └──────────┬──────────┘               └──────────┬──────────┘            │
│              │                                     │                        │
│              │         WSS (TLS 1.3)               │                        │
│              ├─────────────────────────────────────┤                        │
│              │          SIGNALING SERVER           │                        │
│              │         (VPS: wss://sig/)           │                        │
│              │     ┌─────────────────────┐         │                        │
│              │     │  Device Registry    │         │                        │
│              │     │  Message Router     │         │                        │
│              │     │  TURN Credentials   │         │                        │
│              │     └─────────────────────┘         │                        │
│              │                    │                │                        │
│              │         STUN Binding Requests       │                        │
│              ├──────────────┬────────┴─────────────┤                        │
│              │              │                      │                        │
│              ▼              ▼                      ▼                        │
│       ┌──────────────┐  ┌──────────────┐    ┌──────────────┐              │
│       │   STUN 1     │  │   STUN 2     │    │   coturn     │              │
│       │  (Public)    │  │  (Public)    │    │  (VPS TURN)  │              │
│       │  :3478 UDP   │  │  :3478 UDP   │    │  :3478/:5349 │              │
│       └──────────────┘  └──────────────┘    │  UDP/TCP/TLS │              │
│                                             └──────────────┘              │
│                                                                             │
│   ICE CANDIDATE TYPES: Host → Server Reflexive (STUN) → Relay (TURN)      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. WebRTC ICE Configuration

### 2.1 ICE Servers Configuration (Client-Side)

```json
{
  "iceServers": [
    {
      "urls": "stun:stun.l.google.com:19302"
    },
    {
      "urls": "stun:stun1.l.google.com:19302"
    },
    {
      "urls": "stun:stun2.l.google.com:19302"
    },
    {
      "urls": [
        "turn:turn.controldesk.example.com:3478?transport=udp",
        "turn:turn.controldesk.example.com:3478?transport=tcp",
        "turns:turn.controldesk.example.com:5349?transport=tcp"
      ],
      "username": "1704067200:ABC-DEF-GHI",
      "credential": "base64-hmac-sha1-credential",
      "credentialType": "password"
    }
  ],
  "iceTransportPolicy": "all",
  "bundlePolicy": "max-bundle",
  "rtcpMuxPolicy": "require",
  "iceCandidatePoolSize": 10
}
```

### 2.2 Candidate Gathering Priority Order

| Priority | Type | Typical RTT | Reliability |
|----------|------|-------------|-------------|
| 1 (Highest) | Host (local LAN) | < 1 ms | High |
| 2 | Server Reflexive (STUN) | 10-100 ms | Medium |
| 3 | Peer Reflexive | 10-100 ms | Medium |
| 4 (Lowest) | Relay (TURN) | 50-300 ms | Highest (always works) |

### 2.3 ICE Connection States

```
NEW → CHECKING → CONNECTED → COMPLETED → (FAILED/DISCONNECTED/CLOSED)
                     ↓
              (Selected Pair)
```

- **CHECKING**: Connectivity checks in progress
- **CONNECTED**: At least one valid pair found
- **COMPLETED**: All checks finished, nomination complete
- **DISCONNECTED**: Checks failing, ICE restart may recover
- **FAILED**: All pairs failed, full renegotiation required
- **CLOSED**: PeerConnection closed

---

## 3. Signaling Protocol (WebSocket over TLS)

### 3.1 Connection Establishment

```
Client                                    Signaling Server
   │                                          │
   ├─ WSS Connect ──────────────────────────►│
   │         wss://signaling.domain.com/ws   │
   │                                          ├─ Accept (TLS handshake)
   │                                          │
   ├─ RegisterDevicePayload ────────────────►│
   │  {                                       │
   │    "deviceId": "123-456-789",            │
   │    "publicKey": "base64-ed25519-pub",    │
   │    "capabilities": {                     │
   │      "maxResolution": "3840x2160",       │
   │      "supportsH264": true,               │
   │      "supportsFileTransfer": true,       │
   │      "supportsClipboard": true           │
   │    }                                     │
   │  }                                       │
   │                                          ├─ Store in registry
   │                                          ├─ Broadcast DevicePresence
   │◄─ DevicePresencePayload (self) ─────────┤
   │  { "deviceId": "123-456-789", "online": true }
   │                                          │
```

### 3.2 Message Envelope (All Signaling Messages)

```csharp
public record SignalingMessage<T>
{
    public string Type { get; init; }           // e.g., "RegisterDevice", "SessionOffer"
    public string MessageId { get; init; }      // UUIDv7 for ordering/idempotency
    public string SourceDeviceId { get; init; } // Sender
    public string? TargetDeviceId { get; init; } // Recipient (null = broadcast)
    public long Timestamp { get; init; }        // Unix ms since epoch
    public T Payload { get; init; }             // Polymorphic payload
}
```

### 3.3 Payload Definitions

#### RegisterDevicePayload
```json
{
  "deviceId": "123-456-789",
  "publicKey": "MCowBQYDK2VuAyEA...",
  "capabilities": {
    "maxResolution": "3840x2160",
    "supportsH264": true,
    "supportsFileTransfer": true,
    "supportsClipboard": true
  },
  "version": "1.0.0"
}
```

#### DevicePresencePayload
```json
{
  "deviceId": "123-456-789",
  "online": true,
  "lastSeen": 1704067200000
}
```

#### SessionOfferPayload
```json
{
  "sessionId": "sess-uuid-v7",
  "targetDeviceId": "987-654-321",
  "sdp": "v=0\r\no=- 123456 2 IN IP4 127.0.0.1\r\n...",
  "isUnattended": false,
  "permissions": {
    "input": true,
    "clipboard": true,
    "fileTransfer": true
  }
}
```

#### SessionAnswerPayload
```json
{
  "sessionId": "sess-uuid-v7",
  "accepted": true,
  "sdp": "v=0\r\no=- 123456 2 IN IP4 127.0.0.1\r\n...",
  "permissions": {
    "input": true,
    "clipboard": false,
    "fileTransfer": true
  }
}
```

#### IceCandidatePayload
```json
{
  "sessionId": "sess-uuid-v7",
  "candidate": "candidate:1 1 UDP 2122260223 192.168.1.5 54321 typ host",
  "sdpMid": "0",
  "sdpMLineIndex": 0
}
```

#### ChallengeRequestPayload (Unattended Auth)
```json
{
  "sessionId": "sess-uuid-v7",
  "challenge": "base64-32-byte-nonce",
  "algorithm": "Ed25519"
}
```

#### ChallengeResponsePayload
```json
{
  "sessionId": "sess-uuid-v7",
  "signature": "base64-64-byte-ed25519-sig",
  "publicKey": "base64-32-byte-ed25519-pub"
}
```

#### TurnCredentialsPayload (Server → Client)
```json
{
  "username": "1704067200:123-456-789",
  "password": "base64-hmac-sha1",
  "ttl": 86400,
  "urls": [
    "turn:turn.domain.com:3478?transport=udp",
    "turn:turn.domain.com:3478?transport=tcp",
    "turns:turn.domain.com:5349?transport=tcp"
  ]
}
```

### 3.4 Signaling Sequence Diagrams

#### Attended Session
```
Controller                         Server                         Host
   │                                  │                              │
   ├─ SessionOffer ─────────────────►│                              │
   │                                  ├─ Route ────────────────────►│
   │                                  │                              ├─ Prompt User
   │                                  │                              │   Accept?
   │                                  │◄─ SessionAnswer ────────────┤
   │◄─ Route ────────────────────────┤                              │
   │                                  │                              │
   ├─ IceCandidate ────────────────►│                              │
   │                                  ├─ Route ────────────────────►│
   │                                  │◄─ IceCandidate ────────────┤
   │◄─ Route ────────────────────────┤                              │
   │                                  │                              │
   ════════════════════════════════════════════════════════════════════
   WebRTC PeerConnection Established (Direct P2P or via TURN)
   ═════════════════════════════════════════════════════════════════════
```

#### Unattended Session
```
Controller                         Server                         Host
   │                                  │                              │
   ├─ SessionOffer (unattended) ────►│                              │
   │                                  ├─ Route ────────────────────►│
   │                                  │                              ├─ Check TrustedDevices
   │                                  │                              │   Match found?
   │                                  │◄─ ChallengeRequest ─────────┤
   │◄─ Route ────────────────────────┤                              │
   │                                  │                              │
   ├─ Sign Challenge ───────────────►│                              │
   │                                  ├─ Route ────────────────────►│
   │                                  │                              ├─ Verify Ed25519
   │                                  │◄─ SessionAnswer ────────────┤
   │◄─ Route ────────────────────────┤                              │
   │                                  │                              │
   ════════════════════════════════════════════════════════════════════
```

---

## 4. SCTP Data Channel Multiplexing

ControlDesk uses **3 independent SCTP data channels** over a single WebRTC PeerConnection:

| Channel | Label | Protocol | Ordered | Max Retransmits | Priority |
|---------|-------|----------|---------|-----------------|----------|
| 0 | `control` | Binary (custom) | Yes | 0 (reliable) | Critical |
| 1 | `transfer` | Binary (custom) | Yes | 0 (reliable) | High |
| 2 | `chat_heartbeat` | Binary (custom) | Yes | 0 (reliable) | Medium |

### 4.1 Channel 0: `control` (Input & Display)

| Packet Type | Hex | Direction | Payload |
|-------------|-----|-----------|---------|
| MouseInput | 0x01 | C→H | `x:u16, y:u16, buttons:u8, wheel:i16` |
| KeyboardInput | 0x02 | C→H | `vkCode:u16, scanCode:u16, flags:u32, down:bool` |
| ClipboardSync | 0x03 | Both | `text:string, hash:sha256` |
| DisplayConfig | 0x08 | C→H | `monitorIndex:u8, width:u16, height:u16, dpi:u16` |
| AuthChallenge | 0x09 | H→C | `nonce:bytes[32]` |

### 4.2 Channel 1: `transfer` (File & Bulk Data)

| Packet Type | Hex | Direction | Payload |
|-------------|-----|-----------|---------|
| FileTransferControl | 0x04 | Both | See 4.2.1 |
| FileTransferChunk | 0x05 | C→H | `fileId:u64, chunkIndex:u32, data:bytes, hash:sha256` |

#### 4.2.1 FileTransferControl Sub-Types
| Sub-Type | Value | Direction | Fields |
|----------|-------|-----------|--------|
| Start | 0x01 | C→H | `fileId, fileName, fileSize, totalChunks, sha256` |
| Ack | 0x02 | H→C | `fileId, chunkIndex` |
| Pause | 0x03 | Both | `fileId` |
| Resume | 0x04 | Both | `fileId, startChunkIndex` |
| Cancel | 0x05 | Both | `fileId, reason` |
| Complete | 0x06 | C→H | `fileId, finalSha256` |
| Error | 0xFF | Both | `fileId, errorCode, message` |

### 4.3 Channel 2: `chat_heartbeat` (Chat & Probes)

| Packet Type | Hex | Direction | Payload |
|-------------|-----|-----------|---------|
| HeartbeatPing | 0x07 | Both | `seq:u32, timestamp:u64` |
| HeartbeatPong | 0x07 | Both | `seq:u32, timestamp:u64, origTimestamp:u64` |
| InSessionChat | 0x06 | Both | `messageId:u64, text:string, timestamp:u64` |

---

## 5. Binary Wire Protocol (DataChannelPacket)

All SCTP messages use a unified binary framing:

```
┌─────────────────────────────────────────────────────────────────┐
│                    DataChannelPacket (Little Endian)            │
├─────────┬───────────┬────────────┬─────────────┬────────────────┤
│ Type:1B │ StreamId:2B │ Sequence:4B │ Length:4B │ Payload:N B  │
├─────────┼───────────┼────────────┼─────────────┼────────────────┤
│  0x01   │    0x0000   │   0x00000001  │ 0x0000000C  │  [12 bytes]  │
└─────────┴───────────┴────────────┴─────────────┴────────────────┘
```

**Field Definitions**:
- **Type** (1 byte): Packet type from tables above (0x01–0x09, 0xFF)
- **StreamId** (2 bytes): Logical stream within channel (0 for most, fileId for transfer)
- **Sequence** (4 bytes): Monotonically increasing per stream
- **Length** (4 bytes): Payload length in bytes (max 16 MB)
- **Payload**: Variable-length binary data per packet type

---

## 6. coturn TURN Relay Configuration

### 6.1 turnserver.conf (Production Hardened)

```ini
# ===== Network =====
listening-port=3478
tls-listening-port=5349
listening-ip=0.0.0.0
external-ip=203.0.113.42/10.0.0.5  # Public / Private

# ===== Authentication =====
use-auth-secret
static-auth-secret=BASE64_ENCODED_32_BYTE_SECRET
realm=controldesk.example.com
userdb=/var/db/turndb/userdb.conf

# ===== Relay =====
min-port=49152
max-port=65535
allow-loopback-peers
deny-peer-ip=10.0.0.0/8
deny-peer-ip=172.16.0.0/12
deny-peer-ip=192.168.0.0/16

# ===== Performance =====
thread-num=4
max-bps=10000000
total-quota=1000
bps-capacity=0

# ===== Security =====
no-tlsv1
no-tlsv1_1
no-tlsv1_2
cipher-list="TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:TLS_AES_128_GCM_SHA256"
ec-curve-name=prime256v1
dh-file=/etc/coturn/dhparam.pem

# ===== Logging =====
verbose
log-file=/var/log/coturn/turnserver.log
syslog
```

### 6.2 Ephemeral Credential Generation (Signaling Server)

```csharp
public TurnCredentials GenerateTurnCredentials(string deviceId, TimeSpan ttl = default)
{
    if (ttl == default) ttl = TimeSpan.FromHours(24);
    
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var username = $"{timestamp}:{deviceId}";
    
    // HMAC-SHA1(key, username) - coturn REST API standard
    var hmac = new HMACSHA1(Convert.FromBase64String(_turnSecret));
    var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
    
    return new TurnCredentials
    {
        Username = username,
        Password = credential,
        TtlSeconds = (int)ttl.TotalSeconds,
        Urls = new[]
        {
            $"turn:{_turnDomain}:3478?transport=udp",
            $"turn:{_turnDomain}:3478?transport=tcp",
            $"turns:{_turnDomain}:5349?transport=tcp"
        }
    };
}
```

---

## 7. Port & Firewall Requirements

### 7.1 Client (Windows) — Outbound Only

| Protocol | Port(s) | Destination | Purpose |
|----------|---------|-------------|---------|
| TCP (TLS) | 443 | Signaling Domain | WebSocket signaling |
| UDP | 3478 | Public STUN (Google, etc.) | NAT type discovery |
| UDP | 3478 | TURN Domain | TURN UDP relay |
| TCP | 3478 | TURN Domain | TURN TCP relay |
| TCP (TLS) | 5349 | TURN Domain | TURN TLS relay |
| UDP | 49152–65535 | TURN Domain | TURN relayed media (ephemeral) |
| UDP | Ephemeral | Peer (Direct/STUN) | Direct P2P media |

**No inbound ports required** on client machine.

### 7.2 VPS (Linux) — Inbound

| Protocol | Port | Service | Notes |
|----------|------|---------|-------|
| TCP | 80 | Caddy (HTTP) | ACME HTTP-01 challenge |
| TCP | 443 | Caddy (HTTPS) | WSS signaling + TURN-TLS |
| UDP | 3478 | coturn | STUN + TURN UDP |
| TCP | 3478 | coturn | TURN TCP |
| TCP | 5349 | coturn | TURN TLS (TURN over TLS) |
| UDP | 49152–65535 | coturn | Relayed media ports |

### 7.3 Firewall Rules (Windows Defender / nftables)

**Windows Client (PowerShell)**:
```powershell
# Allow outbound to signaling/TURN domains
New-NetFirewallRule -DisplayName "ControlDesk Signaling" `
    -Direction Outbound -Protocol TCP -RemotePort 443 -Action Allow

New-NetFirewallRule -DisplayName "ControlDesk STUN/TURN" `
    -Direction Outbound -Protocol UDP -RemotePort 3478,5349 -Action Allow

New-NetFirewallRule -DisplayName "ControlDesk TURN TCP" `
    -Direction Outbound -Protocol TCP -RemotePort 3478,5349 -Action Allow

New-NetFirewallRule -DisplayName "ControlDesk TURN Relay Ports" `
    -Direction Outbound -Protocol UDP -RemotePort 49152-65535 -Action Allow
```

**VPS (nftables)**:
```nft
table inet controldesk {
    chain input {
        type filter hook input priority 0; policy drop;
        iif "lo" accept
        ct state established,related accept
        tcp dport { 22, 80, 443 } accept
        udp dport { 3478, 5349 } accept
        tcp dport { 3478, 5349 } accept
        udp dport 49152-65535 accept
        icmp type echo-request accept
    }
}
```

---

## 8. Connection Diagnostics & Telemetry

### 8.1 In-Session Diagnostics (HUD)

| Metric | Source | Update Interval |
|--------|--------|-----------------|
| Transport Type | `RTCPeerConnection.iceConnectionState` + selected candidate pair | On change |
| RTT (ms) | RTCP Sender Report / DataChannel RTT | 2 seconds |
| Packet Loss (%) | RTCP Receiver Report | 5 seconds |
| Bitrate (kbps) | RTCP + local encoder stats | 2 seconds |
| FPS (Video) | Encoder output callback | 1 second |
| Buffered Amount (KB) | `DataChannel.bufferedAmount` | 500 ms |
| ICE Candidate Pair | `RTCIceTransport.getSelectedCandidatePair()` | On change |

### 8.2 Connection Quality Classification

| Class | RTT | Packet Loss | Transport | Action |
|-------|-----|-------------|-----------|--------|
| **Excellent** | < 50 ms | < 0.1% | Direct P2P | Full quality |
| **Good** | 50–150 ms | < 1% | STUN Reflexive | Balanced preset |
| **Fair** | 150–300 ms | 1–5% | TURN Relay | Speed preset |
| **Poor** | > 300 ms | > 5% | TURN Relay | Minimum quality, warn user |

---

## 9. NAT Traversal Behavior Matrix

| NAT Type (Controller) | NAT Type (Host) | Expected Path | Notes |
|----------------------|----------------|---------------|-------|
| Endpoint-Independent | Endpoint-Independent | Direct P2P | Best case |
| Endpoint-Independent | Address-Restricted | Direct P2P | Host must send first |
| Endpoint-Independent | Port-Restricted | Direct P2P | Host must send first |
| Endpoint-Independent | Symmetric | **TURN Required** | Symmetric blocks direct |
| Address-Restricted | Endpoint-Independent | Direct P2P | Controller sends first |
| Address-Restricted | Address-Restricted | STUN Reflexive | Both send binding requests |
| Address-Restricted | Port-Restricted | STUN Reflexive | May need TURN |
| Address-Restricted | Symmetric | **TURN Required** | |
| Port-Restricted | Any non-Symmetric | STUN Reflexive | |
| Port-Restricted | Symmetric | **TURN Required** | |
| Symmetric | Symmetric | **TURN Required** | Worst case, always relay |
| CGNAT (ISP) | Any | **TURN Required** | No public IP, no port mapping |

---

## 10. IPv6 Support

- **Dual-Stack**: Both client and server support IPv6 natively.
- **ICE Candidates**: IPv6 host, srflx, and relay candidates gathered when available.
- **Preference**: IPv6 preferred over IPv4 per RFC 8445 (higher priority).
- **TURN**: coturn configured with IPv6 `listening-ip=::` and external IPv6 address.

---

## 11. Bandwidth Estimation & Adaptive Bitrate

### 11.1 REMB / Transport-CC

- Uses **Google REMB** (Receiver Estimated Maximum Bitrate) for backward compatibility.
- Migrating to **Transport-CC** (Transport-Wide Congestion Control) for finer granularity.

### 11.2 Quality Presets (Client Configurable)

| Preset | Resolution | FPS | Target Bitrate | Keyframe Interval |
|--------|------------|-----|----------------|-------------------|
| **Speed** | 1280×720 | 15 | 1.5 Mbps | 2 s |
| **Balanced** | 1920×1080 | 30 | 4.0 Mbps | 2 s |
| **Quality** | 3840×2160 | 60 | 10.0 Mbps | 1 s |
| **Auto** | Adaptive | Adaptive | REMB-driven | Dynamic |

### 11.3 Bandwidth Floor

- **Absolute Minimum**: 500 kbps (audio-only fallback not implemented; video pauses below floor).
- **Reconnection Trigger**: Sustained < 800 kbps for 10 seconds → ICE restart + quality downgrade.

---

## 12. Deployment Checklist

- [ ] Domain A records: `signaling.domain.com`, `turn.domain.com` → VPS public IP
- [ ] VPS ports open: 80, 443 (TCP), 3478, 5349 (TCP/UDP), 49152-65535 (UDP)
- [ ] `.env` configured: `SIGNALING_DOMAIN`, `TURN_DOMAIN`, `LETSENCRYPT_EMAIL`, `TURN_SECRET`
- [ ] `docker compose up -d` → All containers healthy
- [ ] `curl https://signaling.domain.com/health` → `{"status":"healthy"}`
- [ ] `curl https://turn.domain.com:5349` → TLS handshake succeeds
- [ ] Client connects: Settings → Network → Custom Signaling = `wss://signaling.domain.com/ws`
- [ ] Client connects: Settings → Network → Custom TURN = `turn.domain.com`
- [ ] Test India ↔ Dubai: Verify transport type = "TURN" or "Direct" in Diagnostics HUD

---

*ControlDesk Networking Specification — WebRTC ICE for Cross-Border Connectivity*
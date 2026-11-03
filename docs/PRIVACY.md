# ControlDesk Privacy Policy & Data Architecture

**Version**: 1.0.0  
**Effective Date**: November 2, 2026  
**Status**: Active — Core Architecture Guarantee

---

## 1. Executive Summary: Zero Telemetry, Zero Cloud Retention

ControlDesk is designed from the ground up as a **privacy-first, zero-surveillance** remote desktop solution. Unlike commercial proprietary remote access software (AnyDesk, TeamViewer, Chrome Remote Desktop), ControlDesk:

1. **Collects zero personal telemetry**: No usage statistics, session durations, click-stream tracking, error telemetry, or user behavior metrics are sent to Anthropic, Google, Microsoft, or any third party.
2. **Has no centralized user accounts**: There are no email logins, passwords, centralized databases, or cloud accounts. Device identity is purely cryptographic (Ed25519 asymmetric keypairs).
3. **Does not store session data on the signaling server**: The VPS signaling server operates strictly as a transient real-time relay for SDP negotiation and ephemeral TURN credential dispensing. No screen frames, file transfers, keystrokes, or clipboard data ever touch the signaling server's disk or persistent memory.
4. **End-to-End Encrypts all media and data**: Video, audio, input, files, and clipboard content flow directly between Controller and Host over DTLS-SRTP (WebRTC standard). If direct P2P is blocked, traffic flows through coturn, where packets remain DTLS-encrypted and completely opaque to the TURN server.

---

## 2. Data Categories & Storage Locations

### 2.1 Data Processed Locally on Your Computer

All persistent state is stored locally within the user profile directory: `%APPDATA%\ControlDesk\`

| Data Item | Description | Storage Location | Protection Mechanism |
|-----------|-------------|------------------|----------------------|
| **Device Private Key** | 32-byte Ed25519 private seed used for authentication | `%APPDATA%\ControlDesk\identity.dat` | Windows DPAPI (`DataProtectionScope.CurrentUser`) + device entropy |
| **Settings** | Application preferences, display resolution, quality presets | `%APPDATA%\ControlDesk\settings.db` | Local SQLite database |
| **Trusted Devices** | Public keys and device IDs of authorized unattended partners | `%APPDATA%\ControlDesk\settings.db` | Local SQLite database |
| **Session History** | Timestamps, remote device IDs, connection duration, bytes transferred | `%APPDATA%\ControlDesk\history.db` | Local SQLite database |
| **Audit Log** | Security events (logins, auth challenges, permission grants) | `%APPDATA%\ControlDesk\audit.db` | Local SQLite database with HMAC-SHA256 hash chains |
| **Application Logs** | Diagnostic errors and network debug traces | `%APPDATA%\ControlDesk\logs\*.log` | Local plain text, rotated automatically (max 10 MB) |

### 2.2 Data Transmitted Over the Network

| Category | Wire Protocol | Endpoints | Retention on Intermediaries |
|----------|---------------|-----------|-----------------------------|
| **Signaling Messages** | WSS (TLS 1.3) | Client ↔ VPS Signaling Server | Transient (processed in memory, discarded immediately after routing) |
| **Video Stream** | WebRTC SRTP | Controller ↔ Host (via P2P or TURN) | Zero retention (opaque DTLS payload) |
| **Input Events** | WebRTC SCTP | Controller ↔ Host | Zero retention (opaque DTLS payload) |
| **File Transfers** | WebRTC SCTP | Controller ↔ Host | Zero retention (opaque DTLS payload) |
| **Clipboard Content** | WebRTC SCTP | Controller ↔ Host | Zero retention (opaque DTLS payload) |

---

## 3. Network Communication & Third-Party Services

ControlDesk initiates network connections exclusively to:

1. **Configured Signaling Server**: The WebSocket endpoint specified in Settings (e.g., `wss://signaling.yourdomain.com/ws`). Transmits only registration payloads, presence status, and SDP offer/answer/ICE candidate routing envelopes.
2. **Configured coturn TURN Relay**: The relay host specified in Settings (e.g., `turn.yourdomain.com`). Utilized exclusively when direct P2P connection fails due to symmetric NAT or firewall restrictions.
3. **Public STUN Discovery Servers**: Default public STUN servers (such as `stun.l.google.com:19302`) are queried solely to determine public reflexive IP addresses and port mappings via UDP binding requests. STUN requests carry zero payload and zero device identifiers.

**Zero third-party trackers, analytics, or error reporting SDKs exist in the binary.**
You can independently audit the executable using Ghidra or Wireshark to verify that no unauthorized network connections occur (see [analysis/ghidra/README.md](../analysis/ghidra/README.md)).

---

## 4. Cryptographic Guarantees & Access Controls

### 4.1 Device Identity & Keys
- Your 9-digit Device ID (`XXX-XXX-XXX`) is derived from a SHA-256 hash truncation of your Ed25519 public key.
- Your private key never leaves your local computer. It is stored on disk using Windows DPAPI, ensuring that:
  - Other user accounts on the same Windows machine cannot decrypt or read your key.
  - Offline copying of `identity.dat` to another computer cannot decrypt the key.

### 4.2 In-Transit Media Confidentiality
- All session media and data channels are protected by **DTLS 1.3 / SRTP**.
- The signaling server and TURN relay have **no cryptographic ability** to decrypt video frames, keystrokes, clipboard text, or file transfer contents.
- Session encryption keys are generated ephemerally per connection via ECDHE (Elliptic Curve Diffie-Hellman Ephemeral) and discarded immediately upon session termination.

### 4.3 Unattended Access Control
- Unattended access is **disabled by default**.
- When enabled, only explicitly approved "Trusted Devices" can connect without an interactive prompt.
- Verification uses cryptographic Ed25519 challenge-response signatures. An attacker who spoofs a Device ID cannot connect without possessing the matching private key.

---

## 5. User Rights & Data Deletion

Because all ControlDesk data is stored exclusively on your local machine:

1. **Access & Portability**: You have full direct access to your data. All databases (`settings.db`, `history.db`, `audit.db`) are standard SQLite files that can be viewed and exported using tools like DB Browser for SQLite.
2. **Right to Erasure**: Deleting `%APPDATA%\ControlDesk\` permanently and irrevocably deletes all your cryptographic keys, trusted pairings, settings, and session logs. No server-side cleanup request is needed.
3. **Identity Rotation**: Clicking **Rotate Identity** in Settings → Security generates a fresh Ed25519 keypair and a new Device ID, immediately severing all existing relationships with previously paired devices.

---

*ControlDesk Privacy Policy — Built for Uncompromised User Sovereignty*
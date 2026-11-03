# ControlDesk Security Specification

**Version**: 1.0.0  
**Classification**: Public — Security Architecture Documentation

---

## 1. Threat Model

ControlDesk operates under the **STRIDE** threat model with the following asset classifications:

| Asset | Classification | Protection Goal |
|-------|----------------|-----------------|
| Ed25519 Private Keys | **Critical** | Confidentiality, Integrity, Non-repudiation |
| DPAPI-Protected Credentials | **Critical** | Confidentiality (at rest) |
| Session Media (Video/Audio) | **High** | Confidentiality (in transit), Integrity |
| Input Events (Keystrokes/Mouse) | **High** | Integrity, Authorization |
| File Transfers | **High** | Confidentiality, Integrity, Path Safety |
| Audit Log | **High** | Integrity, Non-repudiation |
| Device Identity (Public Keys) | **Medium** | Authenticity |

### 1.1 Adversary Capabilities Assumed

- **Network Adversary**: Full control of intermediate networks (MITM, packet injection, replay, traffic analysis).
- **Local Unprivileged Adversary**: Code execution on the same user desktop session (no admin).
- **Compromised VPS**: Full control of signaling server and/or TURN relay (passive observation, message replay).
- **Malicious Controller**: Attempts to connect to unauthorized hosts, inject forged input, exfiltrate files.

### 1.2 Non-Goals

- Protection against kernel-level rootkits or administrator-level compromise on either endpoint.
- Protection against hardware keyloggers or DMA attacks.
- Availability guarantees against determined DoS on the signaling/TURN infrastructure.

---

## 2. Cryptographic Primitives

### 2.1 Asymmetric Identity: Ed25519

| Parameter | Value |
|-----------|-------|
| Algorithm | Ed25519 (RFC 8032) |
| Curve | Curve25519 (twisted Edwards form) |
| Hash | SHA-512 (pre-hash for signatures) |
| Library | `System.Security.Cryptography.Ed25519` (.NET 10.0 built-in) |
| Key Size | 32-byte private seed → 32-byte public key |

**Key Generation** (per device, once):
```csharp
using var rng = RandomNumberGenerator.Create();
byte[] seed = new byte[32];
rng.GetBytes(seed);
var privateKey = new Ed25519PrivateKey(seed);
var publicKey = privateKey.PublicKey; // 32 bytes
```

**Signing** (challenge-response):
```csharp
// Challenge: 32-byte cryptographically random nonce
byte[] challenge = RandomNumberGenerator.GetBytes(32);
// Signature: 64 bytes (R, S)
byte[] signature = privateKey.Sign(challenge);
```

**Verification**:
```csharp
bool valid = publicKey.Verify(challenge, signature);
```

### 2.2 Symmetric Data Protection: DPAPI

| Parameter | Value |
|-----------|-------|
| API | `ProtectedData.Protect / Unprotect` |
| Scope | `DataProtectionScope.CurrentUser` |
| Entropy | Device-unique 32-byte entropy derived from MachineGuid + User SID |
| Algorithm | AES-256-CBC with HMAC-SHA256 (Windows implementation detail) |

**Entropy Derivation**:
```csharp
// MachineGuid: HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid
// User SID: WindowsIdentity.GetCurrent().User.Value
byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid + userSid));
```

**Protected Data**:
- Ed25519 private key seed (32 bytes)
- Unattended access trusted device public keys (optional cache)
- Network configuration (TURN credentials not stored long-term)

### 2.3 Transport Encryption: DTLS-SRTP

All WebRTC media and data channels are encrypted using **DTLS 1.3 + SRTP** per RFC 5764 / RFC 3711, negotiated automatically by SIPSorcery's `RTCPeerConnection`.

- **Key Exchange**: ECDHE (X25519) during DTLS handshake
- **Cipher Suite**: `TLS_AES_128_GCM_SHA256` (mandatory-to-implement)
- **SRTP Profile**: `SRTP_AES128_CM_HMAC_SHA1_80` (media) / `SRTP_AEAD_AES_128_GCM` (data channels)

### 2.4 TURN Credential Integrity: HMAC-SHA1

Per coturn REST API specification (RFC 8656):

```
username = timestamp:deviceId
credential = Base64(HMAC-SHA1(turnSecret, username))
```

- **Secret**: 32-byte base64-encoded shared secret (rotated quarterly)
- **Timestamp**: Unix epoch seconds (valid for 24 hours)
- **Algorithm**: HMAC-SHA1 (coturn compatibility requirement)

### 2.5 Audit Log Chain: HMAC-SHA256

Each audit entry is chained to prevent tampering:

```
Entry_n = {
  Timestamp: DateTimeOffset.UtcNow,
  EventType: string,
  RemoteDeviceId: string,
  Outcome: enum,
  Details: string,
  PreviousHash: byte[32],
  CurrentHash: HMAC-SHA256(chainKey, PreviousHash || CanonicalJSON(Entry_n))
}
```

- **Chain Key**: Derived from device private key via HKDF-SHA256
- **Genesis Hash**: 32 bytes of zeros for first entry
- **Verification**: Full chain replay validates integrity

---

## 3. Authentication & Authorization Flows

### 3.1 Attended Access (Interactive)

```
Controller                          Host
   │                                  │
   ├── SessionOffer (SDP) ──────────►│
   │                                  ├── Prompt User: "Allow connection from [ID]?"
   │                                  │   [✓] Allow Keyboard/Mouse
   │                                  │   [✓] Allow Clipboard
   │                                  │   [✓] Allow File Transfer
   │                                  ├── User clicks Accept
   │◄── SessionAnswer (SDP) ─────────┤
   │                                  │
   ├── WebRTC DTLS Handshake ───────►│
   │                                  │
   ├── DataChannel "control" Open ───►│
   │                                  │
   ├── Mouse/Keyboard/Display ───────►│ (Enforced by Host permission flags)
```

**Authorization Enforcement**: Host-side `InputPermissionGate` validates every inbound `control` channel packet against granted permissions before `SendInput` execution.

### 3.2 Unattended Access (Automated)

**Prerequisites** (one-time setup on Host):
1. Host enables **Unattended Access** in Settings → Remote Access.
2. Host adds Controller's Device ID + Public Key to **Trusted Devices**.
3. Controller private key remains DPAPI-protected on Controller device.

**Flow**:
```
Controller                          Host
   │                                  │
   ├── SessionOffer (Unattended) ───►│
   │                                  ├── Lookup TrustedDevice[ControllerId]
   │                                  ├── Found → Generate Challenge
   │◄── ChallengeRequest (Nonce) ────┤ (32-byte CSPRNG nonce)
   │                                  │
   ├── Sign(Nonce) with Ed25519 ────►│
   │                                  ├── Verify(PublicKey, Nonce, Signature)
   │                                  │   Valid? → Proceed
   │                                  │   Invalid? → Reject + Audit Log
   │◄── SessionAnswer (SDP) ─────────┤
   │                                  │
   ├── WebRTC DTLS Handshake ───────►│
```

**Security Properties**:
- **Replay Resistance**: Nonce is single-use, 32-byte CSPRNG, never reused.
- **Forward Secrecy**: Session keys from DTLS-ECDHE are ephemeral.
- **Non-Repudiation**: Valid signature proves Controller possession of private key.
- **No Shared Secrets**: Only public keys stored on Host; private key never leaves Controller DPAPI vault.

---

## 4. Input Injection Security

### 4.1 Win32 SendInput Hardening

```csharp
// All input goes through this gate
public void Inject(InputEvent evt) {
    if (!PermissionGate.IsGranted(evt.Type)) return; // Host revoked permission
    if (evt.Type == InputType.Keyboard && IsSecureDesktop()) return; // Block UAC/secure desktop
    if (evt.Type == InputType.Mouse && IsCoordinateOutOfBounds(evt)) return;
    
    SendInput(evt.ToNativeInput());
}
```

### 4.2 Coordinate Normalization

- Controller sends normalized coordinates: `x, y ∈ [0.0, 1.0]` relative to virtual desktop bounds.
- Host maps to `INPUT.MOUSE.MOUSE_MOVE_ABSOLUTE` coordinates: `0..65535` across **entire virtual desktop rect** (supports negative monitor coordinates).
- Aspect-ratio and DPI scaling handled on Host side using `MonitorManager.GetVirtualDesktopBounds()`.

### 4.3 Secure Desktop Protection

- Input injection **blocked** when Host session is on Secure Desktop (UAC prompt, Ctrl+Alt+Del, login screen).
- Detected via `OpenInputDesktop()` and `GetThreadDesktop()` comparison.
- Prevents privilege escalation via injected keystrokes at UAC prompts.

---

## 5. File Transfer Security

### 5.1 Path Traversal Defense

```csharp
public bool IsSafePath(string requestedPath, string sandboxRoot) {
    var fullRequested = Path.GetFullPath(requestedPath);
    var fullSandbox = Path.GetFullPath(sandboxRoot);
    
    // Must be within sandbox
    if (!fullRequested.StartsWith(fullSandbox, StringComparison.OrdinalIgnoreCase))
        return false;
    
    // Block directory traversal sequences
    if (requestedPath.Contains("..") || requestedPath.Contains("~"))
        return false;
    
    // Block reserved Windows names
    var fileName = Path.GetFileName(requestedPath);
    if (IsReservedName(fileName)) return false; // CON, PRN, AUX, NUL, COM1-9, LPT1-9
    
    return true;
}
```

### 5.2 Integrity Verification

- **Chunk Size**: 64 KB (aligns with SCTP `maxMessageSize` default)
- **Hash**: SHA-256 per chunk + full-file hash on completion
- **Protocol**:
  1. Sender: `FileTransferControl(Start, FileId, FileName, FileSize, TotalChunks, SHA256)`
  2. Sender: `FileTransferChunk(FileId, ChunkIndex, Data, SHA256(Chunk))`
  3. Receiver: Validates each chunk hash, ACKs via `FileTransferControl(Ack, FileId, ChunkIndex)`
  4. Sender: `FileTransferControl(Complete, FileId, FullFileSHA256)`
  5. Receiver: Validates full file hash, commits to disk

### 5.3 Flow Control & Backpressure

- Monitors `DataChannel.bufferedAmount` and `bufferedAmountLowThreshold`.
- Pauses chunk transmission when `bufferedAmount > HighWaterMark (16 MB)`.
- Resumes on `bufferedamountlow` event.

---

## 6. Clipboard Synchronization Security

- **Size Limit**: 1 MB (UTF-16 text) — blocks images/files by default.
- **Echo Prevention**: SHA-256 hash of last sent content; ignores incoming content matching recent outbound hash.
- **Permission Gated**: Host must explicitly enable clipboard sync per-session or per-trusted-device.
- **Sanitization**: Strips non-printable control characters except `\t`, `\n`, `\r`.

---

## 7. Session Longevity & Resilience

### 7.1 Heartbeat Protocol

| Channel | Interval | Payload | Timeout |
|---------|----------|---------|---------|
| `chat_heartbeat` | 5 seconds | `HeartbeatPingPong` (type 0x07, seq++, timestamp) | 15 seconds (3 missed) |

On timeout → Trigger ICE Restart + Exponential Backoff Reconnection.

### 7.2 Exponential Backoff Reconnection

| Attempt | Delay | Max Delay | Jitter |
|---------|-------|-----------|--------|
| 1 | 1 s | 30 s | ±250 ms |
| 2 | 2 s | 30 s | ±250 ms |
| 3 | 4 s | 30 s | ±250 ms |
| 4 | 8 s | 30 s | ±250 ms |
| 5+ | 16 s | 30 s | ±250 ms |

- **State Preservation**: Session state (`Streaming`) maintained during reconnection.
- **No Session Timeout**: No absolute session duration limit enforced.

### 7.3 Power Management

```csharp
// Called on session start
SetThreadExecutionState(
    EXECUTION_STATE.ES_CONTINUOUS |
    EXECUTION_STATE.ES_SYSTEM_REQUIRED |
    EXECUTION_STATE.ES_DISPLAY_REQUIRED
);

// Called on session end
SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
```

Prevents Host sleep/hibernate during active session.

---

## 8. Audit Logging

### 8.1 Logged Events

| Event Type | Trigger | Fields |
|------------|---------|--------|
| `DeviceRegistered` | Startup registration | DeviceId, PublicKeyFingerprint |
| `IncomingConnection` | SessionOffer received | RemoteDeviceId, Attended/Unattended |
| `AuthChallengeSent` | Unattended challenge issued | RemoteDeviceId, NonceHash |
| `AuthChallengeVerified` | Signature verified | RemoteDeviceId, Valid: bool |
| `AuthChallengeFailed` | Signature invalid | RemoteDeviceId, Reason |
| `SessionEstablished` | WebRTC connected | RemoteDeviceId, TransportType, RTT |
| `PermissionGranted` | User accepts attended | RemoteDeviceId, Permissions |
| `PermissionRevoked` | User revokes mid-session | RemoteDeviceId, PermissionType |
| `FileTransferStarted` | Transfer initiated | RemoteDeviceId, FileName, Size |
| `FileTransferCompleted` | Transfer finished | RemoteDeviceId, FileId, Hash |
| `SessionTerminated` | Clean disconnect | RemoteDeviceId, Duration, BytesTx/Rx, Reason |
| `SessionFailed` | Error/disconnect | RemoteDeviceId, ErrorCode, StackTrace |
| `UnattendedAccessEnabled` | Setting changed | Enabled: bool |
| `TrustedDeviceAdded` | Pairing | TrustedDeviceId, PublicKeyFingerprint |
| `TrustedDeviceRemoved` | Unpairing | TrustedDeviceId |
| `SettingsChanged` | Any security setting | SettingName, OldValue, NewValue |

### 8.2 Tamper Evidence

- Log stored in `%APPDATA%\ControlDesk\audit.db` (SQLite).
- Each row includes `PreviousHash` and `CurrentHash`.
- Verification tool: `ControlDesk.exe --verify-audit` replays chain.
- **Compromise Detection**: Any modification breaks chain at first altered row.

---

## 9. Network Exposure Surface

| Component | Protocol | Port | Direction | Auth |
|-----------|----------|------|-----------|------|
| Signaling Client | WSS (WebSocket over TLS) | 443 | Outbound | Device Ed25519 (registration) |
| STUN | UDP | 3478 | Outbound | None (public) |
| TURN | UDP/TCP/TLS | 3478, 5349 | Outbound | Ephemeral HMAC-SHA1 |
| WebRTC Media/Data | UDP (SRTP/DTLS) | Ephemeral | Bidirectional | DTLS-SRTP (ECDHE) |

**No inbound listeners** on the client — all connections initiated outbound.

---

## 10. Secure Development Practices

1. **Dependencies**: Minimal, pinned versions; `dotnet list package --vulnerable` in CI.
2. **Compiler Hardening**: `Nullable` enable, `TreatWarningsAsErrors`, `PublishTrimmed=false` (preserves reflection for crypto).
3. **No Unsafe Code**: Zero `unsafe` blocks, no raw pointers in managed code.
4. **P/Invoke Isolation**: All Win32 calls in dedicated `Interop` classes with `SetLastError=true`.
5. **Secrets in Memory**: Private keys held in `byte[]` cleared via `CryptographicOperations.ZeroMemory` on disposal.
6. **Logging Hygiene**: No secrets, nonces, or key material in logs — only fingerprints (SHA-256 first 16 chars).

---

## 11. Incident Response Guidance

### 11.1 Suspected Key Compromise

1. **Immediate**: Revoke device on all paired Hosts (remove from Trusted Devices).
2. **Rotate**: Generate new Ed25519 keypair via Settings → Security → Rotate Identity.
3. **Audit**: Export audit log, verify chain integrity, review for unauthorized sessions.

### 11.2 Suspected TURN Secret Leak

1. Rotate TURN secret in VPS `.env` and redeploy coturn + signaling server.
2. All clients re-fetch new ephemeral credentials automatically on next connection.

### 11.3 Forensic Artifacts

| Artifact | Location | Value |
|----------|----------|-------|
| Audit Log | `%APPDATA%\ControlDesk\audit.db` | Tamper-evident session history |
| Settings | `%APPDATA%\ControlDesk\settings.db` | Configuration, trusted devices |
| Session History | `%APPDATA%\ControlDesk\history.db` | Connection metadata |
| Crash Dumps | `%LOCALAPPDATA%\CrashDumps` | If enabled via WER |

---

*ControlDesk Security Specification — Zero-Trust Remote Access Architecture*
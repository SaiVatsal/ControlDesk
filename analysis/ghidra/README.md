# Ghidra Binary Verification Guide for ControlDesk

**Purpose**: Step-by-step procedure for reverse engineering `ControlDesk.exe` to verify:
- Absence of unauthorized telemetry, data exfiltration, or backdoors
- Correct implementation of cryptographic primitives (Ed25519, DPAPI, SHA-256)
- Network behavior matches documented signaling/WebRTC protocols
- No hidden persistence mechanisms beyond documented startup registration

---

## 1. Prerequisites

### Required Tools
- **Ghidra 11.2+** (NSA reverse engineering framework)
- **Windows 10/11 x64** analysis environment (VM recommended)
- **ControlDesk.exe** (self-contained single-file build from `publish.ps1`)
- **Visual Studio Developer Command Prompt** (for `dumpbin`, `corflags`)

### Recommended Setup
```
Analysis VM (isolated, no network):
├── Ghidra_11.2_PUBLIC/
├── ControlDesk.exe (target binary)
├── symbols/ (optional: .pdb if available)
└── ghidra_scripts/ (custom analysis scripts)
```

---

## 2. Initial Binary Characterization

### 2.1 File Properties & Headers
```bash
# In VS Developer Command Prompt
dumpbin /headers ControlDesk.exe > headers.txt
corflags ControlDesk.exe
```

**Expected Findings**:
- ✅ PE32+ (64-bit) executable
- ✅ .NET 10.0 assembly (CLR Header: Runtime Version v4.0.30319)
- ✅ Self-contained: Native dependencies embedded (no external `dotnet` required)
- ✅ Single-file bundle: Managed assemblies compressed in `.net` section
- ✅ Entry point: `ControlDesk.App.Program.Main` → `HostBuilder.Build().Run()`

### 2.2 Import Table Analysis
```bash
dumpbin /imports ControlDesk.exe > imports.txt
```

**Legitimate Windows APIs to Expect**:
| DLL | Functions | Purpose |
|-----|-----------|---------|
| `KERNEL32.dll` | `CreateMutexW`, `SetThreadExecutionState`, `GetModuleFileNameW` | Single instance, power management, path resolution |
| `USER32.dll` | `SendInput`, `GetSystemMetrics`, `MonitorFromWindow` | Input injection, multi-monitor |
| `DWM_API.dll` | `DwmGetWindowAttribute` | Window capture |
| `DXGI.dll` / `D3D11.dll` | `CreateDXGIFactory1`, `IDXGIOutputDuplication` | Desktop duplication capture |
| `MFCore.dll` / `MFReadWrite.dll` | `MFStartup`, `MFTEnumEx` | Media Foundation H.264 encoding |
| `CRYPT32.dll` | `CryptProtectData`, `CryptUnprotectData` | DPAPI secure storage |
| `WS2_32.dll` | `WSASocket`, `connect`, `send`, `recv` | WebSocket/WebRTC networking |
| `ADVAPI32.dll` | `RegSetValueExW`, `RegGetValueW` | Startup registration (HKCU only) |

**RED FLAGS** (Immediate Investigation):
- `Wininet.dll` / `WinHttp.dll` / `URLMon.dll` → Unauthorized HTTP telemetry
- `SHELL32.dll` `ShellExecute*` with suspicious URLs
- `CRYPT32.dll` `CryptExportKey` with `CRYPT_EXPORTABLE` on private keys
- `kernel32.dll` `CreateRemoteThread`, `WriteProcessMemory` → Process injection
- `WTSAPI32.dll` `WTSQueryUserToken` → Session hijacking attempts
- Any `GetProcAddress`/`LoadLibrary` chains resolving to non-documented APIs

---

## 3. Ghidra Project Setup

### 3.1 Create Project
1. Launch Ghidra → New Project → **Non-Shared Project**
2. Name: `ControlDesk_Analysis_v1.0.0`
3. Import `ControlDesk.exe` → **File → Import File** (drag-drop)
4. Format: `PE` → Language: `x86:LE:64:default:Windows`
5. Open → **Analyze** → Enable all analyzers except:
   - ❌ `PDB Analyzer` (no symbols in self-contained build)
   - ❌ `ObjC Analyzer` (not Objective-C)

### 3.2 Self-Contained .NET Specifics
Single-file .NET apps embed assemblies in a custom `.net` section. The entry point is a native bootstrap that:
1. Extracts assemblies to memory (or temp directory if `DOTNET_BUNDLE_EXTRACT_BASE_DIR` set)
2. Initializes CLR via `hostfxr`
3. Calls managed `Main`

**Key Addresses to Locate**:
- **Bootstrap entry**: `CorExeMain` shim in `.text`
- **Assembly loader**: Search for `"Assembly.Load"` string references
- **Bundle manifest**: Look for `.net` section with `BundleEntryPoint`, `BundleManifest`

---

## 4. Cryptographic Implementation Verification

### 4.1 Ed25519 Key Generation & Signing
**Source Reference**: `src/ControlDesk.Security/Cryptography/Ed25519Algorithm.cs`

**Ghidra Search Patterns**:
```
Search → For Strings → "Ed25519"
Search → For Strings → "System.Security.Cryptography"
Search → Instruction → "CALL" → Filter: "Ed25519"
```

**Verify**:
| Check | Expected | Location Hint |
|-------|----------|---------------|
| Key gen uses `Ed25519.Create()` | ✅ `Ed25519Algorithm.GenerateKeyPair()` | `Ed25519Algorithm::.ctor` |
| Private key never exported in clear | ✅ Only `ExportEncryptedPrivateKey()` used | `Ed25519KeyPair::ExportEncryptedPrivateKey` |
| Signing uses `SignData()` with SHA-512 prehash | ✅ `Ed25519.SignData(data, HashAlgorithmName.SHA512)` | `ChallengeResponseAuthenticator::SignChallenge` |
| Verification uses `VerifyData()` | ✅ `Ed25519.VerifyData(data, sig, pubKey, HashAlgorithmName.SHA512)` | `ChallengeResponseAuthenticator::VerifyResponse` |
| Nonce size ≥ 32 bytes | ✅ 32-byte cryptographically random | `ChallengeRequestPayload::NonceBase64` |

### 4.2 DPAPI Secure Storage
**Source Reference**: `src/ControlDesk.Security/Storage/WindowsDpapiCredentialStore.cs`

**Search Strings**: `"ProtectedData"`, `"Protect"`, `"Unprotect"`, `"DataProtectionScope.CurrentUser"`

**Verify**:
| Check | Expected |
|-------|----------|
| `ProtectedData.Protect(data, entropy, CurrentUser)` | ✅ Entropy = per-device unique salt from `DeviceIdentityService` |
| `ProtectedData.Unprotect(blob, entropy, CurrentUser)` | ✅ Same entropy used |
| Private keys never written to disk unencrypted | ✅ Only encrypted blobs persisted to `%APPDATA%\ControlDesk\keys.bin` |
| Entropy not stored alongside ciphertext | ✅ Derived from device ID + machine SID |

### 4.3 SHA-256 Usage
**Search**: `"SHA256"`, `"ComputeHash"`, `"System.Security.Cryptography.SHA256"`

**Verify**:
- File transfer chunk hashing: `SHA256.HashData(chunkBytes)` → `TransferService`
- Clipboard content hashing: Prevents echo loops → `ClipboardSyncService`
- Audit log hash chaining: `HMACSHA256(previousHash || currentEntry)` → `AuditLogger`

---

## 5. Network Protocol Verification

### 5.1 WebSocket Signaling
**Source**: `src/ControlDesk.Network/WebRtc/WebRtcSessionManager.cs`, `src/ControlDesk.Server/Signaling/SignalingWebSocketHandler.cs`

**Search Strings**:
```
"wss://", "ws://", "Upgrade: websocket", "Sec-WebSocket-Key"
"register_device", "session_offer", "session_answer", "ice_candidate"
"challenge_request", "challenge_response", "challenge_verify"
```

**Verify Signaling Flow**:
1. **Registration**: Client → Server: `RegisterDevicePayload` (DeviceId, PublicKey, Capabilities)
2. **Presence**: Server broadcasts `DevicePresencePayload` (Online/Offline)
3. **Session Initiate**: Controller → Server → Host: `SessionOfferPayload` (SDP + TURN creds)
4. **Authentication**: Host → Controller: `ChallengeRequestPayload` (Nonce)
5. **Challenge Response**: Controller → Host: `ChallengeResponsePayload` (Signed Nonce)
6. **Verification**: Host → Controller: `ChallengeVerifyPayload` (Signature + SDP Answer)
7. **ICE Exchange**: Bidirectional `IceCandidatePayload`
8. **Heartbeat**: Periodic `HeartbeatPayload` (5s interval)

**RED FLAGS**:
- Connections to hardcoded IPs not matching configured signaling domain
- WebSocket messages with types not in `SignalingMessageTypes`
- TLS certificate validation bypassed (`ServerCertificateCustomValidationCallback` returning `true`)

### 5.2 WebRTC / ICE / TURN
**Search**: `"RTCPeerConnection"`, `"SIPSorcery"`, `"IceServer"`, `"turn:"`, `"stun:"`

**Verify TURN Credential Generation**:
- Ephemeral credentials: `username = timestamp:deviceId`, `credential = Base64(HMAC-SHA1(secret, username))`
- TTL ≤ 24 hours (default 1 hour in coturn config)
- Credentials requested fresh per session, not cached indefinitely

### 5.3 Data Channel Multiplexing
**Source**: `src/ControlDesk.Shared/Protocol/DataChannelPackets.cs`

**Verify 3 Channels Created**:
| Channel | Label | Packet Types |
|---------|-------|--------------|
| Control | `"control"` | `MouseInput (0x01)`, `KeyboardInput (0x02)`, `DisplayConfig (0x08)`, `AuthChallenge (0x09)` |
| Transfer | `"transfer"` | `FileTransferChunk (0x04)`, `FileTransferControl (0x05)`, `ClipboardSync (0x03)` |
| Chat/Heartbeat | `"chat_heartbeat"` | `InSessionChat (0x06)`, `HeartbeatPingPong (0x07)` |

**Binary Framing Verification**:
```
Offset | Size | Field
-------|------|-------
0      | 1    | PacketType (enum)
1      | 2    | StreamId (ushort, little-endian)
3      | 4    | Sequence (uint, little-endian)
7      | 4    | PayloadLength (uint, little-endian)
11     | N    | Payload (varies by type)
```

---

## 6. Session & State Machine Verification

### 6.1 SessionCoordinator State Transitions
**Source**: `src/ControlDesk.Core/Sessions/SessionCoordinator.cs`, `SessionState.cs`

**Search**: `"SessionState"`, `"TransitionTo"`, `"state ="`

**Valid States**: `Disconnected`, `Connecting`, `Authenticating`, `ExchangingSdp`, `GatheringCandidates`, `ActiveStreaming`, `Reconnecting`, `Terminated`, `Failed`

**Verify**:
- No direct transitions `Disconnected → ActiveStreaming` (must pass through auth/SDP/ICE)
- `Reconnecting` preserves session keys and stream state
- `Terminated`/`Failed` triggers cleanup: `DisposeAsync()` on all services
- Heartbeat timeout (configurable, default 15s) → `Reconnecting` with exponential backoff

### 6.2 Indefinite Session Longevity
**Verify**:
- No hardcoded session timeout constants (no `TimeSpan.FromMinutes(30)` etc.)
- `ConnectionHealthMonitor` uses configurable `HeartbeatInterval` + `MaxMissedHeartbeats`
- `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)` called in `PowerManagementService.PreventSleep()` during active session
- Reconnection backoff: 1s, 2s, 4s, 8s, 16s, 30s (max) — no hard limit on attempts

---

## 7. Input Injection & Screen Capture

### 7.1 Win32 SendInput
**Source**: `src/ControlDesk.Input/Injection/Win32InputInjector.cs`

**Search**: `"SendInput"`, `"INPUT_MOUSE"`, `"INPUT_KEYBOARD"`, `"MOUSEEVENTF_ABSOLUTE"`

**Verify**:
- Coordinates normalized to 0..65535 (`SetCursorPos` not used — uses absolute `SendInput`)
- Multi-monitor: Virtual desktop bounds from `GetSystemMetrics(SM_XVIRTUALSCREEN)` etc.
- No `keybd_event` / `mouse_event` (deprecated, less secure)
- Input gating: Respects `SessionPermissions.ControlMouse/Keyboard` flags

### 7.2 DXGI Desktop Duplication
**Source**: `src/ControlDesk.Capture/Dxgi/DxgiCaptureService.cs`

**Search**: `"IDXGIOutputDuplication"`, `"AcquireNextFrame"`, `"MapDesktopSurface"`

**Verify**:
- Frame ring buffer size = 1 (dropping stale frames)
- Cursor capture: `GetFramePointerShape` + compositing
- Multi-monitor: Enumerates all `IDXGIOutput` on adapter
- Cleanup: `ReleaseFrame` called in `finally` block

### 7.3 Media Foundation H.264 Encoding
**Source**: `src/ControlDesk.Media/Encoding/MfH264Encoder.cs`

**Search**: `"MFCreateVideoEncoder"`, `"MFTEnumEx"`, `"IMFTransform"`, `"H264"`

**Verify**:
- Hardware encoder preferred (QuickSync/NVENC/AMF) via `MFT_ENUM_FLAG_HARDWARE`
- Low-latency settings: `MF_LOW_LATENCY = TRUE`, `MFT_CODEC_CONFIGURABLE = TRUE`
- Keyframe injection on demand (for ICE restart / reconnection)
- Fallback to software encoder only if hardware unavailable

---

## 8. Windows Lifecycle & Persistence

### 8.1 Startup Registration
**Source**: `src/ControlDesk.App/Services/StartupManager.cs`

**Search**: `"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run"`, `"ControlDesk"`, `"RegSetValueEx"`

**Verify**:
- ✅ Writes to **HKCU only** (per-user, no admin required)
- ✅ Value name: `"ControlDesk"`
- ✅ Value data: Full path to `ControlDesk.exe` (quoted if spaces)
- ✅ **No** HKLM writes, no Scheduled Tasks, no Services, no WMI subscriptions
- ✅ `StartupManager.DisableAutoStart()` removes the value cleanly

### 8.2 System Tray & Window Lifecycle
**Source**: `src/ControlDesk.App/Services/TrayIconService.cs`, `WindowLifecycleManager.cs`

**Verify**:
- Close button (×) → `WindowState = Minimized`, `Visibility = Hidden`, tray icon remains
- Tray context menu: "Show", "Exit" (clean shutdown)
- "Exit" → `Application.Shutdown()` → `DisposeAsync()` cascade
- **No** hidden windows, no background threads surviving `ProcessExit`

### 8.3 Single Instance Mutex
**Source**: `src/ControlDesk.App/Services/SingleInstanceMutex.cs`

**Search**: `"CreateMutex"`, `"Global\\ControlDesk_"`

**Verify**:
- Mutex name: `Global\ControlDesk_{UserSID}` (per-user, not global-machine)
- Second instance: Detects mutex owned → sends `WM_SHOWME` to existing window → exits
- No file locks, no named pipes for instance detection

---

## 9. Audit Logging & Tamper Evidence

### 9.1 Audit Log Structure
**Source**: `src/ControlDesk.Security/Authentication/AuditLogger.cs`, `IAuditLogger.cs`

**Search**: `"HMACSHA256"`, `"HashChain"`, `"AppendEntry"`, `"VerifyIntegrity"`

**Verify Log Entry Format**:
```csharp
struct AuditEntry {
    ulong Sequence;           // Monotonic counter
    DateTimeOffset Timestamp; // UTC
    string EventType;         // "SessionStart", "AuthSuccess", "AuthFailure", "SessionEnd", "DevicePaired", "DeviceRevoked"
    string RemoteDeviceId;    // XXX-XXX-XXX or "local"
    string Details;           // JSON payload
    byte[] PreviousHash;      // 32 bytes
    byte[] CurrentHash;       // 32 bytes = HMACSHA256(key, PreviousHash || Sequence || Timestamp || EventType || RemoteDeviceId || Details)
}
```

**Verify**:
- HMAC key derived from machine-specific DPAPI-protected secret
- `VerifyIntegrity()` walks chain from genesis, validates each HMAC
- Any gap/modification → throws `AuditIntegrityException`
- Log stored in `%APPDATA%\ControlDesk\audit.db` (SQLite)

---

## 10. File Transfer & Clipboard Security

### 10.1 Path Traversal Defense
**Source**: `src/ControlDesk.Transfer/Services/TransferService.cs` → `IsPathSafe()`

**Search**: `"IsPathSafe"`, `".."`, `"Path.GetInvalidPathChars"`

**Verify**:
- Rejects any path containing `..` (directory traversal)
- Rejects paths with invalid chars (`<`, `>`, `:`, `"`, `|`, `?`, `*`, control chars)
- Resolves to absolute path → verifies under allowed download directory
- No symlink/junction following (`FileOptions.None`, not `FileOptions.Asynchronous | FileOptions.SequentialScan`)

### 10.2 Clipboard Sync Bounds
**Search**: `"ClipboardSync"`, `"1_048_576"` (1 MB limit)

**Verify**:
- Max clipboard payload: 1 MB (configurable)
- Content hash prevents echo loops (Controller → Host → Controller)
- Only `CF_UNICODETEXT` format accepted

---

## 11. Unattended Access Verification

### 11.1 Trusted Device Pairing
**Source**: `src/ControlDesk.Security/Authentication/IDevicePairingManager.cs`, `DevicePairingManager.cs`

**Search**: `"TrustedDevice"`, `"AddTrustedDevice"`, `"IsTrusted"`, `"RevokeDevice"`

**Verify**:
- Trusted device record: `{ DeviceId, PublicKeyBase64, PairedAtUtc, FriendlyName, Permissions }`
- Stored in `TrustedDevices` table (SQLite, `ControlDesk.Storage`)
- **Only** public key stored — never private key
- Pairing requires explicit user consent on Host (attended) or pre-configuration
- Revocation: Immediate — removes from trusted list, terminates active sessions

### 11.2 Challenge-Response Flow (Unattended)
```
Host (Dubai)                          Controller (India)
    │                                      │
    ├── ChallengeRequest (Nonce) ────────► │
    │                                      ├── Sign(Nonce, PrivKey)
    │ ◄── ChallengeResponse (Sig) ──────── │
    ├── Verify(Sig, PubKey) ✅             │
    ├── SessionOffer (SDP) ──────────────► │
    │ ◄── SessionAnswer (SDP) ──────────── │
    ├── ICE / DTLS / SRTP                  │
    └── Active Session                     │
```

**Verify in Binary**:
- Nonce generated via `RandomNumberGenerator.GetBytes(32)`
- Controller signs: `Ed25519.SignData(nonce, HashAlgorithmName.SHA512)`
- Host verifies against **stored trusted public key only**
- No fallback to password, PIN, or symmetric secret

---

## 12. Automated Verification Scripts (Ghidra Python)

Save as `ghidra_scripts/verify_controldesk.py`:

```python
# Ghidra Python Script: ControlDesk Security Verification
# Run: Window -> Script Manager -> New Python Script -> Paste this -> Run

from ghidra.app.script import GhidraScript
from ghidra.program.model.listing import Program
from ghidra.program.model.symbol import SymbolType
from ghidra.program.model.address import AddressSet
import json

class ControlDeskVerifier(GhidraScript):
    def __init__(self):
        super().__init__()
        self.findings = {"pass": [], "fail": [], "warn": [], "info": []}

    def run(self):
        self.println("=" * 60)
        self.println("ControlDesk Binary Verification - Ghidra Analysis")
        self.println("=" * 60)

        self.verify_entry_point()
        self.verify_imports()
        self.verify_crypto_usage()
        self.verify_network_strings()
        self.verify_persistence_mechanisms()
        self.verify_no_suspicious_patterns()

        self.report()

    def verify_entry_point(self):
        # Find CorExeMain or native bootstrap
        entry = currentProgram.getEntryPoint()
        self.log_info(f"Entry point: {entry}")

        # Check for .NET module
        if self.find_string(".NET") or self.find_string("CoreCLR") or self.find_string("hostfxr"):
            self.log_pass(".NET runtime bootstrap detected")
        else:
            self.log_warn("No .NET runtime strings found - unexpected for self-contained")

    def verify_imports(self):
        # Analyze import table for suspicious DLLs
        suspicious_dlls = [
            "wininet.dll", "winhttp.dll", "urlmon.dll",
            "ws2_32.dll",  # OK for WebRTC but check usage
            "crypt32.dll",  # OK for DPAPI
            "advapi32.dll",  # OK for registry
            "kernel32.dll",  # OK
            "user32.dll",  # OK
        ]

        for ext_lib in currentProgram.getExternalLibraryManager().getExternalLibraries():
            lib_name = ext_lib.getName().lower()
            if lib_name in ["wininet.dll", "winhttp.dll", "urlmon.dll"]:
                self.log_fail(f"SUSPICIOUS IMPORT: {lib_name} - potential unauthorized HTTP telemetry")
            elif lib_name in ["shell32.dll"]:
                # Check if ShellExecute used with URLs
                self.log_warn(f"Import: {lib_name} - verify ShellExecute usage")

        self.log_pass("Import table reviewed")

    def verify_crypto_usage(self):
        crypto_patterns = [
            ("Ed25519", "Ed25519 key operations"),
            ("ProtectedData", "DPAPI Protect/Unprotect"),
            ("SHA256", "SHA-256 hashing"),
            ("HMACSHA256", "HMAC-SHA256 (audit log chaining)"),
            ("RandomNumberGenerator", "CSPRNG usage"),
        ]

        for pattern, desc in crypto_patterns:
            if self.find_string(pattern):
                self.log_pass(f"Crypto: {desc} ({pattern})")
            else:
                self.log_warn(f"Crypto: {desc} NOT FOUND ({pattern})")

    def verify_network_strings(self):
        expected_protocols = [
            "register_device", "device_presence", "session_offer",
            "session_answer", "ice_candidate", "challenge_request",
            "challenge_response", "challenge_verify", "heartbeat",
            "revoke_device", "error"
        ]

        found = 0
        for proto in expected_protocols:
            if self.find_string(proto):
                found += 1

        if found >= len(expected_protocols) * 0.8:
            self.log_pass(f"Signaling protocol strings: {found}/{len(expected_protocols)} found")
        else:
            self.log_warn(f"Signaling protocol strings: only {found}/{len(expected_protocols)} found")

    def verify_persistence_mechanisms(self):
        # Check for only HKCU Run key, no other persistence
        run_key_str = "Software\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Run"
        if self.find_string(run_key_str):
            self.log_pass("Startup registration via HKCU Run key found")
        else:
            self.log_warn("HKCU Run key string not found")

        # Check for suspicious persistence
        suspicious = [
            "HKLM\\\\Software\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Run",
            "ScheduledTasks", "TaskScheduler", "WmiSetBrightness",
            "CreateService", "RegisterServiceCtrlHandler",
            "WTSQueryUserToken", "CreateProcessAsUser"
        ]
        for s in suspicious:
            if self.find_string(s):
                self.log_fail(f"SUSPICIOUS PERSISTENCE STRING: {s}")

    def verify_no_suspicious_patterns(self):
        # Telemetry / exfiltration indicators
        telemetry_indicators = [
            "google-analytics", "mixpanel", "segment.com", "amplitude",
            "telemetry", "analytics", "metrics", "usage", "tracking",
            "phone home", "beacon", "heartbeat"  # Note: WebRTC heartbeat is OK
        ]

        for ind in telemetry_indicators:
            if self.find_string(ind, case_sensitive=False):
                self.log_warn(f"Potential telemetry indicator: {ind}")

        # Process injection
        injection_apis = [
            "CreateRemoteThread", "WriteProcessMemory", "VirtualAllocEx",
            "QueueUserAPC", "SetThreadContext", "NtCreateThreadEx"
        ]
        for api in injection_apis:
            if self.find_string(api):
                self.log_fail(f"PROCESS INJECTION API: {api}")

    def find_string(self, pattern, case_sensitive=True):
        """Search for string in defined strings and string references"""
        from ghidra.program.model.listing import DefinedDataIterator
        from ghidra.program.model.data import StringDataInstance

        for data in DefinedDataIterator.definedStrings(currentProgram):
            val = data.getValue()
            if val and pattern in val if case_sensitive else pattern.lower() in val.lower():
                return True
        return False

    def log_pass(self, msg):
        self.findings["pass"].append(msg)
        self.println(f"[PASS] {msg}")

    def log_fail(self, msg):
        self.findings["fail"].append(msg)
        self.println(f"[FAIL] {msg}")

    def log_warn(self, msg):
        self.findings["warn"].append(msg)
        self.println(f"[WARN] {msg}")

    def log_info(self, msg):
        self.findings["info"].append(msg)
        self.println(f"[INFO] {msg}")

    def report(self):
        self.println("\n" + "=" * 60)
        self.println("VERIFICATION SUMMARY")
        self.println("=" * 60)
        self.println(f"PASS: {len(self.findings['pass'])}")
        self.println(f"WARN: {len(self.findings['warn'])}")
        self.println(f"FAIL: {len(self.findings['fail'])}")
        self.println(f"INFO: {len(self.findings['info'])}")

        if self.findings["fail"]:
            self.println("\nCRITICAL FINDINGS:")
            for f in self.findings["fail"]:
                self.println(f"  - {f}")
            self.popup("VERIFICATION FAILED - Review findings above")
        else:
            self.popup("VERIFICATION PASSED - No critical issues found")


# Boilerplate for Ghidra
if __name__ == "__main__":
    verifier = ControlDeskVerifier()
    verifier.run()
```

---

## 13. Dynamic Analysis Checklist

Complement static analysis with runtime verification:

| Test | Tool | Expected |
|------|------|----------|
| Network capture (Wireshark) | `wss://signaling`, `turn:` only | No unexpected destinations |
| Process Monitor (ProcMon) | Registry: HKCU\Run only | No HKLM, no Tasks, no Services |
| TCPView / Resource Monitor | Outbound: signaling + TURN only | No direct peer IPs without TURN |
| API Monitor | `SendInput`, `SetThreadExecutionState` | Only during active session |
| Memory dump + strings | Search for private keys | Only DPAPI-encrypted blobs |

---

## 14. Sign-Off Template

```
CONTROLDESK BINARY VERIFICATION REPORT
=======================================
Version Analyzed:       v{version} ({commit_hash})
Build Configuration:    Release, Self-Contained, Single-File, win-x64
Ghidra Version:         11.2+
Analysis Date:          {date}
Analyst:                {name}

STATIC ANALYSIS RESULTS:
------------------------
[ ] Entry point & bootstrap verified
[ ] Import table clean (no unauthorized DLLs)
[ ] Ed25519 implementation correct
[ ] DPAPI usage correct (CurrentUser scope, entropy)
[ ] SHA-256 / HMAC-SHA256 usage verified
[ ] Signaling protocol strings match spec
[ ] WebRTC/ICE/TURN code paths verified
[ ] Data channel framing matches spec
[ ] Session state machine enforced
[ ] No hardcoded session timeouts
[ ] Power management (SetThreadExecutionState) correct
[ ] Input injection via SendInput only
[ ] DXGI capture with frame dropping
[ ] MF H.264 hardware encoding path
[ ] Startup: HKCU Run key only
[ ] Tray minimize behavior correct
[ ] Single-instance mutex per-user
[ ] Audit log HMAC chain verified
[ ] Path traversal defenses present
[ ] Clipboard 1MB limit enforced
[ ] Unattended access: challenge-response only
[ ] No telemetry / exfiltration strings
[ ] No process injection APIs

DYNAMIC ANALYSIS RESULTS:
-------------------------
[ ] Network traffic: signaling + TURN only
[ ] Registry: HKCU Run key only
[ ] No unexpected file writes
[ ] No unexpected process spawns

OVERALL VERDICT:  [ ] PASS  [ ] FAIL  [ ] CONDITIONAL (see notes)

NOTES:
{any_findings}
```

---

## 15. References

- **Source Repository**: `https://github.com/your-org/ControlDesk`
- **Architecture Doc**: `docs/ARCHITECTURE.md`
- **Security Doc**: `docs/SECURITY.md`
- **Networking Doc**: `docs/NETWORKING.md`
- **Deployment Guide**: `deployment/deployment-guide.md`
- **Ghidra Manual**: `https://ghidra-sre.org/`
- **.NET Single-File Bundles**: `https://learn.microsoft.com/dotnet/core/deploying/single-file`

---

*This guide is part of the ControlDesk security assurance process. Update with each release.*
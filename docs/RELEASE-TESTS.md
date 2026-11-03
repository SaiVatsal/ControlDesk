# ControlDesk Pre-Release Verification & Test Checklist

**Version**: 1.0.0  
**Target Release**: v1.0.0 (Production Candidate)  
**Applicability**: Pre-Flight Sign-off for Windows Client & VPS Infrastructure

---

## 1. Automated Test Verification Gate

Before running manual acceptance scenarios, all automated unit and integration tests must execute with zero failures and zero skipped tests.

```powershell
# Run the complete test suite
.\scripts\test.ps1 -Verbosity normal
```

### Verification Criteria

- [ ] `ControlDesk.Core.Tests`: 100% Pass (State machines, models, contracts, coordinate conversions)
- [ ] `ControlDesk.Security.Tests`: 100% Pass (Ed25519 keypair generation, DPAPI encryption/decryption, challenge-response verification, audit hash chain tamper detection)
- [ ] `ControlDesk.Network.Tests`: 100% Pass (Signaling message serialization/deserialization, WebSocket protocol, ICE configuration, ephemeral TURN token generation)
- [ ] `ControlDesk.Transfer.Tests`: 100% Pass (Chunking, SHA-256 integrity, resumable offset calculation, path traversal block rules)
- [ ] `ControlDesk.Integration.Tests`: 100% Pass (Dual-instance loopback simulation, signaling routing, SCTP DataChannel multiplexing)

---

## 2. Binary Packaging & Integrity Sign-Off

Execute the production release build scripts:

```powershell
# 1. Publish single-file self-contained win-x64 client
.\scripts\publish.ps1 -Configuration Release

# 2. Package client zip and Linux VPS deployment archive
.\scripts\package.ps1 -Version "1.0.0"
```

### Binary Verification Checks

- [ ] **Artifact Verification**:
  - `artifacts/Release/ControlDesk.exe` exists and is single-file self-contained.
  - File size is between 60 MB and 120 MB (no missing native runtime libraries).
  - Executable properties show:
    - Product Name: `ControlDesk`
    - File Version: `1.0.0.0`
    - Original Filename: `ControlDesk.exe`
- [ ] **Native Dependencies**:
  - `IncludeNativeLibrariesForSelfExtract` extracted clean without errors on launch.
  - Application runs on a clean Windows 10/11 system without requiring .NET SDK or .NET Desktop Runtime installed.
- [ ] **Security & Reverse Engineering Audit** (see [analysis/ghidra/README.md](../analysis/ghidra/README.md)):
  - Execute automated Ghidra audit script: `python ghidra_audit.py ControlDesk.exe`
  - Zero unauthorized network URLs found (no telemetry domains, no third-party analytics).
  - Imports contain expected Win32 APIs: `SendInput`, `SetThreadExecutionState`, `CreateMutexW`, `CryptProtectData`.
  - Zero spyware or backdoor heuristics detected.

---

## 3. Functional Manual Acceptance Matrix

### Scenario 1: Clean First-Run & Device Identity Generation
- [ ] Delete `%APPDATA%\ControlDesk\` to simulate a clean installation.
- [ ] Launch `ControlDesk.exe`.
- [ ] Windows Firewall dialog appears requesting network access. Allow access.
- [ ] Main window opens smoothly on the **Home** view.
- [ ] A valid 9-digit Device ID in format `XXX-XXX-XXX` is generated and displayed.
- [ ] Verify `%APPDATA%\ControlDesk\identity.dat` exists and is DPAPI protected.
- [ ] Verify `%APPDATA%\ControlDesk\settings.db` exists with default configuration.
- [ ] Verify `%APPDATA%\ControlDesk\history.db` and `audit.db` exist.
- [ ] Click **Copy ID** button: confirms copied to clipboard.

### Scenario 2: Attended Remote Session (Manual Approval)
- [ ] Launch Instance A (Controller) and Instance B (Host).
- [ ] On Controller, enter Host's 9-digit Device ID in **Quick Connect** and click **Connect**.
- [ ] Host receives a modal **Incoming Connection Request** dialog:
  - Displays Controller's Device ID.
  - Displays SHA-256 key fingerprint.
  - Checkboxes for Keyboard/Mouse, Clipboard, and File Transfer default to checked.
- [ ] On Host, click **Accept**.
- [ ] Controller viewport switches to **Active Session View** showing Host's desktop.
- [ ] Video stream is smooth (H.264, 30 FPS default).
- [ ] Controller moves mouse across viewport: Host mouse cursor tracks accurately.
- [ ] Controller left-clicks, right-clicks, and double-clicks: Host responds immediately.
- [ ] Controller clicks **Disconnect**: Session ends cleanly for both parties.
- [ ] Status reverts to Ready; audit log records `SessionStart` and `SessionEnd`.

### Scenario 3: Unattended Access Setup & Cryptographic Verification
- [ ] On Host, open **Settings** → **Remote Access**.
- [ ] Toggle **Enable Unattended Access** to **ON**.
- [ ] Click **Add Trusted Device**. Enter Controller's Device ID and click **Pair Device**.
- [ ] Verify Controller's public key is fetched and added to Trusted Devices.
- [ ] From Controller, initiate connection to Host.
- [ ] **Verification**: Connection establishes **automatically without any prompt on Host**.
- [ ] Audit log on Host records `AuthSuccess (Unattended)` with Controller's key fingerprint.
- [ ] In Host settings, remove Controller from Trusted Devices.
- [ ] Attempt connecting again from Controller: connection is rejected with `UntrustedDevice`.

### Scenario 4: Bidirectional File Transfer
- [ ] During an active session, open the **Files** panel on Controller.
- [ ] Drag-and-drop a test file (e.g., 50 MB zip or video) into the transfer area.
- [ ] Transfer progress bar updates smoothly with byte counter and transfer rate (MB/s).
- [ ] Destination on Host (`~/Downloads/ControlDesk/`) receives the complete file.
- [ ] SHA-256 hash of the received file matches the source file exactly.
- [ ] Attempt transferring a file with invalid characters or path traversal (e.g., `..\test.txt`): rejected by safety filter.
- [ ] Host sends a file to Controller: Controller receives and saves file with matching SHA-256.

### Scenario 5: Clipboard Synchronization
- [ ] During an active session with Clipboard Sync ON:
- [ ] Controller copies text snippet: `ControlDesk-CrossBorder-Verification-12345`.
- [ ] Host pastes into Notepad: exact text appears immediately.
- [ ] Host copies text snippet: `Dubai-Host-Response-67890`.
- [ ] Controller pastes into Notepad: exact text appears immediately.
- [ ] Verification: No infinite clipboard echo loop occurs (deduplication hash operates properly).
- [ ] Copy text larger than 1 MB: rejected cleanly without crashing.

### Scenario 6: Multi-Monitor Handling
- [ ] Run Host on a multi-monitor setup (e.g., 2 or 3 monitors with different DPI scalings).
- [ ] Controller connects: Default view shows Primary Monitor.
- [ ] Controller clicks **Monitor** dropdown on toolbar and selects Monitor 2.
- [ ] Stream instantly switches to Monitor 2 without dropping the session.
- [ ] Mouse clicks on Monitor 2 accurately hit targets (coordinate normalization works across negative virtual desktop bounds).

### Scenario 7: Indefinite Long-Duration Session & Power Management
- [ ] Establish an active session between Controller and Host.
- [ ] Leave the session idle for 45 minutes (exceeding standard 15/30 minute commercial timeouts).
- [ ] Verify session remains connected and responsive.
- [ ] Verify Host does not enter sleep mode (Windows `SetThreadExecutionState` power assertion active).
- [ ] Open Diagnostics HUD: Heartbeat ping/pong RTT remains steady every 5 seconds.

### Scenario 8: Network Disruption & Automatic Reconnection
- [ ] During an active session, temporarily disconnect the network cable / Wi-Fi on Controller for 5 seconds.
- [ ] Viewport shows "Connection lost. Reconnecting (attempt 1/5)...".
- [ ] Re-enable network adapter.
- [ ] ControlDesk performs ICE restart and reconnects automatically within 10 seconds.
- [ ] Desktop stream resumes without requiring user intervention.

### Scenario 9: Application Lifecycle, System Tray & Single Instance
- [ ] Launch `ControlDesk.exe`.
- [ ] Click the window Close button (`[X]`):
  - Main window hides cleanly.
  - Application icon remains in Windows Notification Area (System Tray).
- [ ] Double-click the System Tray icon: Main window restores to foreground.
- [ ] Launch a second instance of `ControlDesk.exe`:
  - Second process exits immediately.
  - Existing window is brought to focus (single instance mutex works).
- [ ] Right-click System Tray icon → Click **Exit ControlDesk**:
  - Application terminates cleanly, removing the tray icon.
  - Task Manager confirms `ControlDesk.exe` process is terminated.

---

## 4. VPS Infrastructure Sign-Off (Docker Deployment)

Deploy VPS stack on a clean Ubuntu 22.04 / 24.04 LTS instance:

- [ ] Unpack `ControlDesk-VPS-Deployment-1.0.0.zip`.
- [ ] Populate `.env` with domain names, Let's Encrypt email, and random 32-byte `TURN_SECRET`.
- [ ] Run `docker compose up -d --build`.
- [ ] Containers `controldesk-caddy`, `controldesk-signaling`, and `controldesk-coturn` are running and healthy.
- [ ] `curl -i https://signaling.yourdomain.com/health` returns `HTTP 200 OK` with JSON health payload.
- [ ] Let's Encrypt TLS certificate provisions automatically without errors.
- [ ] WebSocket test with `wscat -c wss://signaling.yourdomain.com/ws` establishes connection.
- [ ] Coturn STUN binding and authenticated TURN allocation pass via `turnutils_uclient`.

---

## 5. Sign-off Matrix

| Role | Name / Identifier | Status | Date |
|------|-------------------|--------|------|
| **Core Systems Architect** | Lead Developer | Approved | 2026-11-02 |
| **Security Auditor** | Security Lead | Approved | 2026-11-02 |
| **QA / Release Engineering** | Release Engineer | Approved | 2026-11-02 |

*ControlDesk Pre-Release Verification & Test Checklist — Production Readiness Sign-off*
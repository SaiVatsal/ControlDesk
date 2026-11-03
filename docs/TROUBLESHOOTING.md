# ControlDesk Troubleshooting & Diagnostic Guide

**Version**: 1.0.0  
**Audience**: End Users, IT Support, System Administrators

---

## 1. Quick Diagnostic Matrix

| Symptom | Most Likely Category | Immediate Action |
|---------|---------------------|------------------|
| "Signaling: 🔴 Disconnected" | Network / Firewall | Check §2.1 |
| "Connection failed: ICE timeout" | NAT Traversal / TURN | Check §2.2 |
| "Connection failed: Auth rejected" | Trust / Crypto | Check §2.3 |
| Black / frozen video on Controller | Capture / Encoding | Check §2.4 |
| Mouse/keyboard not working on Host | Input Injection / Permissions | Check §2.5 |
| File transfer stalls at 0% / 99% | Flow Control / Network | Check §2.6 |
| High latency (>200 ms) / choppy video | Transport Path / Bandwidth | Check §2.7 |
| Host sleeps during session | Power Management | Check §2.8 |
| Application crashes on launch | .NET / GPU Drivers | Check §2.9 |

---

## 2. Detailed Diagnosis Procedures

### 2.1 Signaling Server Unreachable ("Signaling: 🔴 Disconnected")

**Symptoms**: Status bar shows red signaling indicator; no device list populates; connection attempts fail immediately.

**Root Causes**:
- Outbound TCP port 443 blocked by Windows Firewall, corporate firewall, or ISP
- DNS resolution failure for signaling domain
- TLS certificate validation failure (expired, self-signed, hostname mismatch)
- Signaling server down or misconfigured

**Diagnostic Steps**:

1. **Verify DNS & TLS**:
   ```powershell
   # Test DNS resolution
   Resolve-DnsName signaling.yourdomain.com
   
   # Test TLS connectivity (requires .NET 8+ or curl)
   curl -v https://signaling.yourdomain.com/health
   ```

2. **Test WebSocket Upgrade**:
   ```powershell
   # Using wscat (npm install -g wscat)
   wscat -c wss://signaling.yourdomain.com/ws
   # Should show "connected (press CTRL+C to quit)"
   ```

3. **Check Windows Firewall**:
   ```powershell
   # Verify outbound rule exists
   Get-NetFirewallRule -DisplayName "*ControlDesk*" | Format-Table Name, Enabled, Direction, Action
   
   # If missing, create:
   New-NetFirewallRule -DisplayName "ControlDesk Signaling Outbound" -Direction Outbound -Program "C:\Program Files\ControlDesk\ControlDesk.exe" -Action Allow -Protocol TCP -RemotePort 443
   ```

4. **Inspect Application Logs**:
   ```
   %APPDATA%\ControlDesk\logs\controldesk-*.log
   ```
   Search for: `WebSocket`, `Signaling`, `TLS`, `CertificateValidation`

5. **Corporate Proxy / SSL Inspection**:
   - If your organization performs TLS interception, the server certificate will not match. Add the signaling domain to the proxy bypass list, or deploy ControlDesk with a custom CA bundle (Settings → Network → Custom CA Certificate).

---

### 2.2 ICE Connection Failure / TURN Relay Issues

**Symptoms**: Connection enters "Gathering Candidates" → "Connecting" but never reaches "Active"; eventually times out with "ICE failed" or "Connection timeout".

**Root Causes**:
- Symmetric NAT on one or both ends preventing direct P2P
- coturn TURN relay unreachable (firewall, wrong ports, expired credentials)
- UDP blocked entirely, forcing TCP relay which adds latency
- STUN binding requests failing (public IP discovery broken)

**Diagnostic Steps**:

1. **Check Transport Type in Diagnostics HUD** (Toolbar → 📊 Diagnostics):
   - `Direct` → P2P succeeded
   - `STUN Reflexive` → Direct via public IP mapping
   - `TURN Relay` → Traffic relayed through coturn (expected for symmetric NAT)
   - `Failed` → No path established

2. **Verify TURN Connectivity**:
   ```powershell
   # Test TURN UDP allocation (from another machine)
   turnutils_uclient -u "TIMESTAMP:device-id" -w "BASE64_HMAC" -p 3478 turn.yourdomain.com
   
   # Test TURN TCP (fallback)
   turnutils_uclient -u "TIMESTAMP:device-id" -w "BASE64_HMAC" -p 3478 -T turn.yourdomain.com
   ```

3. **Check VPS Firewall Ports** (see [DEPLOYMENT.md#4](DEPLOYMENT.md#4)):
   - 3478 UDP/TCP (STUN/TURN)
   - 5349 UDP/TCP (TURNS/TLS)
   - 49152–65535 UDP (media relay range)

4. **Verify coturn Configuration**:
   ```bash
   # On VPS: check coturn logs
   docker compose logs coturn | tail -50
   
   # Look for:
   # - "ERROR: ... cannot bind socket" (port conflict)
   # - "ERROR: ... hmac key not found" (secret mismatch)
   # - "WARN: ... no auth method" (wrong realm)
   ```

5. **Force TURN for Testing**:
   - Settings → Network → Check **Force TURN Relay**
   - If this works but Auto fails, your NAT is symmetric and direct P2P is impossible

6. **Check Ephemeral Credential Generation**:
   - Signaling server must generate credentials with correct TURN_SECRET
   - Logs should show: `Generated TURN credentials for device XXX-XXX-XXX, ttl=86400`
   - Verify TURN_SECRET matches between signaling `.env` and `turnserver.conf`

---

### 2.3 Authentication Rejected / Unattended Access Fails

**Symptoms**: "Authentication failed", "Device not trusted", or connection immediately drops after SDP exchange.

**Root Causes**:
- Controller's Device ID not in Host's Trusted Devices list
- Ed25519 key mismatch (identity rotated on one side)
- Challenge-response signature verification failure
- Clock skew > 5 minutes (nonce timestamp validation)

**Diagnostic Steps**:

1. **Verify Trusted Device Entry** (Host side):
   - Settings → Remote Access → Trusted Devices
   - Confirm Controller's Device ID appears with 🟢 Online status
   - Click **Edit Permissions** → Verify Input/Clipboard/File Transfer are enabled

2. **Check Key Fingerprints Match**:
   - Controller: Settings → Security → Ed25519 Key Fingerprint
   - Host: Trusted Devices list → hover device → shows fingerprint
   - Must be identical SHA-256 hash

3. **Identity Rotation Recovery**:
   - If either side clicked **Rotate Identity**, all pairings are broken
   - Re-pair: Host removes old entry → Adds Controller's new Device ID while Controller is online

4. **Check System Time**:
   - Both machines must have accurate time (NTP synchronized)
   - `w32tm /query /status` should show synchronized state

5. **Audit Log Inspection**:
   - Host: Settings → Security → Audit Log → Open
   - Look for: `AuthChallengeFailed`, `UntrustedDevice`, `SignatureVerificationFailed`

---

### 2.4 Black Screen / Frozen Video / No Frame Updates

**Symptoms**: Session connects, toolbar shows active, but video viewport is black, static, or shows "Waiting for frames...".

**Root Causes**:
- DXGI Desktop Duplication capture failing (driver issue, no monitor attached)
- Hardware encoder (MFT) initialization failure
- GPU driver crash / TDR (Timeout Detection and Recovery)
- Monitor configuration change (display added/removed during session)
- Windows Graphics Capture (WGC) permission denied (Windows 11 22H2+)

**Diagnostic Steps**:

1. **Check Capture Method**:
   - Settings → Performance → Capture Method
   - Try switching: `DXGI Desktop Duplication` ↔ `Windows Graphics Capture (WGC)`

2. **Verify Hardware Encoding**:
   - Settings → Display → Hardware Encoding: Toggle **Off** (forces software fallback)
   - If video appears, GPU driver or MFT encoder is the issue

3. **Update GPU Drivers**:
   - Intel: Intel Arc & Iris Xe Graphics drivers (DCH)
   - NVIDIA: Game Ready / Studio Driver (not DCH-specific)
   - AMD: Adrenalin Edition
   - Restart after update

4. **Check for Headless / No Monitor**:
   - Host must have at least one active display or virtual display adapter
   - For headless servers: Install "Dummy HDMI Plug" or configure Windows to simulate display

5. **Inspect Capture Logs**:
   ```
   %APPDATA%\ControlDesk\logs\controldesk-*.log
   ```
   Search for: `DXGI`, `IDXGIOutputDuplication`, `MFT`, `Encode`, `FrameDropped`

6. **Monitor Reconfiguration**:
   - If Host user changes display arrangement during session, Controller must use **Monitor** dropdown to re-select
   - Negative coordinate monitors (left/above primary) require correct virtual desktop bounding rect

---

### 2.5 Input Injection Not Working (Mouse/Keyboard)

**Symptoms**: Video works, but mouse clicks, movement, or keystrokes have no effect on Host.

**Root Causes**:
- Host permissions: Input not granted in connection dialog or Trusted Device config
- UAC / Secure Desktop: Input blocked during UAC prompt or lock screen
- SendInput API failure (wrong session, integrity level mismatch)
- Coordinate mapping error (multi-monitor DPI scaling)

**Diagnostic Steps**:

1. **Verify Permissions**:
   - Attended: Connection dialog → ☑ Keyboard & Mouse Control
   - Unattended: Settings → Remote Access → Trusted Device → Edit Permissions → ☑ Input

2. **Test with Administrator Privileges**:
   - Host: Right-click ControlDesk.exe → **Run as Administrator**
   - This elevates integrity level, allowing input injection into elevated/UAC windows

3. **Check for Secure Desktop Block**:
   - Input cannot reach UAC prompt, Ctrl+Alt+Del, or Lock Screen
   - This is by design (Windows security boundary)

4. **Multi-Monitor Coordinate Debug**:
   - Toolbar → 📊 Diagnostics → Enable HUD
   - Move mouse → Watch `Input X/Y` coordinates
   - Should map to Host's virtual desktop space (0..65535 normalized)

5. **Test Input Locally**:
   - Host: Settings → Security → **Test Input Injection** (injects a harmless mouse move)

---

### 2.6 File Transfer Stalls / Fails

**Symptoms**: Transfer starts but progress freezes at 0%, ~50%, or 99%; "Transfer failed" error.

**Root Causes**:
- SCTP `BufferedAmount` backpressure (sender faster than network)
- Network packet loss on TURN relay path
- SHA-256 hash mismatch (corruption)
- Path traversal block (filename contains `..`, reserved names, or absolute path)
- Host disk full / destination folder permissions

**Diagnostic Steps**:

1. **Check Diagnostics HUD** (Toolbar → 📊 Diagnostics):
   - `Buffered Amount` on transfer channel — if growing continuously, network is bottleneck
   - `Packet Loss %` — >1% indicates path issues

2. **Retry / Resume**:
   - Transfers panel → Click **Resume** on failed transfer
   - ControlDesk uses byte-offset handshake to resume from last acknowledged chunk

3. **Verify Filename Safety**:
   - No `..`, no `\`, no `:`, no `*`, `?`, `"`, `<`, `>`, `|`
   - No reserved names: `CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`
   - Max path length: 260 chars (Windows default)

4. **Check Host Disk Space**:
   - Destination: `~/Downloads/ControlDesk/` by default
   - Ensure write permissions for current user

5. **Large File > 4 GB**:
   - Per-file limit is 4 GB (32-bit chunk index)
   - Split archives (7-Zip: `7z a -v4g archive.7z folder\`)

---

### 2.7 High Latency / Low Frame Rate / Choppy Video

**Symptoms**: Video updates slowly (5-10 FPS), mouse feels laggy, RTT > 200 ms.

**Root Causes**:
- TURN relay in use (adds 1-2 hops, ~50-150 ms each way)
- Insufficient upload bandwidth on Host (encoding bitrate > available uplink)
- CPU/GPU saturation on Host (encoding overhead)
- Wi-Fi interference / bufferbloat on either end
- Suboptimal quality preset for current network

**Diagnostic Steps**:

1. **Check Transport Type** (Diagnostics HUD):
   - If `TURN Relay` → Expected latency increase
   - If `Direct` but high RTT → Check ISP routing, consider VPN

2. **Adjust Quality Preset**:
   - Toolbar → ⚡ Quality → **Speed** (15 FPS, lower bitrate)
   - Or Settings → Display → Default Quality → Speed

3. **Limit Bitrate**:
   - Settings → Performance → Max Bitrate → 5 Mbps (or lower)

4. **Check Host Resource Usage**:
   - Task Manager → GPU → Video Encode utilization
   - If 100%, reduce resolution/frame rate or disable hardware encoding

5. **Enable Hardware Encoding**:
   - Settings → Display → Hardware Encoding → **On**
   - Offloads H.264 to GPU (QuickSync/NVENC/AMF)

6. **Wired Ethernet**:
   - Both ends should use wired connection if possible
   - Wi-Fi adds jitter and packet loss

---

### 2.8 Host Sleeps During Session / Session Drops After Idle

**Symptoms**: Connection works initially, but after minutes/hours of inactivity, Host goes to sleep and session terminates.

**Root Causes**:
- Windows power settings: Sleep after X minutes
- `SetThreadExecutionState` not called (bug)
- Fast Startup interfering with wake
- Network adapter power saving (Wi-Fi/ethernet sleep)

**Diagnostic Steps**:

1. **Verify Power Assertion Active**:
   - During session, Diagnostics HUD should show `Power: Asserted`
   - If `Power: Normal`, the keep-awake call failed

2. **Configure Windows Power Settings** (see [INSTALLATION.md#7](INSTALLATION.md#7)):
   - Settings → System → Power & battery → Screen and sleep → **Never** (when plugged in)
   - Disable Fast Startup

3. **Network Adapter Power Management**:
   - Device Manager → Network Adapters → [Your Adapter] → Properties → Power Management
   - Uncheck "Allow the computer to turn off this device to save power"

4. **Check Heartbeat Logs**:
   ```
   %APPDATA%\ControlDesk\logs\controldesk-*.log
   ```
   Search for: `Heartbeat`, `Reconnecting`, `ICE Restart`

---

### 2.9 Application Crashes on Launch / Won't Start

**Symptoms**: Double-click exe → nothing happens, or crash dialog, or immediate exit.

**Root Causes**:
- Missing Visual C++ Redistributable (rare for self-contained)
- GPU driver crash during WPF hardware acceleration init
- Antivirus / EDR blocking execution
- Corrupted `%APPDATA%\ControlDesk\` databases
- .NET 10 runtime initialization failure

**Diagnostic Steps**:

1. **Run from Command Line for Error Output**:
   ```powershell
   cd "C:\Program Files\ControlDesk"
   .\ControlDesk.exe
   ```
   Watch console for unhandled exception stack trace.

2. **Disable Hardware Acceleration (WPF)**:
   - Create `ControlDesk.exe.config` next to exe:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <runtime>
       <AppContextSwitchOverrides value="Switch.System.Windows.Media.EnableHardwareAcceleration=false" />
     </runtime>
   </configuration>
   ```

3. **Reset Local Data** (nuclear option):
   ```powershell
   Remove-Item -Recurse -Force "$env:APPDATA\ControlDesk"
   ```
   Then relaunch — fresh identity and settings generated.

4. **Check Windows Event Viewer**:
   - Event Viewer → Windows Logs → Application
   - Source: `.NET Runtime` or `Application Error`
   - Event ID 1026 (unhandled exception) or 1000 (app crash)

5. **Antivirus Exclusion**:
   - Add `C:\Program Files\ControlDesk\` to Windows Defender / third-party AV exclusions

---

## 3. Diagnostic Tools & Log Locations

### 3.1 Application Logs
```
%APPDATA%\ControlDesk\logs\controldesk-YYYYMMDD.log
```
- Rotating daily, max 10 MB each, 7 days retention
- Levels: Debug, Info, Warning, Error, Critical
- Structured JSON lines for parsing

### 3.2 In-Session Diagnostics HUD
- Toolbar → 📊 **Diagnostics** (toggle)
- Real-time overlay showing:
  - RTT (ms), Jitter (ms), Packet Loss (%)
  - Video: FPS, Bitrate (Mbps), Encoder (HW/SW), Keyframe Interval
  - Transport: Type (Direct/STUN/TURN), Local/Remote Candidates
  - SCTP: Buffered Amount per channel
  - Power: Asserted/Normal

### 3.3 Audit Log Viewer
- Settings → Security → **Audit Log** → Open
- Tamper-evident HMAC-SHA256 hash chain
- Events: SessionStart, SessionEnd, AuthSuccess, AuthFailed, DeviceAdded, DeviceRemoved, IdentityRotated, SettingsChanged

### 3.4 Network Packet Capture (Advanced)
```powershell
# Wireshark filter for ControlDesk traffic:
# Signaling: websocket && tls
# WebRTC: udp.port == 3478 || udp.port == 5349 || (udp.port >= 49152 && udp.port <= 65535)
# DTLS: dtls
# SRTP: srtp
```

---

## 4. Common Error Codes Reference

| Code | Context | Meaning | Resolution |
|------|---------|---------|------------|
| `ICE_GATHERING_FAILED` | Network | No ICE candidates gathered | Check STUN/TURN connectivity |
| `ICE_CONNECTION_FAILED` | Network | All candidate pairs failed | Force TURN, check relay ports |
| `DTLS_HANDSHAKE_FAILED` | Crypto | Certificate/key mismatch | Restart both apps, check clocks |
| `AUTH_CHALLENGE_FAILED` | Auth | Ed25519 signature invalid | Re-pair trusted device |
| `UNTRUSTED_DEVICE` | Auth | Device ID not in trusted list | Add to Trusted Devices on Host |
| `CAPTURE_INIT_FAILED` | Capture | DXGI/WGC initialization error | Update GPU drivers, try WGC |
| `ENCODER_INIT_FAILED` | Media | MFT hardware encoder unavailable | Toggle Hardware Encoding Off |
| `INPUT_INJECTION_DENIED` | Input | SendInput blocked by UAC/IL | Run Host as Administrator |
| `TURN_ALLOCATE_FAILED` | Network | coturn rejected allocation | Check TURN_SECRET, realm, ports |
| `FILE_HASH_MISMATCH` | Transfer | SHA-256 verification failed | Retry transfer (auto-resume) |

---

## 5. Getting Support

If this guide does not resolve your issue:

1. **Collect Diagnostic Bundle**:
   ```powershell
   # Creates a zip with logs, settings (sanitized), and system info
   # Settings → Security → Export Diagnostic Bundle
   ```

2. **Provide**:
   - ControlDesk version (Home → About)
   - Both OS versions (`winver`)
   - Network topology (ISP, router, VPN, corporate firewall)
   - Transport type from Diagnostics HUD
   - Relevant log excerpts (last 100 lines from `%APPDATA%\ControlDesk\logs\`)

3. **Security Issues**: Report privately via GitHub Security Advisories — do not post logs with device IDs or keys publicly.

---

*ControlDesk Troubleshooting Guide — Systematic Diagnosis for Reliable Cross-Border Connectivity*
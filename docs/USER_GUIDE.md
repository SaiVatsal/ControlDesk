# ControlDesk User Guide

**Version**: 1.0.0  
**Audience**: End Users (Controller & Host)

---

## 1. Getting Started

### 1.1 First Launch

1. Extract `ControlDesk.exe` to a folder (e.g., `C:\Program Files\ControlDesk\`).
2. Double-click `ControlDesk.exe`.
3. On first run, Windows Firewall will prompt: **"Allow ControlDesk to communicate on private networks?"** → Click **Allow Access**.
4. The main window opens showing your **Device ID** (format: `XXX-XXX-XXX`).

> **Tip**: Your Device ID is your unique identity. Share it with the person you want to connect to/from.

---

## 2. Main Window Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  ControlDesk                              [─] [□] [×]                       │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────────────────────────────────────────────┐  │
│  │  NAVIGATION │  │                    CONTENT AREA                     │  │
│  ├─────────────┤  │                                                     │  │
│  │ 🏠 Home     │  │  ┌─────────────────────────────────────────────┐   │  │
│  │ 📱 Devices  │  │  │           YOUR DEVICE ID                    │   │  │
│  │ 📋 History  │  │  │        123-456-789                          │   │  │
│  │ 📁 Transfers│  │  │  [Copy ID]  [QR Code]  [Regenerate*]        │   │  │
│  │ ⚙ Settings  │  │  └─────────────────────────────────────────────┘   │  │
│  │             │  │                                                     │  │
│  │             │  │  ┌─────────────────────────────────────────────┐   │  │
│  │             │  │  │           QUICK CONNECT                     │   │  │
│  │             │  │  │  Device ID: [________________]  [Connect]   │   │  │
│  │             │  │  └─────────────────────────────────────────────┘   │  │
│  │             │  │                                                     │  │
│  │             │  │  Recent Devices:                                  │   │  │
│  │             │  │  • 987-654-321  Dubai-Host      🟢 Online        │   │  │
│  │             │  │  • 555-123-456  Office-PC       🔴 Offline       │   │  │
│  │             │  │                                                     │  │
│  └─────────────┘  └─────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────────────────┤
│  Status: Ready  |  Signaling: 🟢 Connected  |  TURN: 🟢 Ready             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Navigation Pane

| View | Purpose |
|------|---------|
| **Home** | Device ID, Quick Connect, recent devices, connection status |
| **Devices** | Trusted devices, unattended access authorization, device management |
| **History** | Past sessions with duration, transport type, bytes transferred |
| **Transfers** | Active/completed file transfers, progress, cancel/retry |
| **Settings** | All configuration options |

---

## 3. Connecting (Controller → Host)

### 3.1 Attended Connection (Manual Accept)

**On Controller (India):**
1. Go to **Home** view.
2. Enter Host's Device ID in **Quick Connect** box (format: `XXX-XXX-XXX`).
3. Click **Connect**.
4. Wait for connection... Host will see a prompt.

**On Host (Dubai):**
1. A modal dialog appears: **"Incoming Connection Request"**
   ```
   ┌─────────────────────────────────────────────────────────────┐
   │  Incoming Connection Request                    [×]         │
   ├─────────────────────────────────────────────────────────────┤
   │  Device: 123-456-789 (Controller-India)                     │
   │  Key Fingerprint: SHA256:AB:CD:EF:12:34:56:78:90...         │
   │                                                             │
   │  Permissions requested:                                     │
   │  ☑ Keyboard & Mouse Control                                 │
   │  ☑ Clipboard Synchronization                                │
   │  ☑ File Transfer                                            │
   │                                                             │
   │  [Deny]                                    [Accept]         │
   └─────────────────────────────────────────────────────────────┘
   ```
2. Review the Device ID and key fingerprint.
3. Adjust permissions if needed.
4. Click **Accept**.

**Result**: Session begins. Controller sees Host's desktop.

### 3.2 Unattended Connection (Automatic)

**Prerequisites (one-time setup on Host):**
1. Host: Settings → **Remote Access** → Enable **Unattended Access**.
2. Host: Settings → **Remote Access** → **Trusted Devices** → **Add Device**.
3. Enter Controller's Device ID → **Add**.
4. Controller's public key is now trusted.

**On Controller (India):**
1. Enter Host's Device ID in Quick Connect.
2. Click **Connect**.
3. **No prompt appears on Host** — connection establishes automatically.

> **Security**: Unattended access uses Ed25519 challenge-response. The Host verifies the Controller possesses the private key matching the trusted public key. No passwords transmitted.

---

## 4. Active Session — Controller View

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  ControlDesk — Session: 987-654-321 (Dubai-Host)              [Disconnect]  │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  ████████████████████████████████████████████████████████████████   │  │  ← Video Viewport
│  │  █  HOST DESKTOP STREAM (H.264, 30 FPS, 1920×1080)                  █  │
│  │  █                                                                     █  │
│  │  █                                                                     █  │
│  └───────────────────────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────────────────┤
│  [🖥 Monitor ▼]  [⚡ Quality: Balanced ▼]  [🔍 Scale: Fit ▼]  [⌨ Input: ON] │
│  [📋 Clipboard: ON]  [📁 Files]  [💬 Chat]  [📊 Diagnostics]  [⛔ Disconnect]│
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.1 Toolbar Controls

| Control | Function |
|---------|----------|
| **Monitor** | Switch between Host's multiple monitors |
| **Quality** | Speed (15 FPS) / Balanced (30 FPS) / Quality (60 FPS) / Auto |
| **Scale** | Fit to window / Actual size (100%) / Stretch |
| **Input** | Toggle keyboard/mouse forwarding (Host permission permitting) |
| **Clipboard** | Toggle clipboard sync |
| **Files** | Open file transfer panel |
| **Chat** | Open in-session text chat |
| **Diagnostics** | Toggle real-time HUD (RTT, FPS, bitrate, transport) |
| **Disconnect** | End session cleanly |

### 4.2 Keyboard & Mouse

| Action | Behavior |
|--------|----------|
| Mouse movement | Relative to viewport → mapped to Host virtual desktop |
| Left/Right/Middle click | Injected via `SendInput` on Host |
| Scroll wheel | Vertical/horizontal scroll injected |
| Keyboard shortcuts | Most pass through (Win, Alt+Tab, Ctrl+Alt+Del handled locally) |
| Special keys | Print Screen, Windows key, Ctrl+Alt+Del **never forwarded** |

### 4.3 Multi-Monitor Support

- Host monitors enumerated at session start.
- Controller selects active monitor from dropdown.
- Negative coordinate monitors (left/above primary) fully supported.
- DPI scaling applied per-monitor.

---

## 5. File Transfer

### 5.1 Sending Files (Controller → Host)

1. In active session, click **📁 Files** on toolbar.
2. Click **Send Files** or drag-and-drop files into panel.
3. Select destination folder on Host (default: `~/Downloads/ControlDesk`).
4. Transfer begins — progress bars show speed, ETA, per-file status.

### 5.2 Receiving Files (Host → Controller)

1. Host: In session toolbar, click **📁 Files** → **Send Files**.
2. Controller: Receives notification, chooses save location.

### 5.3 Transfer Features

- **Chunk Size**: 64 KB
- **Integrity**: SHA-256 per chunk + full file hash
- **Resume**: Interrupted transfers resume from last acknowledged chunk
- **Parallel**: Up to 4 concurrent file streams
- **Size Limit**: Per-file 4 GB, session total unlimited
- **Path Safety**: Blocks `..`, reserved names, absolute paths outside sandbox

---

## 6. Clipboard Synchronization

### 6.1 Behavior

- **Enabled by default** in attended sessions (if both parties allow).
- **Text only** (UTF-16), max 1 MB.
- **Bidirectional**: Copy on Controller → Paste on Host, and vice versa.
- **Echo Prevention**: Hash-based deduplication prevents infinite copy loops.

### 6.2 Disabling

- Toolbar: **📋 Clipboard: ON/OFF**
- Settings → Remote Access → Clipboard Sync (global default)

---

## 7. In-Session Chat

### 7.1 Usage

1. Click **💬 Chat** on toolbar.
2. Type message, press Enter to send.
3. Messages appear with timestamps, color-coded by sender.

### 7.2 Features

- **Persistent during session only** (not saved after disconnect).
- **Max message length**: 4000 characters.
- **System messages**: Connection events, file transfer notifications.

---

## 8. Devices View — Trusted Devices & Unattended Access

### 8.1 Trusted Devices List

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  TRUSTED DEVICES (Unattended Access)                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ 🟢 123-456-789  Controller-India                                      │  │
│  │    Added: 2025-01-15 14:32  |  Last connected: 2025-01-20 09:15     │  │
│  │    Permissions: ☑ Input  ☑ Clipboard  ☑ File Transfer                │  │
│  │    [Edit Permissions]  [Remove]                                       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │ 🔴 555-123-456  Old-Laptop                                            │  │
│  │    Added: 2024-11-02  |  Never connected                              │  │
│  │    [Remove]                                                           │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  [+ Add Trusted Device]                                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 8.2 Adding a Trusted Device

1. Click **+ Add Trusted Device**.
2. Enter Device ID (format: `XXX-XXX-XXX`).
3. Controller must be **online** (green dot) for key exchange.
4. Click **Add** → Controller's public key fetched and stored.
5. Configure default permissions for this device.

### 8.3 Managing Permissions

Per trusted device, you can set default permissions:
- **Keyboard & Mouse**: Allow input injection
- **Clipboard**: Allow clipboard sync
- **File Transfer**: Allow file send/receive

These are the **maximum** permissions; you can reduce them per-session via the incoming connection dialog.

---

## 9. Session History

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  SESSION HISTORY                                                            │
├──────────────┬──────────────┬────────────┬──────────┬──────────┬───────────┤
│ Date         │ Remote Device│ Duration   │ Transport│ Data TX  │ Result    │
├──────────────┼──────────────┼────────────┼──────────┼──────────┼───────────┤
│ 2025-01-20   │ 987-654-321  │ 2h 34m     │ Direct   │ 1.2 GB   │ Completed │
│ 2025-01-19   │ 123-456-789  │ 45m        │ TURN     │ 340 MB   │ Completed │
│ 2025-01-18   │ 555-123-456  │ 12m        │ Failed   │ 0 B      │ Timeout   │
│ 2025-01-15   │ 987-654-321  │ 5h 12m     │ TURN     │ 4.7 GB   │ Completed │
└──────────────┴──────────────┴────────────┴──────────┴──────────┴───────────┘
```

- **Transport**: Direct P2P, STUN Reflexive, or TURN Relay
- **Data TX**: Total bytes sent (video + files + overhead)
- **Result**: Completed, Timeout, Rejected, Error

---

## 10. Settings Reference

### 10.1 General

| Setting | Default | Description |
|---------|---------|-------------|
| Start with Windows | ✓ On | Registers per-user Run key |
| Minimize to tray on close | ✓ On | Close button hides to tray |
| Single instance | ✓ On | Prevents duplicate windows |
| Theme | System | Light / Dark / Follow System |
| Language | English | UI language |

### 10.2 Remote Access

| Setting | Default | Description |
|---------|---------|-------------|
| Unattended Access | Off | Allow trusted devices to connect without prompt |
| Trusted Devices | (list) | Manage authorized unattended controllers |
| Require confirmation for attended | On | Always prompt for manual connections |
| Default permissions (attended) | All on | Input, Clipboard, File Transfer |

### 10.3 Display

| Setting | Default | Description |
|---------|---------|-------------|
| Default quality | Balanced | Speed / Balanced / Quality / Auto |
| Default scaling | Fit | Fit / Actual Size / Stretch |
| Hardware encoding | On | Use GPU H.264 encoder (QuickSync/NVENC/AMF) |
| Frame rate limit | 60 FPS | Cap encoder output |
| Show connection HUD | Off | Overlay RTT/FPS/bitrate |

### 10.4 Security

| Setting | Default | Description |
|---------|---------|-------------|
| Device ID | (generated) | Your 9-digit identity (regeneratable*) |
| Ed25519 Key Fingerprint | (display) | SHA-256 fingerprint of your public key |
| Rotate Identity | (button) | Generate new keypair — breaks all trusted pairings |
| Audit Log | (button) | Open tamper-evident log viewer |
| Verify Audit Chain | (button) | Cryptographic integrity check |

> *Regenerating Device ID invalidates all existing trusted device relationships on other machines.

### 10.5 Network

| Setting | Default | Description |
|---------|---------|-------------|
| Signaling Server | `wss://signaling.controldesk.example.com/ws` | WebSocket endpoint |
| TURN Domain | `turn.controldesk.example.com` | coturn relay hostname |
| STUN Servers | Google public | Fallback discovery |
| ICE Policy | All | Allow Direct + STUN + TURN |
| Force TURN | Off | Bypass direct/STUN, always relay |

### 10.6 Performance

| Setting | Default | Description |
|---------|---------|-------------|
| Capture Method | DXGI | DXGI Desktop Duplication / WGC Fallback |
| Encoder | Hardware | Hardware (MFT) / Software Fallback |
| Bitrate Mode | CBR | Constant / Variable |
| Max Bitrate | 10 Mbps | Cap for Quality preset |
| Latency Mode | Low | Ultra-low / Low / Normal |

---

## 11. System Tray Operation

### 11.1 Tray Icon States

| Icon | Meaning |
|------|---------|
| 🟢 Green | Ready, signaling connected |
| 🟡 Yellow | Connecting / Reconnecting |
| 🔴 Red | Signaling disconnected |
| 🔵 Blue | Active session in progress |
| ⚪ Gray | Offline mode (no network) |

### 11.2 Tray Context Menu

```
┌─────────────────────────────┐
│ ControlDesk                 │
│ ──────────────────────────  │
│ 🟢 Device ID: 123-456-789   │
│ ──────────────────────────  │
│ 🏠  Open ControlDesk        │
│ 📱  Devices                 │
│ ⚙  Settings                 │
│ ──────────────────────────  │
│ 🔵 Active Session: Dubai    │
│    [Disconnect]             │
│    [Files]                  │
│    [Chat]                   │
│ ──────────────────────────  │
│ ✕ Exit ControlDesk          │
└─────────────────────────────┘
```

- **Left-click**: Opens main window (or restores from tray).
- **Right-click**: Context menu above.
- **Exit**: Fully terminates application (stops capture, closes sessions, removes tray icon).

---

## 12. Keyboard Shortcuts (Global)

| Shortcut | Action |
|----------|--------|
| `Ctrl+Alt+C` | Show/Hide main window |
| `Ctrl+Alt+D` | Disconnect active session |
| `Ctrl+Alt+F` | Toggle file transfer panel |
| `Ctrl+Alt+H` | Toggle diagnostics HUD |
| `Ctrl+Alt+Q` | Quick Connect dialog |

---

## 13. Common Workflows

### 13.1 Daily Driver: India Controller → Dubai Host (Unattended)

1. **Dubai Host** (one-time):
   - Enable Unattended Access
   - Add India Controller as Trusted Device
   - Keep ControlDesk running (tray)

2. **India Controller** (each session):
   - Launch ControlDesk (or already running in tray)
   - Click Dubai Host in Recent Devices → Connect
   - Work remotely — no interaction needed in Dubai

### 13.2 Ad-Hoc Support: Friend's Computer

1. Both launch ControlDesk.
2. Share Device IDs (read aloud, chat, email).
3. Controller enters Host ID → Connect.
4. Host clicks Accept on prompt.
5. Session ends → both close or minimize to tray.

### 13.3 File Transfer During Session

1. Drag file from Controller Explorer → Session viewport (or Files panel).
2. Host receives notification → saves to chosen folder.
3. Progress visible on both sides.

---

## 14. Troubleshooting Quick Reference

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| "Signaling disconnected" | Network / firewall | Check internet, allow port 443 outbound |
| "Connection failed" | TURN unreachable | Verify TURN domain, port 3478/5349 UDP |
| Black screen on Host | GPU driver / capture | Update GPU drivers, toggle Hardware Encoding |
| Input not working | Permissions / UAC | Run as Administrator on Host |
| High latency (>300ms) | TURN relay in use | Check Diagnostics HUD → Transport Type |
| File transfer stuck | Backpressure / network | Pause/Resume, check Diagnostics → Buffered Amount |

> For detailed troubleshooting, see [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

---

## 15. Uninstall

1. Exit ControlDesk (Tray → Exit).
2. Delete the installation folder (e.g., `C:\Program Files\ControlDesk\`).
3. Optional: Remove settings: `%APPDATA%\ControlDesk\`
4. Optional: Remove startup entry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ControlDesk`

---

*ControlDesk User Guide — Complete Reference for Controllers and Hosts*
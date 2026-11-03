# ControlDesk Installation & Setup Guide

**Version**: 1.0.0  
**Target Platform**: Windows 10 (21H2+) / Windows 11 x64  
**Audience**: End Users, System Administrators

---

## 1. System Requirements

### 1.1 Minimum Requirements

| Component | Minimum Specification |
|-----------|------------------------|
| **Operating System** | Windows 10 64-bit (Version 21H2 / Build 19044 or later) |
| **Processor** | Intel Core i3 (4th Gen) or AMD equivalent (x64 architecture) |
| **Memory (RAM)** | 4 GB |
| **Storage** | 150 MB free disk space |
| **Graphics Adapter** | DirectX 11.0 compatible GPU (Intel HD 4000, NVIDIA GT 600, AMD HD 7000) |
| **Network Interface** | 5 Mbps broadband internet connection (low-latency connection recommended) |
| **Display** | 1280×720 minimum display resolution |

### 1.2 Recommended Requirements

| Component | Recommended Specification |
|-----------|---------------------------|
| **Operating System** | Windows 11 64-bit (Version 22H2 or later) |
| **Processor** | Intel Core i5 (8th Gen+) or AMD Ryzen 5 (3000 series+) |
| **Memory (RAM)** | 8 GB or higher |
| **Storage** | 500 MB free SSD storage |
| **Hardware Video Acceleration** | Intel QuickSync (Gen 7+), NVIDIA NVENC (Maxwell 2nd Gen+), or AMD AMF (GCN 2nd Gen+) |
| **Network Interface** | 25+ Mbps high-speed fiber connection (<100 ms RTT cross-border) |
| **Display** | 1920×1080 (1080p) or 3840×2160 (4K) multi-monitor setup |

> **Note on .NET Runtime**: ControlDesk is distributed as a **self-contained executable**. You do **not** need to install the .NET Runtime or .NET SDK separately.

---

## 2. Installation Methods

ControlDesk is designed for portability and zero footprint. It does not require a traditional MSI installer or complex registry tampering.

### 2.1 Portable Installation (Recommended)

1. Download the latest client archive: `ControlDesk-Client-win-x64-v1.0.0.zip`.
2. Extract the archive contents to your chosen application directory:
   - For a single user: `%LOCALAPPDATA%\Programs\ControlDesk\`
   - For all machine users: `C:\Program Files\ControlDesk\` (requires Administrator privileges)
3. The directory should contain:
   - `ControlDesk.exe` (The primary application executable)
   - `app.manifest` (Application execution manifest declaring DPI awareness and execution level)
   - `LICENSE.txt` (MIT License)
   - `INSTALL.txt` (Quick reference instructions)
4. Right-click `ControlDesk.exe` and select **Pin to Start** or **Pin to Taskbar** for easy access.
5. Launch `ControlDesk.exe`.

### 2.2 Silent / Automated Deployment (PowerShell)

For system administrators deploying ControlDesk across multiple endpoints:

```powershell
# Run with elevated privileges
$installDir = "C:\Program Files\ControlDesk"
$zipUrl = "https://github.com/your-org/ControlDesk/releases/latest/download/ControlDesk-Client-win-x64.zip"
$tempZip = "$env:TEMP\ControlDesk-Client.zip"

# Create destination folder
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# Download release archive
Invoke-WebRequest -Uri $zipUrl -OutFile $tempZip

# Extract package
Expand-Archive -Path $tempZip -DestinationPath $installDir -Force
Remove-Item $tempZip -Force

# Configure Windows Defender Firewall rule for outbound signaling/media
New-NetFirewallRule -DisplayName "ControlDesk Client Outbound" `
    -Direction Outbound `
    -Program "$installDir\ControlDesk.exe" `
    -Action Allow `
    -Profile Any | Out-Null

Write-Host "ControlDesk installed successfully to: $installDir" -ForegroundColor Green
```

---

## 3. First Launch & Configuration

### 3.1 Windows Firewall Prompt

On first launch, Windows Defender Firewall will present a security alert:
- **Alert**: *Windows Defender Firewall has blocked some features of this app*
- **Action**: Ensure **Private networks** is checked (and Public networks if operating on untrusted Wi-Fi). Click **Allow access**.

ControlDesk makes outbound connections to the signaling server and TURN relay over standard HTTPS (port 443) and WebRTC UDP/TCP ports. No inbound firewall listening ports are opened.

### 3.2 Device Identity Generation

Upon startup, ControlDesk checks for existing credentials in `%APPDATA%\ControlDesk\`. If none are found:
1. It generates a unique 9-digit Device ID formatted as `XXX-XXX-XXX`.
2. It generates a cryptographic Ed25519 identity keypair.
3. The private key seed is encrypted via Windows DPAPI (`DataProtectionScope.CurrentUser`) and saved locally.
4. Your unique Device ID and public key fingerprint are displayed on the **Home** tab.

---

## 4. Setting Up Automatic Startup

To ensure the Dubai Host (or India Controller) is always accessible after a system reboot:

1. Launch **ControlDesk**.
2. Click **Settings** (gear icon) in the left navigation sidebar.
3. On the **General** tab:
   - Toggle **Start with Windows** to **ON**.
   - Toggle **Minimize to system tray on close** to **ON**.
4. ControlDesk registers a startup shortcut under the current user's registry hive:
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ControlDesk`
   Pointing to: `"C:\Program Files\ControlDesk\ControlDesk.exe" --background`

---

## 5. Network Configuration & Custom Servers

ControlDesk includes default signaling and TURN endpoints, but organizations and private users connecting between India and Dubai should configure their self-hosted VPS servers for maximum speed and privacy.

1. Navigate to **Settings** → **Network**.
2. Update the following fields:
   - **Signaling Server URL**: Enter your secure WebSocket URL:
     `wss://signaling.yourdomain.com/ws`
   - **TURN Domain**: Enter your coturn relay hostname:
     `turn.yourdomain.com`
   - **Force TURN Relay**: Leave unchecked unless you are experiencing severe symmetric NAT blockage and wish to bypass direct P2P candidate negotiation entirely.
3. Click **Save & Reconnect**.
4. Verify the status bar at the bottom displays:
   - `Signaling: Connected (Green)`
   - `TURN: Ready (Green)`

---

## 6. Configuring Unattended Access (Dubai Host Setup)

To allow the India Controller to connect without someone in Dubai having to manually click "Accept":

1. On the **Dubai Host**:
   - Open **Settings** → **Remote Access**.
   - Toggle **Enable Unattended Access** to **ON**.
   - Under **Trusted Devices**, click **Add Trusted Device**.
   - Enter the India Controller's 9-digit Device ID (e.g., `123-456-789`).
   - Click **Pair Device**.
2. Verification:
   - The India Controller must be online during pairing.
   - Once paired, the Controller's Ed25519 public key is stored in the Host's trusted database (`%APPDATA%\ControlDesk\settings.db`).
   - The India Controller can now connect at any time.

---

## 7. Power Management Setup (Preventing Host Sleep)

Remote desktop sessions cannot connect if the Host computer enters Sleep or Hibernate.

1. In ControlDesk, power assertion is handled automatically: during active sessions, ControlDesk invokes Win32 `SetThreadExecutionState` to keep the host awake.
2. To ensure the Host is always available *before* a session connects:
   - Open Windows **Settings** → **System** → **Power & battery**.
   - Under **Screen and sleep**:
     - Set *When plugged in, put my device to sleep after*: **Never**.
   - Disable **Fast Startup** if you plan to wake the PC using Wake-on-LAN:
     - Open `Control Panel` → `Power Options` → `Choose what the power buttons do`.
     - Click `Change settings that are currently unavailable`.
     - Uncheck `Turn on fast startup (recommended)`.

---

## 8. Directory Structure & Local Data Files

ControlDesk stores all configuration, keys, and session logs in the user profile directory:

```
%APPDATA%\ControlDesk\
├── identity.dat      # DPAPI-encrypted Ed25519 private key seed
├── settings.db       # SQLite: User settings, trusted devices, network endpoints
├── history.db        # SQLite: Historic session metadata, connection metrics
├── audit.db          # SQLite: Tamper-evident HMAC-SHA256 chained audit logs
└── logs/
    └── controldesk-*.log  # Rotating application diagnostics log files
```

---

## 9. Updating ControlDesk

Because ControlDesk is a portable single-file binary:
1. Exit ControlDesk completely (Right-click tray icon → **Exit ControlDesk**).
2. Download the new release archive.
3. Replace `ControlDesk.exe` in the application directory.
4. Launch `ControlDesk.exe`. Your settings, keys, and trusted device pairings remain completely intact in `%APPDATA%\ControlDesk\`.

---

## 10. Uninstallation & Data Removal

To cleanly remove ControlDesk from your system:

1. Open ControlDesk, navigate to **Settings** → **General**, and toggle **Start with Windows** to **OFF**.
2. Exit the application completely.
3. Delete the application folder (e.g., `C:\Program Files\ControlDesk\`).
4. To remove all configuration, cryptographic keys, and history:
   ```powershell
   Remove-Item -Recurse -Force "$env:APPDATA\ControlDesk"
   ```
5. Remove firewall rules (optional):
   ```powershell
   Remove-NetFirewallRule -DisplayName "ControlDesk Client Outbound" -ErrorAction SilentlyContinue
   ```

---

*ControlDesk Installation Guide — Enterprise Cross-Border Remote Access*
# ControlDesk — Features & Architecture Guide

ControlDesk is a high-performance, standalone, production-ready Windows remote desktop solution engineered specifically for ultra-low latency, cross-border remote management (such as connecting machines between **India** and **Dubai**) across symmetric NATs, CGNATs, and restrictive enterprise firewalls.

---

## 1. Executive Summary

ControlDesk delivers a native, lightweight alternative to commercial remote desktop utilities (AnyDesk, TeamViewer) without subscription walls, artificial timeouts, tracking, or cloud bloat. 

- **Unified Dual-Role Executable**: One application acts interchangeably as **Controller** (client viewing and operating) and **Host** (remote machine being accessed).
- **Zero-Animation Native UI**: Built with pure WPF and C# 13 / .NET 10. No Electron, no web rendering engines, no animated fluff—ensuring instantaneous input responsiveness, minimal memory footprint (< 65 MB RAM at idle), and minimal CPU impact (< 0.5% CPU when idle).
- **Cross-Border NAT Traversal**: Uses WebRTC ICE with direct P2P via STUN and automatic fallback to an authenticated self-hosted `coturn` TURN relay, enabling reliable connectivity between India and Dubai.
- **Cryptographic Security**: Employs Ed25519 asymmetric keypairs, mutual challenge-response authentication, and Windows DPAPI encrypted secret storage.

---

## 2. Core Capabilities & Feature Breakdown

### 2.1 Unified Dual-Role Operation
- **Controller Mode**: Connect to any remote host using their 9-digit Device ID (`XXX-XXX-XXX`) and authorization token/approval. View multiple monitors, send input, sync clipboard, transfer files, and chat.
- **Host Mode**: Share your 9-digit Device ID with a remote operator. Accept incoming connection requests manually with granular permission toggles or enable unattended access for trusted devices.

### 2.2 Unattended Access & Trusted Device Pairing
- **Zero-Touch Access**: Allows trusted controller machines (e.g., your primary workstation in India) to connect directly to an unattended host (e.g., your PC or server in Dubai) without human intervention on the remote end.
- **Cryptographic Identity Verification**: 
  - Pairing exchanges an Ed25519 public key.
  - During connection, the host challenges the controller with a random 256-bit cryptographic nonce.
  - The controller signs the nonce with its private key stored securely in Windows DPAPI (`DataProtectionScope.CurrentUser`).
  - Access is granted only upon mathematical proof of key ownership—passwords cannot be snooped or brute-forced.

### 2.3 Low-Latency Screen Capture & Video Pipeline
- **DXGI Desktop Duplication API (DDA)**: Direct GPU surface frame acquisition (< 2ms capture latency).
- **Windows Graphics Capture (WGC) Fallback**: Seamless multi-monitor handling and window capture without display-switch hitches.
- **Hardware-Accelerated H.264 MFT Encoder**: Integrates directly with Windows Media Foundation Transforms (MFT) to leverage NVENC (NVIDIA), QuickSync (Intel), or AMF (AMD) hardware encoders with real-time dynamic IDR keyframe insertion.
- **Adaptive Performance Presets**:
  - **Speed**: Optimized for constrained network links (15 FPS, aggressive bitrate capping).
  - **Balanced**: Ideal for general productivity and daily workflows (30 FPS, smooth motion).
  - **Quality**: High-fidelity mode for precision work (60 FPS, high bitrate, minimal compression artifacts).

### 2.4 Remote Input Injection & Multi-Monitor Support
- **Win32 `SendInput` Engine**: Translates remote mouse movements, clicks, scrolls, and key combinations with sub-millisecond local processing.
- **Normalized Virtual Desktop Coordinates**: Translates viewport pixel coordinates to virtual multi-monitor normalized coordinates (`0..65535`), correctly handling multi-monitor topologies with negative offsets and mixed DPI scaling.
- **UAC / Secure Desktop Awareness**: Alerts the controller if a privileged Windows UAC prompt is displayed.

### 2.5 Resumable File Transfer & Clipboard Synchronization
- **Chunked DataChannel File Transfer**: Files are transferred via multiplexed SCTP WebRTC DataChannels in 64 KB chunks with backpressure handling (`BufferedAmountLow`).
- **Data Integrity & Safety**:
  - Automatic SHA-256 hash calculation and verification upon completion.
  - Path traversal protection (blocks `..`, illegal characters, and absolute target paths).
- **Bidirectional Clipboard Sync**:
  - Automatically syncs text clipboard between Controller and Host.
  - SHA-256 loopback suppression prevents infinite clipboard echo loops.
  - Configurable 1 MB payload limits with user toggles.

### 2.6 Native Windows Lifecycle & System Tray Integration
- **System Tray Minimization**: Closing the application window minimizes ControlDesk to the Windows Notification Area (System Tray), keeping active host listeners and unattended access alive without cluttering the taskbar.
- **Windows Startup**: One-click configuration to automatically launch on Windows user login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **Single-Instance Enforcement**: Utilizes a Windows named mutex (`Global\ControlDesk_SingleInstanceMutex`) to prevent duplicate instances and bring existing windows into focus upon re-launch.
- **Power Management (`SetThreadExecutionState`)**: Automatically prevents the host computer from entering sleep or suspension mode while an active remote session is underway.

### 2.7 In-Session Toolbar & Live Diagnostics
During an active session, a sleek, floating toolbar provides instant access to:
- **Monitor Switcher**: Switch between Display 1, Display 2, or spanned view.
- **Quality Selector**: Switch between Speed, Balanced, and Quality on the fly.
- **Input Controls**: Toggle remote input control on/off without dropping the view.
- **File Transfer Manager**: Send files and monitor transfer queues.
- **Session Chat**: Real-time text messaging between controller and host.
- **HUD Diagnostics**: View real-time RTT (Round Trip Time), current FPS, incoming bitrate, and transport protocol (Direct P2P vs TURN Relay).

---

## 3. Security Architecture & Threat Model

| Security Layer | Implementation Mechanism | Purpose |
| :--- | :--- | :--- |
| **Transport Encryption** | DTLS-SRTP & SCTP over DTLS | All video, audio, input, and data channels are encrypted end-to-end using AES-128-GCM. |
| **Device Identification** | SHA-256 truncated device IDs | Identifies endpoints without disclosing IP addresses or hostnames. |
| **Local Secret Storage** | Windows DPAPI (`CryptProtectData`) | Private keys and paired device tokens are encrypted using Windows user account credentials. |
| **Unattended Authentication** | Ed25519 Nonce Signature Verification | Eliminates static password attacks; zero credential transmission over the wire. |
| **Audit Logging** | SQLite Local Audit Trail | Logs every connection attempt, device ID, timestamp, and permission granted locally. |

---

## 4. Cross-Border Deployment (India to Dubai)

For reliable connectivity across international boundaries:
1. **Direct P2P (STUN)**: If both endpoints have friendly NATs (Full Cone or Restricted Cone), connections are established directly peer-to-peer with lowest possible latency.
2. **TURN Relay Fallback**: In cross-border scenarios where ISPs implement Symmetric NAT or Carrier-Grade NAT (CGNAT), WebRTC automatically relays DTLS-encrypted media and data packets through an intermediate `coturn` server hosted on a low-latency VPS (such as Bahrain, Oman, or Germany), guaranteeing 100% connection success.

---

## 5. Summary of Included Standalone Binaries

1. **`ControlDesk.exe`**:
   - The unified dual-role client application.
   - Completely self-contained single-file executable with embedded high-resolution application icon.
   - Zero external .NET runtime installation required.
2. **`ControlDesk-Setup.exe`**:
   - The official installer wizard.
   - Includes Welcome, Terms of Service (EULA), Privacy Policy, Installation Location, and Auto-Start options.
   - Deploys ControlDesk to `AppData\Local\Programs\ControlDesk` and creates Desktop & Start Menu shortcuts.

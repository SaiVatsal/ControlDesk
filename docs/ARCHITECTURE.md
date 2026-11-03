# ControlDesk Architecture Specification

**Version**: 1.0.0  
**Target Platform**: Windows 10 (21H2+) / Windows 11 x64  
**Runtime**: .NET 10.0 (C# 13, Native AOT / Single-File self-contained)

---

## 1. System Overview

ControlDesk is an enterprise-grade, peer-to-peer Windows remote desktop system engineered for cross-border connectivity between India (Controller) and Dubai (Host). The architecture eliminates artificial session limits, proprietary cloud lock-in, and complex VPN setups through standardized WebRTC ICE traversal, hardware-accelerated video pipelines, and cryptographic mutual authentication.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             ControlDesk.App                                  │
│  ┌───────────────────────┐  ┌───────────────────────┐  ┌──────────────────┐ │
│  │   WPF View Layer      │  │  MVVM ViewModels      │  │  App Lifecycle   │ │
│  │  (Zero Animations,    │◄─┼─ (CommunityToolkit)   │◄─┼─ (Tray, Mutex,   │ │
│  │   Dark/Light Theme)   │  │                       │  │   Power State)   │ │
│  └───────────────────────┘  └───────────┬───────────┘  └──────────────────┘ │
└─────────────────────────────────────────┼───────────────────────────────────┘
                                          │
┌─────────────────────────────────────────▼───────────────────────────────────┐
│                            ControlDesk.Core                                 │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                    SessionCoordinator (State Machine)                 │  │
│  │  Disconnected ──► Connecting ──► Authenticating ──► Streaming ──► ... │  │
│  └───────┬──────────────────┬───────────────────┬───────────────────┬────┘  │
└──────────┼──────────────────┼───────────────────┼───────────────────┼───────┘
           │                  │                   │                   │
┌──────────▼───────┐  ┌───────▼────────┐  ┌───────▼────────┐  ┌───────▼───────┐
│Capture & Media   │  │Input Subsystem │  │Security & Auth │  │Network Layer  │
│                  │  │                │  │                │  │               │
│DXGI Duplication  │  │Win32 SendInput │  │Ed25519 Keys    │  │WebRTC Peer    │
│WGC Fallback      │  │Multi-monitor   │  │DPAPI Storage   │  │SCTP Channels  │
│MFT H.264 Encoder │  │Coord Normalizer│  │Challenge/Resp  │  │coturn Relay   │
│SIPSorcery Adapter│  │Permission Gate │  │HMAC Audit Log  │  │WebSocket Sig  │
└──────────────────┘  └────────────────┘  └────────────────┘  └───────────────┘
```

---

## 2. Core Subsystems & Project Decoupling

The codebase strictly adheres to clean separation of concerns across 11 projects:

### 2.1 `ControlDesk.Shared`
- **Role**: Pure protocol contracts, serialization models, and wire DTOs.
- **Dependencies**: None (pure .NET 10.0).
- **Key Types**:
  - `SignalingMessage<T>`: Universal signaling envelope with polymorphic payloads.
  - `DataChannelPacket`: Binary framed wire protocol for SCTP data channels.
  - Payloads: `RegisterDevicePayload`, `SessionOfferPayload`, `SessionAnswerPayload`, `IceCandidatePayload`, `ChallengeRequestPayload`, `ChallengeResponsePayload`.

### 2.2 `ControlDesk.Core`
- **Role**: Domain orchestrator, session state machine, and service abstractions.
- **Dependencies**: `ControlDesk.Shared`.
- **Key Types**:
  - `SessionCoordinator`: Central manager coordinating capture, encoding, input, network, and security.
  - `SessionState`: Finite state machine (`Disconnected`, `ConnectingToSignaling`, `RegisteredWithSignaling`, `RequestingSession`, `Authenticating`, `ExchangingSdp`, `GatheringCandidates`, `EstablishingPeerConnection`, `Connected`, `Streaming`, `Reconnecting`, `Disconnecting`, `Terminated`, `Failed`).
  - Core interfaces: `IScreenCaptureService`, `IMediaEncoder`, `IInputInjector`, `ITransferManager`.

### 2.3 `ControlDesk.Security`
- **Role**: Device identity, asymmetric cryptography, secure secret persistence, and audit logging.
- **Dependencies**: `ControlDesk.Shared`, `ControlDesk.Core`, `ControlDesk.Storage`.
- **Key Types**:
  - `DeviceIdentityService`: Generates 9-digit canonical device IDs (`XXX-XXX-XXX`) and manages Ed25519 keypairs.
  - `WindowsDpapiCredentialStore`: Cryptographically protects private keys using Windows DPAPI (`DataProtectionScope.CurrentUser`) with device-unique entropy.
  - `ChallengeResponseAuthenticator`: Nonce generation, signing, and signature verification.
  - `AuditLogger`: Tamper-evident, hash-chained security log using HMAC-SHA256.

### 2.4 `ControlDesk.Storage`
- **Role**: Local high-performance persistence via SQLite.
- **Dependencies**: `Microsoft.Data.Sqlite`, `ControlDesk.Shared`.
- **Key Types**:
  - `SqliteSettingsRepository`: User settings, network configuration, and display preferences.
  - `SqliteDeviceRepository`: Known devices and trusted device pairing records.
  - `SqliteSessionHistoryRepository`: Historic connection logs with durations and transport metrics.

### 2.5 `ControlDesk.Capture`
- **Role**: GPU-accelerated desktop surface capture.
- **Dependencies**: `ControlDesk.Core`, Windows Desktop Duplication API (DXGI / Direct3D 11).
- **Key Types**:
  - `DxgiCaptureService`: Utilizes `IDXGIOutputDuplication` for sub-millisecond frame acquisition directly from GPU VRAM.
  - `MonitorManager`: Enumerates virtual monitors, multi-head displays, and per-monitor DPI scale factors.
  - Ring Buffer: Single-element buffer strategy ensures zero stale queued frames, eliminating presentation lag.

### 2.6 `ControlDesk.Media`
- **Role**: Hardware-accelerated video encoding and WebRTC media stream feeding.
- **Dependencies**: `ControlDesk.Core`, Windows Media Foundation Transform (MFT).
- **Key Types**:
  - `MfH264Encoder`: Hardware encoder utilizing Intel QuickSync, NVIDIA NVENC, or AMD AMF with baseline profile, CBR/VBR, low latency mode (`MF_LOW_LATENCY = TRUE`), and dynamic IDR/I-frame keyframe injection.
  - `SoftwareVideoEncoder`: Compact fallback encoder for headless systems or generic display adapters.
  - `WebRtcVideoSource`: SIPSorcery `IVideoSource` implementation packaging NAL units into RTP packets.

### 2.7 `ControlDesk.Input`
- **Role**: Client-side input event capture and host-side Win32 injection.
- **Dependencies**: `ControlDesk.Core`, `ControlDesk.Shared`, `P/Invoke User32`.
- **Key Types**:
  - `Win32InputInjector`: Executes native `SendInput` for mouse and keyboard events.
  - Multi-Monitor Normalizer: Maps client normalized coordinates `(0.0..1.0)` to Windows Virtual Desktop coordinates `(0..65535)` supporting asymmetric and negative-coordinate monitor arrangements.

### 2.8 `ControlDesk.Network`
- **Role**: WebRTC connection lifecycle, ICE candidate management, and WebSocket signaling.
- **Dependencies**: `SIPSorcery`, `ControlDesk.Core`, `ControlDesk.Shared`.
- **Key Types**:
  - `WebRtcSessionManager`: Wraps `RTCPeerConnection`, manages SDP offer/answer exchange, STUN/TURN servers, and ICE gathering.
  - DataChannel Multiplexer: Splits communications across 3 SCTP channels (`control`, `transfer`, `chat_heartbeat`).

### 2.9 `ControlDesk.Transfer`
- **Role**: High-speed chunked file transfers, clipboard synchronization, and text messaging.
- **Dependencies**: `ControlDesk.Core`, `ControlDesk.Shared`.
- **Key Types**:
  - `TransferService`: 64 KB chunking, backpressure flow control, SHA-256 integrity validation, path traversal defenses.
  - Clipboard Service: Synchronizes text up to 1 MB with SHA-256 hashing to suppress feedback loops.

### 2.10 `ControlDesk.Server`
- **Role**: Independent ASP.NET Core WebSocket signaling server and coturn ephemeral TURN token dispenser.
- **Dependencies**: `ControlDesk.Shared`.
- **Key Types**:
  - `SignalingWebSocketHandler`: In-memory concurrent device registry, presence tracking, and message routing.
  - Ephemeral TURN Credential Generator: HMAC-SHA1 timestamped token generation compatible with coturn REST API.

### 2.11 `ControlDesk.App`
- **Role**: Native Windows WPF UI, MVVM bindings, system tray, and OS integration.
- **Dependencies**: All client subsystems, `CommunityToolkit.Mvvm`.
- **Key Features**:
  - Zero-animation high-performance rendering.
  - Neutral dark/light palettes.
  - Per-user startup registration (`HKCU\Run`).
  - Named single-instance mutex.
  - Win32 `SetThreadExecutionState` continuous power assertion.

---

## 3. Detailed Data Flows & Sequences

### 3.1 Initial Device Registration & Presence

```
Client (India/Dubai)                    Signaling Server (VPS)
        │                                        │
        ├── Connect WSS: wss://signaling/ws ────►│
        │                                        ├── Accept WebSocket
        ├── RegisterDevicePayload ──────────────►│
        │   (DeviceId, PublicKey, Caps)          ├── Store in DeviceRegistry
        │                                        ├── Broadcast DevicePresence(Online)
        │◄── DevicePresencePayload (Self) ───────┤
        │                                        │
```

### 3.2 Attended Connection Handshake

```
Controller (India)              Signaling Server (VPS)              Host (Dubai)
        │                                │                                │
        ├── SessionOffer (SDP) ─────────►│                                │
        │   (Target: Dubai)              ├── Route SessionOffer ─────────►│
        │                                │                                ├── Prompt User:
        │                                │                                │   "Allow connection?"
        │                                │                                ├── User clicks "Accept"
        │                                │◄── SessionAnswer (SDP) ────────┤
        │◄── Route SessionAnswer ────────┤    (Accepted: true)            │
        │                                │                                │
        ├── IceCandidate ───────────────►├── Route IceCandidate ─────────►│
        │◄── Route IceCandidate ─────────┼── IceCandidate ────────────────┤
        │                                │                                │
        │══════════════════════ WebRTC Direct P2P / TURN ═════════════════│
        │                                                                 │
```

### 3.3 Unattended Connection Handshake (India → Dubai)

```
Controller (India)              Signaling Server (VPS)              Host (Dubai)
        │                                │                                │
        ├── SessionOffer (Unattended) ──►│                                │
        │                                ├── Route SessionOffer ─────────►│
        │                                │                                ├── Check TrustedDevices:
        │                                │                                │   Found matching Controller ID?
        │                                │◄── ChallengeRequest (Nonce) ───┤
        │◄── Route ChallengeRequest ─────┤                                │
        ├── Sign Nonce with Ed25519      │                                │
        ├── ChallengeResponse (Sig) ────►│                                │
        │                                ├── Route ChallengeResponse ────►│
        │                                │                                ├── Verify Ed25519 Sig
        │                                │                                │   against stored public key
        │                                │◄── SessionAnswer (SDP Accepted)┤
        │◄── Route SessionAnswer ────────┤                                │
        │══════════════════════ WebRTC Direct P2P / TURN ═════════════════│
```

### 3.4 In-Session Media & Data Multiplexing

```
      Controller                                            Host
 ┌──────────────────┐                              ┌──────────────────┐
 │ ActiveSessionView│                              │ DxgiCapture      │
 │ Viewport Canvas  │◄────── Video Track (RTP) ────│ MfH264Encoder    │
 └────────┬─────────┘                              └──────────────────┘
          │
          │         ┌──────────────────────────────┐
          ├────────►│ DataChannel: "control"       ├──► Win32InputInjector
          │         │ (Mouse, Keyboard, Display)   │
          │         ├──────────────────────────────┤
          ├────────►│ DataChannel: "transfer"      ├──► TransferService
          │         │ (64KB Chunks, Clipboard)     │
          │         ├──────────────────────────────┤
          └────────►│ DataChannel: "chat_heartbeat"├──► InSessionChat
                    │ (Ping/Pong Probes, Messages) │
                    └──────────────────────────────┘
```

---

## 4. Hardware Acceleration Pipeline

### 4.1 Screen Capture
1. **GPU Acquisition**: `IDXGIOutputDuplication::AcquireNextFrame` locks the active desktop frame buffer directly in GPU VRAM without CPU copies.
2. **Surface Extraction**: Extracts `ID3D11Texture2D` in `DXGI_FORMAT_B8G8R8A8_UNORM`.
3. **Cursor Blitting**: If the hardware cursor is moved or modified, the shape and position are obtained via `DXGI_OUTDUPL_FRAME_INFO` and composited directly onto the texture surface.
4. **Latency Budget**: Total capture cycle time is kept under **3 ms**.

### 4.2 Video Encoding
1. **Direct3D 11 / MFT Binding**: The GPU texture is passed directly into the Media Foundation Transform (`IMFTransform`) hardware encoder.
2. **Encoder Configurations**:
   - Codec: H.264 / AVC (Baseline or Main Profile).
   - Rate Control: Low-latency CBR with VBR peak smoothing.
   - Low Latency Mode: `MF_LOW_LATENCY = TRUE`, `CODECAPI_AVLowLatencyMode = 1`.
   - GOP Structure: Infinite GOP with on-demand IDR/keyframe injection when packet loss or reconnection occurs.
3. **Adaptive Bitrate Engine**: Dynamically shifts bitrate across presets:
   - *Speed*: 15 FPS, 1.5 Mbps (ideal for unstable 3G/4G cross-border links).
   - *Balanced*: 30 FPS, 4.0 Mbps (recommended default India ↔ Dubai).
   - *Quality*: 60 FPS, 10.0 Mbps (high-speed fiber connections).

---

## 5. Security & Isolation Architecture

1. **Process Isolation**: The application executes purely in standard user context (`asInvoker`). Administrative elevation is never requested unless explicitly launched as Administrator by the user for managing elevated Windows applications.
2. **DPAPI Key Isolation**: Cryptographic private keys are protected with DPAPI scoped strictly to the current user SID and bound with a machine-unique entropy hash, preventing offline memory extraction across accounts.
3. **Zero Third-Party Telemetry**: The binary contains zero analytic SDKs, zero crash telemetry collectors, and zero background HTTP polling.
4. **Network Whitelisting**: Outbound connections occur strictly to the configured Signaling domain and TURN relay servers over TLS/DTLS.

---

*ControlDesk Architecture Specification — Enterprise Cross-Border Remote Access*
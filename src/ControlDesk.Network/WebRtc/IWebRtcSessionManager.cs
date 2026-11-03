using System;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Core.Sessions;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Network.WebRtc;

/// <summary>
/// Interface for managing WebRTC peer connections, signaling, and data channels.
/// </summary>
public interface IWebRtcSessionManager : IAsyncDisposable
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Remote device ID if connected.
    /// </summary>
    string? RemoteDeviceId { get; }

    /// <summary>
    /// Current transport type (Direct, Stun, Turn).
    /// </summary>
    TransportType TransportType { get; }

    /// <summary>
    /// Event fired when connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Event fired when an incoming session request is received.
    /// </summary>
    event EventHandler<IncomingSessionEventArgs>? IncomingSession;

    /// <summary>
    /// Event fired when a data channel message is received.
    /// </summary>
    event EventHandler<DataChannelMessageEventArgs>? DataChannelMessageReceived;

    /// <summary>
    /// Event fired when connection diagnostics are updated.
    /// </summary>
    event EventHandler<ConnectionDiagnostics>? DiagnosticsUpdated;

    /// <summary>
    /// Connects to the signaling server and registers this device.
    /// </summary>
    /// <param name="signalingUrl">WebSocket URL of the signaling server (e.g., wss://example.com/ws).</param>
    /// <param name="deviceId">This device's formatted ID.</param>
    /// <param name="publicKeyBase64">This device's Ed25519 public key in Base64.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registration succeeded.</returns>
    Task<bool> ConnectToSignalingAsync(
        string signalingUrl,
        string deviceId,
        string publicKeyBase64,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a connection to a remote device.
    /// </summary>
    /// <param name="targetDeviceId">Target device's formatted ID.</param>
    /// <param name="accessMode">Requested access mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection request was sent.</returns>
    Task<bool> ConnectAsync(
        string targetDeviceId,
        AccessMode accessMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts an incoming session request.
    /// </summary>
    /// <param name="grantedPermissions">Permissions to grant the remote device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if accepted and connection initiated.</returns>
    Task<bool> AcceptIncomingSessionAsync(
        SessionPermissions grantedPermissions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects an incoming session request.
    /// </summary>
    /// <param name="reason">Reason for rejection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if rejection was sent.</returns>
    Task<bool> RejectIncomingSessionAsync(
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the current session gracefully.
    /// </summary>
    /// <param name="reason">Optional reason for disconnection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an input event (mouse/keyboard) over the control data channel.
    /// </summary>
    /// <param name="inputPacket">Serialized input packet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendInputEventAsync(ReadOnlyMemory<byte> inputPacket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a display configuration update over the control data channel.
    /// </summary>
    /// <param name="displayConfig">Display configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendDisplayConfigAsync(DisplayConfig displayConfig, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a file transfer control message.
    /// </summary>
    /// <param name="controlPacket">File transfer control packet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendFileTransferControlAsync(FileTransferControlPacket controlPacket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a file data chunk.
    /// </summary>
    /// <param name="chunk">File chunk data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendFileChunkAsync(FileChunkPacket chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a clipboard sync message.
    /// </summary>
    /// <param name="clipboardData">Clipboard content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendClipboardSyncAsync(ClipboardSyncPacket clipboardData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a chat message.
    /// </summary>
    /// <param name="message">Chat message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendChatMessageAsync(ChatMessagePacket message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a heartbeat ping.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendHeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests an ICE restart to recover from connectivity issues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RequestIceRestartAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// WebRTC connection states.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    ConnectingToSignaling,
    RegisteredWithSignaling,
    RequestingSession,
    Authenticating,
    ExchangingSdp,
    GatheringCandidates,
    EstablishingPeerConnection,
    Connected,
    Streaming,
    Reconnecting,
    Disconnecting,
    Terminated,
    Failed
}

/// <summary>
/// Transport type for the WebRTC connection.
/// </summary>
public enum TransportType
{
    Unknown,
    Direct,
    StunReflexive,
    TurnRelay
}

/// <summary>
/// Connection state change event args.
/// </summary>
public record ConnectionStateChangedEventArgs(
    ConnectionState PreviousState,
    ConnectionState CurrentState,
    string? Reason = null
);

/// <summary>
/// Incoming session event args.
/// </summary>
public record IncomingSessionEventArgs(
    string RemoteDeviceId,
    string RemotePublicKeyBase64,
    string RemoteDeviceName,
    AccessMode RequestedAccessMode,
    SessionPermissions RequestedPermissions
);

/// <summary>
/// Data channel message event args.
/// </summary>
public record DataChannelMessageEventArgs(
    string ChannelName,
    ReadOnlyMemory<byte> Data
);

/// <summary>
/// Connection diagnostics.
/// </summary>
public record ConnectionDiagnostics(
    TransportType TransportType,
    TimeSpan RoundTripTime,
    double PacketLossPercent,
    long BytesSent,
    long BytesReceived,
    long CurrentBitrateBps,
    int FrameRate,
    DateTimeOffset LastUpdateUtc
);
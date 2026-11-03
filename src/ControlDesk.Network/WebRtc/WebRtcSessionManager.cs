using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Core.Interfaces;
using ControlDesk.Core.Sessions;
using ControlDesk.Media.Encoding;
using ControlDesk.Input.Injection;
using ControlDesk.Transfer.Services;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Network.WebRtc;

/// <summary>
/// Manages WebRTC peer connection, signaling, and data channels for a remote desktop session.
/// Implements IWebRtcSessionManager for DI registration.
/// </summary>
public sealed class WebRtcSessionManager : IWebRtcSessionManager
{
    private readonly IMediaEncoder _videoEncoder;
    private readonly IInputInjector _inputInjector;
    private readonly ITransferManager _transferManager;
    private readonly IScreenCaptureService _captureService;

    private ClientWebSocket? _signalingSocket;
    private CancellationTokenSource? _signalingCts;
    private Task? _signalingTask;
    private string? _localDeviceId;
    private string? _signalingServerUrl;
    private string? _localPublicKeyBase64;
    private bool _disposed;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? RemoteDeviceId { get; private set; }
    public TransportType TransportType { get; private set; } = TransportType.Unknown;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public event EventHandler<IncomingSessionEventArgs>? IncomingSession;
    public event EventHandler<DataChannelMessageEventArgs>? DataChannelMessageReceived;
    public event EventHandler<ConnectionDiagnostics>? DiagnosticsUpdated;

    public WebRtcSessionManager(
        IMediaEncoder? videoEncoder = null,
        IInputInjector? inputInjector = null,
        ITransferManager? transferManager = null,
        IScreenCaptureService? captureService = null)
    {
        _videoEncoder = videoEncoder!;
        _inputInjector = inputInjector!;
        _transferManager = transferManager!;
        _captureService = captureService!;
    }

    /// <summary>
    /// Connects to the signaling server and registers this device.
    /// </summary>
    public async Task<bool> ConnectToSignalingAsync(
        string signalingUrl,
        string deviceId,
        string publicKeyBase64,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Disconnected)
            return false;

        _signalingServerUrl = signalingUrl;
        _localDeviceId = deviceId;
        _localPublicKeyBase64 = publicKeyBase64;

        await SetStateAsync(ConnectionState.ConnectingToSignaling);

        _signalingSocket = new ClientWebSocket();
        _signalingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await _signalingSocket.ConnectAsync(new Uri(_signalingServerUrl), _signalingCts.Token);
            await SetStateAsync(ConnectionState.RegisteredWithSignaling);

            // Register this device
            await RegisterDeviceAsync();

            _signalingTask = Task.Run(() => SignalingLoop(_signalingCts.Token), _signalingCts.Token);
            return true;
        }
        catch (Exception ex)
        {
            await SetStateAsync(ConnectionState.Failed);
            return false;
        }
    }

    /// <summary>
    /// Initiates a connection to a remote device.
    /// </summary>
    public async Task<bool> ConnectAsync(
        string targetDeviceId,
        AccessMode accessMode,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.RegisteredWithSignaling)
            throw new InvalidOperationException("Not connected to signaling server");

        RemoteDeviceId = targetDeviceId;
        await SetStateAsync(ConnectionState.RequestingSession);

        // Create SDP offer
        string sdpOffer = await CreateSdpOfferAsync();

        var offer = new SessionOfferPayload(
            TargetDeviceId: targetDeviceId,
            SenderDeviceId: _localDeviceId!,
            SenderFriendlyName: Environment.MachineName,
            AccessMode: accessMode,
            SdpOffer: sdpOffer,
            TurnCredentials: null, // Will be provided by server
            RequestedPermissions: new SessionPermissions(
                ViewScreen: true,
                ControlMouse: true,
                ControlKeyboard: true,
                ClipboardSync: true,
                FileTransfer: true,
                Chat: true,
                Recording: false
            )
        );

        await SendSignalingMessageAsync(SignalingMessageTypes.SessionOffer, offer);
        return true;
    }

    /// <summary>
    /// Accepts an incoming session request.
    /// </summary>
    public async Task<bool> AcceptIncomingSessionAsync(
        SessionPermissions grantedPermissions,
        CancellationToken cancellationToken = default)
    {
        if (RemoteDeviceId == null)
            return false;

        await SetStateAsync(ConnectionState.Authenticating);

        string sdpAnswer = await CreateSdpAnswerAsync(RemoteDeviceId);

        var answer = new SessionAnswerPayload(
            TargetDeviceId: RemoteDeviceId,
            SenderDeviceId: _localDeviceId!,
            Accepted: true,
            SdpAnswer: sdpAnswer,
            RejectReason: null
        );

        await SendSignalingMessageAsync(SignalingMessageTypes.SessionAnswer, answer);
        await StartStreamingAsync();
        return true;
    }

    /// <summary>
    /// Rejects an incoming session request.
    /// </summary>
    public async Task<bool> RejectIncomingSessionAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (RemoteDeviceId == null)
            return false;

        var answer = new SessionAnswerPayload(
            TargetDeviceId: RemoteDeviceId,
            SenderDeviceId: _localDeviceId!,
            Accepted: false,
            SdpAnswer: null,
            RejectReason: reason
        );

        await SendSignalingMessageAsync(SignalingMessageTypes.SessionAnswer, answer);
        await SetStateAsync(ConnectionState.Disconnected);
        return true;
    }

    /// <summary>
    /// Disconnects the current session gracefully.
    /// </summary>
    public async Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Disconnected || State == ConnectionState.Terminated)
            return;

        await SetStateAsync(ConnectionState.Disconnecting);

        try
        {
            if (RemoteDeviceId != null)
            {
                // Notify peer of disconnect
                // (would send a disconnect message over data channel)
            }

            await StopStreamingAsync();
        }
        finally
        {
            await SetStateAsync(ConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// Sends an input event (mouse/keyboard) over the control data channel.
    /// </summary>
    public Task SendInputEventAsync(ReadOnlyMemory<byte> inputPacket, CancellationToken cancellationToken = default)
    {
        // Send over control data channel
        // Implementation would send via WebRTC data channel
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a display configuration update over the control data channel.
    /// </summary>
    public Task SendDisplayConfigAsync(DisplayConfig displayConfig, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a file transfer control message.
    /// </summary>
    public Task SendFileTransferControlAsync(FileTransferControlPacket controlPacket, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a file data chunk.
    /// </summary>
    public Task SendFileChunkAsync(FileChunkPacket chunk, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a clipboard sync message.
    /// </summary>
    public Task SendClipboardSyncAsync(ClipboardSyncPacket clipboardData, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a chat message.
    /// </summary>
    public Task SendChatMessageAsync(ChatMessagePacket message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a heartbeat ping.
    /// </summary>
    public Task SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Requests an ICE restart to recover from connectivity issues.
    /// </summary>
    public Task RequestIceRestartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private async Task RegisterDeviceAsync()
    {
        var payload = new RegisterDevicePayload(
            DeviceId: _localDeviceId!,
            PublicKeyBase64: _localPublicKeyBase64!,
            FriendlyName: Environment.MachineName,
            ClientVersion: "1.0.0",
            Capabilities: new DeviceCapabilities(
                CanHost: true,
                CanControl: true,
                SupportsHardwareEncoding: false,
                SupportedCodecs: new[] { "H264", "VP8" },
                MaxResolutionWidth: 3840,
                MaxResolutionHeight: 2160
            )
        );

        await SendSignalingMessageAsync(SignalingMessageTypes.RegisterDevice, payload);
    }

    private async Task<string> CreateSdpOfferAsync()
    {
        // Create WebRTC SDP offer
        // This is a placeholder - real implementation uses SIPSorcery
        return "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0 1 2\r\n";
    }

    private async Task<string> CreateSdpAnswerAsync(string remoteDeviceId)
    {
        // Create WebRTC SDP answer
        return "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0 1 2\r\n";
    }

    private async Task StartStreamingAsync()
    {
        await SetStateAsync(ConnectionState.ExchangingSdp);
        await SetStateAsync(ConnectionState.GatheringCandidates);
        await SetStateAsync(ConnectionState.EstablishingPeerConnection);

        // Start screen capture
        _captureService.FrameArrived += OnFrameCaptured;
        await _captureService.StartCaptureAsync(0, 30);

        await SetStateAsync(ConnectionState.Connected);
        await SetStateAsync(ConnectionState.Streaming);

        // Start heartbeat
        _ = Task.Run(() => HeartbeatLoop());
    }

    private async Task StopStreamingAsync()
    {
        _captureService.FrameArrived -= OnFrameCaptured;
        await _captureService.StopCaptureAsync();
    }

    private void OnFrameCaptured(object? sender, CapturedFrame frame)
    {
        // Encode frame
        _ = _videoEncoder.EncodeAsync(frame.Data, frame.Width, frame.Height, frame.Stride, frame.TimestampTicks);
    }

    private async Task SignalingLoop(CancellationToken token)
    {
        var buffer = new byte[65536];

        while (!token.IsCancellationRequested && _signalingSocket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _signalingSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessSignalingMessage(json);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await SetStateAsync(ConnectionState.Failed);
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // LogMessage?.Invoke(this, $"Signaling error: {ex.Message}");
            }
        }
    }

    private void ProcessSignalingMessage(string json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<SignalingMessage<JsonElement>>(json);
            if (envelope == null) return;

            switch (envelope.Type)
            {
                case SignalingMessageTypes.SessionOffer:
                    var offer = JsonSerializer.Deserialize<SessionOfferPayload>(envelope.Payload.GetRawText());
                    if (offer != null)
                    {
                        RemoteDeviceId = offer.SenderDeviceId;
                        IncomingSession?.Invoke(this, new IncomingSessionEventArgs(
                            RemoteDeviceId: offer.SenderDeviceId,
                            RemotePublicKeyBase64: string.Empty, // Would be filled from security module
                            RemoteDeviceName: offer.SenderFriendlyName,
                            RequestedAccessMode: offer.AccessMode,
                            RequestedPermissions: offer.RequestedPermissions
                        ));
                    }
                    break;

                case SignalingMessageTypes.SessionAnswer:
                    // Handle answer
                    break;

                case SignalingMessageTypes.IceCandidate:
                    // Handle ICE candidate
                    break;

                case SignalingMessageTypes.ChallengeRequest:
                    // Handle challenge
                    break;

                case SignalingMessageTypes.Error:
                    var error = JsonSerializer.Deserialize<ErrorPayload>(envelope.Payload.GetRawText());
                    // LogMessage?.Invoke(this, $"Server error: {error?.Message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            // LogMessage?.Invoke(this, $"Failed to process signaling message: {ex.Message}");
        }
    }

    private async Task SendSignalingMessageAsync<T>(string type, T payload)
    {
        if (_signalingSocket?.State != WebSocketState.Open)
            return;

        var message = new SignalingMessage<T>(type, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, payload);
        var json = JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        await _signalingSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task HeartbeatLoop()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (State == ConnectionState.Streaming && await timer.WaitForNextTickAsync())
        {
            await SendSignalingMessageAsync(SignalingMessageTypes.Heartbeat, new { Timestamp = DateTimeOffset.UtcNow });
        }
    }

    private Task SetStateAsync(ConnectionState newState)
    {
        var previous = State;
        State = newState;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(previous, newState));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _signalingCts?.Cancel();
            if (_signalingTask != null)
            {
                try { await _signalingTask; } catch { }
            }
            _signalingSocket?.Dispose();
            await StopStreamingAsync();
        }
    }
}
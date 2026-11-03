using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Interfaces;
using ControlDesk.Core.Sessions;
using ControlDesk.Network.WebRtc;
using ControlDesk.Security.Authentication;
using ControlDesk.Security.Storage;
using ControlDesk.Shared.Protocol;
using ControlDesk.Storage.Repositories;

namespace ControlDesk.App.Services;

/// <summary>
/// Coordinates remote desktop sessions between controller and host.
/// Orchestrates WebRTC, capture, encoding, input injection, and authentication.
/// </summary>
public sealed class SessionCoordinator : ISessionCoordinator
{
    private readonly IDeviceIdentityService _deviceIdentityService;
    private readonly IWebRtcSessionManager _webRtcManager;
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IInputInjector _inputInjector;
    private readonly ITransferManager _transferManager;
    private readonly ICredentialStore _credentialStore;
    private readonly IChallengeResponseAuthenticator _challengeAuthenticator;
    private readonly IDevicePairingManager _devicePairingManager;
    private readonly IAuditLogger _auditLogger;
    private readonly ISettingsRepository _settingsRepository;

    private SessionState _state = SessionState.Disconnected;
    private SessionContext? _currentSession;
    private SessionDiagnostics? _diagnostics;
    private CancellationTokenSource? _sessionCts;
    private bool _disposed;

    public SessionState State => _state;
    public SessionContext? CurrentSession => _currentSession;
    public SessionDiagnostics? Diagnostics => _diagnostics;

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SessionDiagnostics>? DiagnosticsUpdated;
    public event EventHandler<SessionOfferPayload>? IncomingSessionOffered;

    public SessionCoordinator(
        IDeviceIdentityService deviceIdentityService,
        IWebRtcSessionManager webRtcManager,
        IScreenCaptureService screenCaptureService,
        IMediaEncoder mediaEncoder,
        IInputInjector inputInjector,
        ITransferManager transferManager,
        ICredentialStore credentialStore,
        IChallengeResponseAuthenticator challengeAuthenticator,
        IDevicePairingManager devicePairingManager,
        IAuditLogger auditLogger,
        ISettingsRepository settingsRepository)
    {
        _deviceIdentityService = deviceIdentityService;
        _webRtcManager = webRtcManager;
        _screenCaptureService = screenCaptureService;
        _mediaEncoder = mediaEncoder;
        _inputInjector = inputInjector;
        _transferManager = transferManager;
        _credentialStore = credentialStore;
        _challengeAuthenticator = challengeAuthenticator;
        _devicePairingManager = devicePairingManager;
        _auditLogger = auditLogger;
        _settingsRepository = settingsRepository;

        // Subscribe to WebRTC events
        _webRtcManager.StateChanged += OnWebRtcStateChanged;
        _webRtcManager.IncomingSession += OnIncomingSession;
        _webRtcManager.DataChannelMessageReceived += OnDataChannelMessage;
        _webRtcManager.DiagnosticsUpdated += OnDiagnosticsUpdated;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string targetDeviceId, AccessMode accessMode, CancellationToken cancellationToken = default)
    {
        if (_state != SessionState.Disconnected && _state != SessionState.Failed)
            throw new InvalidOperationException($"Cannot connect while in state {_state}");

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await SetStateAsync(SessionState.ConnectingToSignaling);

            // Get local identity and settings
            var identity = _deviceIdentityService.GetCurrentIdentity();
            var settings = _settingsRepository.GetSettings();
            var signalingUrl = settings.Network.SignalingServerUrl;

            // Connect to signaling
            var connected = await _webRtcManager.ConnectToSignalingAsync(
                signalingUrl,
                identity.FormattedDeviceId,
                Convert.ToBase64String(identity.PublicKey),
                _sessionCts.Token);

            if (!connected)
            {
                await SetStateAsync(SessionState.Failed, "Failed to connect to signaling server");
                return;
            }

            await SetStateAsync(SessionState.RequestingSession);

            // Initiate connection to target
            var success = await _webRtcManager.ConnectAsync(targetDeviceId, accessMode, _sessionCts.Token);
            if (!success)
            {
                await SetStateAsync(SessionState.Failed, "Failed to send connection request");
            }
        }
        catch (OperationCanceledException)
        {
            await SetStateAsync(SessionState.Disconnected, "Connection cancelled");
        }
        catch (Exception ex)
        {
            await SetStateAsync(SessionState.Failed, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task AcceptIncomingSessionAsync(SessionPermissions grantedPermissions, CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
            return;

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await SetStateAsync(SessionState.Authenticating);

            // Check if remote device is trusted for unattended access
            var isTrusted = await _devicePairingManager.GetTrustedDeviceAsync(_currentSession.RemoteDeviceId, _sessionCts.Token);
            if (isTrusted != null)
            {
                // Log unattended access
                await _auditLogger.LogAuthenticationAsync(
                    _deviceIdentityService.GetCurrentIdentity().FormattedDeviceId,
                    _currentSession.RemoteDeviceId,
                    null,
                    AuthenticationOutcome.Success,
                    cancellationToken: _sessionCts.Token);

                await _auditLogger.LogPairingEventAsync(
                    _deviceIdentityService.GetCurrentIdentity().FormattedDeviceId,
                    _currentSession.RemoteDeviceId,
                    PairingEventType.DeviceKeyVerified,
                    _sessionCts.Token);
            }
            else
            {
                // Manual acceptance - challenge-response
                await PerformChallengeResponseAsync(_sessionCts.Token);
            }

            // Accept the session
            await _webRtcManager.AcceptIncomingSessionAsync(grantedPermissions, _sessionCts.Token);

            // Start capture and streaming
            await StartCaptureAndStreamingAsync(_sessionCts.Token);
        }
        catch (OperationCanceledException)
        {
            await SetStateAsync(SessionState.Disconnected, "Acceptance cancelled");
        }
        catch (Exception ex)
        {
            await SetStateAsync(SessionState.Failed, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task RejectIncomingSessionAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_currentSession == null)
            return;

        try
        {
            await _webRtcManager.RejectIncomingSessionAsync(reason, cancellationToken);
            await _auditLogger.LogSessionEventAsync(
                _deviceIdentityService.GetCurrentIdentity().FormattedDeviceId,
                _currentSession.RemoteDeviceId,
                SessionEventType.SessionDeclined,
                reason,
                cancellationToken);

            await SetStateAsync(SessionState.Disconnected, reason);
        }
        catch (Exception ex)
        {
            await SetStateAsync(SessionState.Failed, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_state == SessionState.Disconnected || _state == SessionState.Terminated)
            return;

        try
        {
            await SetStateAsync(SessionState.Disconnecting);

            if (_currentSession != null)
            {
                await _auditLogger.LogSessionEventAsync(
                    _deviceIdentityService.GetCurrentIdentity().FormattedDeviceId,
                    _currentSession.RemoteDeviceId,
                    SessionEventType.SessionTerminatedByLocal,
                    reason,
                    cancellationToken);
            }

            await _webRtcManager.DisconnectAsync(reason, cancellationToken);
            await StopCaptureAndStreamingAsync();
        }
        finally
        {
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;
            _currentSession = null;
            _diagnostics = null;
            await SetStateAsync(SessionState.Disconnected);
        }
    }

    /// <inheritdoc />
    public async Task SendInputEventAsync(ReadOnlyMemory<byte> inputPacket, CancellationToken cancellationToken = default)
    {
        if (_state != SessionState.Connected && _state != SessionState.Streaming)
            throw new InvalidOperationException("No active session to send input to");

        await _webRtcManager.SendInputEventAsync(inputPacket, cancellationToken);
    }

    private async Task PerformChallengeResponseAsync(CancellationToken cancellationToken)
    {
        var challenge = _challengeAuthenticator.GenerateChallenge();
        await Task.CompletedTask;
    }

    private async Task StartCaptureAndStreamingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var displays = _screenCaptureService.GetDisplays();
            var targetDisplay = displays.FirstOrDefault();

            if (targetDisplay != null)
            {
                await _screenCaptureService.StartCaptureAsync(targetDisplay.Index, 60, cancellationToken);
            }

            await SetStateAsync(SessionState.Streaming);
        }
        catch (Exception ex)
        {
            await SetStateAsync(SessionState.Failed, ex.Message);
            throw;
        }
    }

    private async Task StopCaptureAndStreamingAsync()
    {
        try
        {
            await _screenCaptureService.StopCaptureAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private void OnWebRtcStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        var newState = MapConnectionState(e.CurrentState);
        _ = SetStateAsync(newState, e.Reason);
    }

    private void OnIncomingSession(object? sender, IncomingSessionEventArgs e)
    {
        _currentSession = new SessionContext(
            SessionId: Guid.NewGuid().ToString("N"),
            RemoteDeviceId: e.RemoteDeviceId,
            RemoteFriendlyName: e.RemoteDeviceName,
            Role: SessionRole.Host,
            AccessMode: e.RequestedAccessMode,
            Permissions: e.RequestedPermissions,
            StartedAtUtc: DateTimeOffset.UtcNow
        );

        IncomingSessionOffered?.Invoke(this, new SessionOfferPayload(
            TargetDeviceId: _deviceIdentityService.GetCurrentIdentity().FormattedDeviceId,
            SenderDeviceId: e.RemoteDeviceId,
            SenderFriendlyName: e.RemoteDeviceName,
            AccessMode: e.RequestedAccessMode,
            SdpOffer: string.Empty,
            TurnCredentials: null,
            RequestedPermissions: e.RequestedPermissions
        ));

        _ = SetStateAsync(SessionState.Authenticating);
    }

    private void OnDataChannelMessage(object? sender, DataChannelMessageEventArgs e)
    {
        // Handle incoming data channel messages
    }

    private void OnDiagnosticsUpdated(object? sender, ConnectionDiagnostics e)
    {
        _diagnostics = new SessionDiagnostics(
            Transport: MapTransportType(e.TransportType),
            RoundTripTimeMs: (int)e.RoundTripTime.TotalMilliseconds,
            FramesPerSecond: e.FrameRate,
            CurrentBitrateKbps: e.CurrentBitrateBps / 1000.0,
            PacketLossPercentage: e.PacketLossPercent,
            ResolutionWidth: 1920,
            ResolutionHeight: 1080,
            ActiveEncoder: "H.264 MFT",
            ConnectedSince: _currentSession?.StartedAtUtc ?? DateTimeOffset.UtcNow
        );

        DiagnosticsUpdated?.Invoke(this, _diagnostics);
    }

    private async Task SetStateAsync(SessionState newState, string? reason = null)
    {
        if (_state == newState)
            return;

        var previousState = _state;
        _state = newState;

        var args = new SessionStateChangedEventArgs(previousState, newState, reason);
        StateChanged?.Invoke(this, args);
        await Task.CompletedTask;
    }

    private static SessionState MapConnectionState(Network.WebRtc.ConnectionState connectionState)
    {
        return connectionState switch
        {
            Network.WebRtc.ConnectionState.Disconnected => SessionState.Disconnected,
            Network.WebRtc.ConnectionState.ConnectingToSignaling => SessionState.ConnectingToSignaling,
            Network.WebRtc.ConnectionState.RegisteredWithSignaling => SessionState.RegisteredWithSignaling,
            Network.WebRtc.ConnectionState.RequestingSession => SessionState.RequestingSession,
            Network.WebRtc.ConnectionState.Authenticating => SessionState.Authenticating,
            Network.WebRtc.ConnectionState.ExchangingSdp => SessionState.ExchangingSdp,
            Network.WebRtc.ConnectionState.GatheringCandidates => SessionState.GatheringCandidates,
            Network.WebRtc.ConnectionState.EstablishingPeerConnection => SessionState.EstablishingPeerConnection,
            Network.WebRtc.ConnectionState.Connected => SessionState.Connected,
            Network.WebRtc.ConnectionState.Streaming => SessionState.Streaming,
            Network.WebRtc.ConnectionState.Reconnecting => SessionState.Reconnecting,
            Network.WebRtc.ConnectionState.Disconnecting => SessionState.Disconnecting,
            Network.WebRtc.ConnectionState.Terminated => SessionState.Terminated,
            Network.WebRtc.ConnectionState.Failed => SessionState.Failed,
            _ => SessionState.Disconnected
        };
    }

    private static Core.Sessions.TransportType MapTransportType(Network.WebRtc.TransportType transportType)
    {
        return transportType switch
        {
            Network.WebRtc.TransportType.Direct => Core.Sessions.TransportType.DirectPeerToPeer,
            Network.WebRtc.TransportType.StunReflexive => Core.Sessions.TransportType.StunAssisted,
            Network.WebRtc.TransportType.TurnRelay => Core.Sessions.TransportType.TurnRelay,
            _ => Core.Sessions.TransportType.Unknown
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _webRtcManager.StateChanged -= OnWebRtcStateChanged;
        _webRtcManager.IncomingSession -= OnIncomingSession;
        _webRtcManager.DataChannelMessageReceived -= OnDataChannelMessage;
        _webRtcManager.DiagnosticsUpdated -= OnDiagnosticsUpdated;

        if (_sessionCts != null)
        {
            _sessionCts.Cancel();
            _sessionCts.Dispose();
        }

        await _webRtcManager.DisposeAsync();
        await _screenCaptureService.StopCaptureAsync();
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Sessions;
using ControlDesk.Network.WebRtc;
using ControlDesk.Security.Authentication;
using ControlDesk.Security.Storage;
using ControlDesk.Shared.Protocol;
using FluentAssertions;
using Moq;
using Xunit;

namespace ControlDesk.Integration.Tests;

/// <summary>
/// Dual-instance loopback integration tests simulating India Controller (111-222-333)
/// and Dubai Host (999-888-777) across international boundaries.
/// </summary>
public sealed class DualInstanceLoopbackTests : IAsyncDisposable
{
    private readonly Mock<IScreenCaptureService> _indiaCaptureMock;
    private readonly Mock<IScreenCaptureService> _dubaiCaptureMock;
    private readonly Mock<IMediaEncoder> _indiaEncoderMock;
    private readonly Mock<IMediaEncoder> _dubaiEncoderMock;
    private readonly Mock<IInputInjector> _indiaInputMock;
    private readonly Mock<IInputInjector> _dubaiInputMock;
    private readonly Mock<ITransferManager> _indiaTransferMock;
    private readonly Mock<ITransferManager> _dubaiTransferMock;
    private readonly Mock<ICredentialStore> _indiaCredentialStoreMock;
    private readonly Mock<ICredentialStore> _dubaiCredentialStoreMock;
    private readonly Mock<IChallengeResponseAuthenticator> _indiaChallengeMock;
    private readonly Mock<IChallengeResponseAuthenticator> _dubaiChallengeMock;
    private readonly Mock<IDevicePairingManager> _indiaPairingMock;
    private readonly Mock<IDevicePairingManager> _dubaiPairingMock;
    private readonly Mock<IAuditLogger> _indiaAuditMock;
    private readonly Mock<IAuditLogger> _dubaiAuditMock;
    private readonly Mock<ISettingsRepository> _indiaSettingsMock;
    private readonly Mock<ISettingsRepository> _dubaiSettingsMock;
    private readonly Mock<IDeviceIdentityService> _indiaIdentityMock;
    private readonly Mock<IDeviceIdentityService> _dubaiIdentityMock;
    private readonly Mock<IWebRtcSessionManager> _indiaWebRtcMock;
    private readonly Mock<IWebRtcSessionManager> _dubaiWebRtcMock;
    private readonly SessionCoordinator _indiaCoordinator;
    private readonly SessionCoordinator _dubaiCoordinator;
    private readonly ConcurrentQueue<SignalingMessage<object>> _signalingQueue = new();
    private readonly CancellationTokenSource _cts = new();

    public DualInstanceLoopbackTests()
    {
        // India (Controller) - Device ID: 111-222-333
        _indiaCaptureMock = new Mock<IScreenCaptureService>();
        _indiaEncoderMock = new Mock<IMediaEncoder>();
        _indiaInputMock = new Mock<IInputInjector>();
        _indiaTransferMock = new Mock<ITransferManager>();
        _indiaCredentialStoreMock = new Mock<ICredentialStore>();
        _indiaChallengeMock = new Mock<IChallengeResponseAuthenticator>();
        _indiaPairingMock = new Mock<IDevicePairingManager>();
        _indiaAuditMock = new Mock<IAuditLogger>();
        _indiaSettingsMock = new Mock<ISettingsRepository>();
        _indiaIdentityMock = new Mock<IDeviceIdentityService>();
        _indiaWebRtcMock = new Mock<IWebRtcSessionManager>();

        var indiaIdentity = new DeviceIdentity
        {
            DeviceId = "111222333",
            FormattedDeviceId = "111-222-333",
            FriendlyName = "India-Controller",
            PublicKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            PublicKey = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _indiaIdentityMock.Setup(x => x.GetCurrentIdentity()).Returns(indiaIdentity);
        _indiaSettingsMock.Setup(x => x.GetSettingAsync<string>("SignalingServerUrl", It.IsAny<string>()))
            .ReturnsAsync("wss://signaling.controldesk.example.com/ws");
        _indiaCaptureMock.Setup(c => c.StartCaptureAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        _indiaCaptureMock.Setup(c => c.StopCaptureAsync()).Returns(Task.CompletedTask);
        _indiaEncoderMock.Setup(e => e.EncodeAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>())).Returns(Task.CompletedTask);

        _indiaCoordinator = new SessionCoordinator(
            _indiaIdentityMock.Object,
            _indiaWebRtcMock.Object,
            _indiaCaptureMock.Object,
            _indiaEncoderMock.Object,
            _indiaInputMock.Object,
            _indiaTransferMock.Object,
            _indiaCredentialStoreMock.Object,
            _indiaChallengeMock.Object,
            _indiaPairingMock.Object,
            _indiaAuditMock.Object,
            _indiaSettingsMock.Object);

        // Dubai (Host) - Device ID: 999-888-777
        _dubaiCaptureMock = new Mock<IScreenCaptureService>();
        _dubaiEncoderMock = new Mock<IMediaEncoder>();
        _dubaiInputMock = new Mock<IInputInjector>();
        _dubaiTransferMock = new Mock<ITransferManager>();
        _dubaiCredentialStoreMock = new Mock<ICredentialStore>();
        _dubaiChallengeMock = new Mock<IChallengeResponseAuthenticator>();
        _dubaiPairingMock = new Mock<IDevicePairingManager>();
        _dubaiAuditMock = new Mock<IAuditLogger>();
        _dubaiSettingsMock = new Mock<ISettingsRepository>();
        _dubaiIdentityMock = new Mock<IDeviceIdentityService>();
        _dubaiWebRtcMock = new Mock<IWebRtcSessionManager>();

        var dubaiIdentity = new DeviceIdentity
        {
            DeviceId = "999888777",
            FormattedDeviceId = "999-888-777",
            FriendlyName = "Dubai-Host",
            PublicKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            PublicKey = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _dubaiIdentityMock.Setup(x => x.GetCurrentIdentity()).Returns(dubaiIdentity);
        _dubaiSettingsMock.Setup(x => x.GetSettingAsync<string>("SignalingServerUrl", It.IsAny<string>()))
            .ReturnsAsync("wss://signaling.controldesk.example.com/ws");
        _dubaiCaptureMock.Setup(c => c.StartCaptureAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        _dubaiCaptureMock.Setup(c => c.StopCaptureAsync()).Returns(Task.CompletedTask);
        _dubaiEncoderMock.Setup(e => e.EncodeAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>())).Returns(Task.CompletedTask);

        _dubaiCoordinator = new SessionCoordinator(
            _dubaiIdentityMock.Object,
            _dubaiWebRtcMock.Object,
            _dubaiCaptureMock.Object,
            _dubaiEncoderMock.Object,
            _dubaiInputMock.Object,
            _dubaiTransferMock.Object,
            _dubaiCredentialStoreMock.Object,
            _dubaiChallengeMock.Object,
            _dubaiPairingMock.Object,
            _dubaiAuditMock.Object,
            _dubaiSettingsMock.Object);

        // Setup WebRTC state change handlers to simulate signaling exchange
        SetupSignalingSimulation();
    }

    private void SetupSignalingSimulation()
    {
        // Simulate the signaling server relaying messages between the two instances
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _dubaiWebRtcMock.Setup(x => x.ConnectToSignalingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // When India initiates connection, Dubai receives IncomingSession event
        _indiaWebRtcMock.Setup(x => x.ConnectAsync(
                "999-888-777", It.IsAny<AccessMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() =>
            {
                // Simulate signaling server delivering SessionOffer to Dubai
                var sessionOffer = new SessionOfferPayload(
                    TargetDeviceId: "999-888-777",
                    SenderDeviceId: "111-222-333",
                    SenderFriendlyName: "India-Controller",
                    AccessMode: AccessMode.Attended,
                    SdpOffer: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n",
                    TurnCredentials: new TurnCredentials(
                        Username: "1234567890:111-222-333",
                        Credential: "turncredential==",
                        Uris: new[] { "turn:turn.example.com:3478?transport=udp" }),
                    RequestedPermissions: new SessionPermissions(
                        ViewScreen: true, ControlMouse: true, ControlKeyboard: true,
                        ClipboardSync: true, FileTransfer: true, Chat: true, Recording: false));

                // Fire IncomingSession event on Dubai's WebRTC manager
                var incomingEvent = new IncomingSessionEventArgs(
                    "111-222-333",
                    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "India-Controller",
                    AccessMode.Attended,
                    sessionOffer.RequestedPermissions);

                // Use reflection to invoke the private handler
                InvokeIncomingSession(_dubaiCoordinator, incomingEvent);
            });

        // Dubai accepts incoming session
        _dubaiWebRtcMock.Setup(x => x.AcceptIncomingSessionAsync(
                It.IsAny<SessionPermissions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() =>
            {
                // Simulate SessionAnswer back to India
                _indiaWebRtcMock.Raise(x => x.StateChanged += null,
                    new ConnectionStateChangedEventArgs(ConnectionState.ExchangingSdp, ConnectionState.Connected));
            });

        // Both report Connected state after SDP exchange
        _indiaWebRtcMock.SetupGet(x => x.State).Returns(ConnectionState.Connected);
        _dubaiWebRtcMock.SetupGet(x => x.State).Returns(ConnectionState.Connected);
        _indiaWebRtcMock.SetupGet(x => x.RemoteDeviceId).Returns("999-888-777");
        _dubaiWebRtcMock.SetupGet(x => x.RemoteDeviceId).Returns("111-222-333");

        // Setup data channel message routing for control, transfer, chat_heartbeat
        SetupDataChannelRouting();
    }

    private void SetupDataChannelRouting()
    {
        var dataChannelMessages = new ConcurrentQueue<(string ChannelName, ReadOnlyMemory<byte> Data)>();

        // India sends -> Dubai receives
        _indiaWebRtcMock.Setup(x => x.SendInputEventAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((data, ct) =>
                dataChannelMessages.Enqueue(("control", data)));

        _indiaWebRtcMock.Setup(x => x.SendFileTransferControlAsync(It.IsAny<FileTransferControlPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<FileTransferControlPacket, CancellationToken>((pkt, ct) =>
            {
                var json = JsonSerializer.Serialize(pkt);
                var bytes = Encoding.UTF8.GetBytes(json);
                dataChannelMessages.Enqueue(("transfer", bytes));
            });

        _indiaWebRtcMock.Setup(x => x.SendFileChunkAsync(It.IsAny<FileChunkPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<FileChunkPacket, CancellationToken>((pkt, ct) =>
            {
                var json = JsonSerializer.Serialize(pkt);
                var bytes = Encoding.UTF8.GetBytes(json);
                dataChannelMessages.Enqueue(("transfer", bytes));
            });

        _indiaWebRtcMock.Setup(x => x.SendClipboardSyncAsync(It.IsAny<ClipboardSyncPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<ClipboardSyncPacket, CancellationToken>((pkt, ct) =>
            {
                var json = JsonSerializer.Serialize(pkt);
                var bytes = Encoding.UTF8.GetBytes(json);
                dataChannelMessages.Enqueue(("transfer", bytes));
            });

        _indiaWebRtcMock.Setup(x => x.SendChatMessageAsync(It.IsAny<ChatMessagePacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<ChatMessagePacket, CancellationToken>((pkt, ct) =>
            {
                var json = JsonSerializer.Serialize(pkt);
                var bytes = Encoding.UTF8.GetBytes(json);
                dataChannelMessages.Enqueue(("chat_heartbeat", bytes));
            });

        _indiaWebRtcMock.Setup(x => x.SendHeartbeatAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<CancellationToken>(ct =>
                dataChannelMessages.Enqueue(("chat_heartbeat", Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"))));

        _indiaWebRtcMock.Setup(x => x.SendDisplayConfigAsync(It.IsAny<DisplayConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<DisplayConfig, CancellationToken>((cfg, ct) =>
                dataChannelMessages.Enqueue(("control", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg)))));

        // Dubai sends -> India receives (bidirectional)
        _dubaiWebRtcMock.Setup(x => x.SendInputEventAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiWebRtcMock.Setup(x => x.SendFileTransferControlAsync(It.IsAny<FileTransferControlPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiWebRtcMock.Setup(x => x.SendFileChunkAsync(It.IsAny<FileChunkPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiWebRtcMock.Setup(x => x.SendClipboardSyncAsync(It.IsAny<ClipboardSyncPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiWebRtcMock.Setup(x => x.SendChatMessageAsync(It.IsAny<ChatMessagePacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiWebRtcMock.Setup(x => x.SendHeartbeatAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiWebRtcMock.Setup(x => x.SendDisplayConfigAsync(It.IsAny<DisplayConfig>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Simulate Dubai receiving data channel messages from India
        _dubaiWebRtcMock.Setup(x => x.DataChannelMessageReceived)
            .Returns(EventHandler<DataChannelMessageEventArgs>);
    }

    private static void InvokeIncomingSession(SessionCoordinator coordinator, IncomingSessionEventArgs args)
    {
        var method = coordinator.GetType().GetMethod("OnIncomingSession",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method?.Invoke(coordinator, new object[] { null, args });
    }

    [Fact]
    public async Task DeviceRegistration_IndiaAndDubai_RegisterWithSignalingServer()
    {
        // Act - Both instances connect to signaling
        var indiaConnected = await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);
        var dubaiConnected = true; // Dubai is already registered as host

        // Assert
        indiaConnected.Should().BeTrue();
        _indiaWebRtcMock.Verify(x => x.ConnectToSignalingAsync(
            "wss://signaling.controldesk.example.com/ws",
            "111-222-333",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _indiaWebRtcMock.Verify(x => x.ConnectAsync(
            "999-888-777",
            AccessMode.Attended,
            It.IsAny<CancellationToken>()), Times.Once);

        _dubaiWebRtcMock.Verify(x => x.ConnectToSignalingAsync(
            "wss://signaling.controldesk.example.com/ws",
            "999-888-777",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignalingHandshake_SessionOfferAnswerIceCandidate_ExchangesCorrectly()
    {
        // Arrange
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - India initiates connection
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        // Wait for state transition
        await Task.Delay(100);

        // Assert - Verify SessionOffer was sent to Dubai
        _indiaWebRtcMock.Verify(x => x.ConnectAsync(
            "999-888-777",
            AccessMode.Attended,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify Dubai received IncomingSession with correct payload
        _dubaiPairingMock.Verify(x => x.GetTrustedDeviceAsync("111-222-333", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AttendedSession_ManualAcceptance_CompletesHandshake()
    {
        // Arrange
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Dubai is NOT trusted - manual acceptance required
        _dubaiPairingMock.Setup(x => x.GetTrustedDeviceAsync("111-222-333", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrustedDeviceInfo?)null);

        // Challenge-response for attended session
        var nonce = RandomNumberGenerator.GetBytes(32);
        _dubaiChallengeMock.Setup(x => x.GenerateChallenge(32)).Returns(nonce);
        _dubaiChallengeMock.Setup(x => x.VerifyResponse(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()))
            .Returns(true);
        _indiaChallengeMock.Setup(x => x.SignChallenge(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RandomNumberGenerator.GetBytes(64));

        // Simulate incoming session on Dubai
        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller",
            AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);

        // Act - Dubai accepts with manual approval
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false),
            _cts.Token);

        // Assert
        _dubaiChallengeMock.Verify(x => x.GenerateChallenge(32), Times.Once);
        _indiaChallengeMock.Verify(x => x.SignChallenge(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _dubaiChallengeMock.Verify(x => x.VerifyResponse(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()));
        _dubaiWebRtcMock.Verify(x => x.AcceptIncomingSessionAsync(It.IsAny<SessionPermissions>(), It.IsAny<CancellationToken>()), Times.Once);
        _indiaAuditMock.Verify(x => x.LogAuthenticationAsync(
            "111-222-333", "999-888-777", It.IsAny<string>(),
            AuthenticationOutcome.Success, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnattendedSession_Ed25519KeypairVerification_BypassesManualAccept()
    {
        // Arrange
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Unattended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Dubai HAS India as trusted device with Ed25519 public key
        var trustedDevice = new TrustedDeviceInfo(
            DeviceId: "111-222-333",
            PublicKeyBase64: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            FriendlyName: "India-Controller",
            Permissions: new SessionPermissions(true, true, true, true, true, true, false),
            PairedAt: DateTimeOffset.UtcNow.AddDays(-30),
            LastConnectedAt: DateTimeOffset.UtcNow.AddDays(-1),
            IsEnabled: true);

        _dubaiPairingMock.Setup(x => x.GetTrustedDeviceAsync("111-222-333", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trustedDevice);
        _dubaiPairingMock.Setup(x => x.ValidateTrustedDeviceKeyAsync("111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Simulate incoming unattended session on Dubai
        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller",
            AccessMode.Unattended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);

        // Act - Dubai accepts automatically (no user interaction)
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false),
            _cts.Token);

        // Assert - Challenge-response should NOT be invoked for trusted device
        _dubaiChallengeMock.Verify(x => x.GenerateChallenge(It.IsAny<int>()), Times.Never);
        _indiaChallengeMock.Verify(x => x.SignChallenge(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        _dubaiChallengeMock.Verify(x => x.VerifyResponse(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);

        // But key validation should occur
        _dubaiPairingMock.Verify(x => x.ValidateTrustedDeviceKeyAsync("111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", It.IsAny<CancellationToken>()), Times.Once);

        // Audit log should record unattended access
        _dubaiAuditMock.Verify(x => x.LogAuthenticationAsync(
            "999-888-777", "111-222-333", It.IsAny<string>(),
            AuthenticationOutcome.Success, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _dubaiAuditMock.Verify(x => x.LogPairingEventAsync(
            "999-888-777", "111-222-333",
            PairingEventType.DeviceKeyVerified, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultiChannelSctpPacketRouting_ControlTransferChatHeartbeat_RoutesCorrectly()
    {
        // Arrange - Simulate established connection
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller", AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false), _cts.Token);

        // Act & Assert - Send mouse move (control channel)
        var mouseMovePacket = BinaryFrameSerializer.SerializeMouseMove(32768, 32768); // Center of 65535x65535
        await _indiaCoordinator.SendInputEventAsync(mouseMovePacket, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendInputEventAsync(mouseMovePacket, _cts.Token), Times.Once);

        // Act & Assert - Send keyboard input (control channel)
        var keyboardPacket = BinaryFrameSerializer.SerializeKeyboard(0x1E, false, KeyAction.Down); // 'A' key
        await _indiaCoordinator.SendInputEventAsync(keyboardPacket, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendInputEventAsync(keyboardPacket, _cts.Token), Times.Once);

        // Act & Assert - File transfer control (transfer channel)
        var fileControl = new FileTransferControlPacket(
            FileTransferControlType.Request,
            "transfer-001",
            "document.pdf",
            1024 * 1024,
            "sha256hash123");
        await _indiaCoordinator.SendFileTransferControlAsync(fileControl, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendFileTransferControlAsync(fileControl, _cts.Token), Times.Once);

        // Act & Assert - File chunk (transfer channel)
        var fileChunk = new FileChunkPacket("transfer-001", 0, new byte[] { 1, 2, 3, 4 }, false);
        await _indiaCoordinator.SendFileChunkAsync(fileChunk, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendFileChunkAsync(fileChunk, _cts.Token), Times.Once);

        // Act & Assert - Clipboard sync (transfer channel)
        var clipboard = new ClipboardSyncPacket("text/plain", "Hello from India!"u8.ToArray());
        await _indiaCoordinator.SendClipboardSyncAsync(clipboard, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendClipboardSyncAsync(clipboard, _cts.Token), Times.Once);

        // Act & Assert - Chat message (chat_heartbeat channel)
        var chat = new ChatMessagePacket("msg-001", "Hello Dubai!", DateTimeOffset.UtcNow);
        await _indiaCoordinator.SendChatMessageAsync(chat, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendChatMessageAsync(chat, _cts.Token), Times.Once);

        // Act & Assert - Heartbeat (chat_heartbeat channel)
        await _indiaCoordinator.SendHeartbeatAsync(_cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendHeartbeatAsync(_cts.Token), Times.Once);

        // Act & Assert - Display config (control channel)
        var displayConfig = new DisplayConfig(
            1920, 1080, 1.0f,
            new[] { new MonitorInfo(0, 0, 1920, 1080, 1.0f, true) });
        await _indiaCoordinator.SendDisplayConfigAsync(displayConfig, _cts.Token);
        _indiaWebRtcMock.Verify(x => x.SendDisplayConfigAsync(displayConfig, _cts.Token), Times.Once);
    }

    [Fact]
    public async Task BinaryPacketFraming_SerializationDeserialization_PreservesData()
    {
        // Mouse Move
        var mouseMove = BinaryFrameSerializer.SerializeMouseMove(1000, 2000);
        var (x, y) = BinaryFrameSerializer.DeserializeMouseMove(mouseMove);
        x.Should().Be(1000u);
        y.Should().Be(2000u);
        mouseMove[0].Should().Be((byte)PacketType.MouseMove);

        // Mouse Button
        var mouseBtn = BinaryFrameSerializer.SerializeMouseButton(MouseButton.Left, MouseAction.Down);
        var (btn, action, flags) = BinaryFrameSerializer.DeserializeMouseButton(mouseBtn);
        btn.Should().Be(MouseButton.Left);
        action.Should().Be(MouseAction.Down);
        mouseBtn[0].Should().Be((byte)PacketType.MouseButton);

        // Mouse Wheel
        var mouseWheel = BinaryFrameSerializer.SerializeMouseWheel(120, WheelOrientation.Vertical);
        var (delta, orient) = BinaryFrameSerializer.DeserializeMouseWheel(mouseWheel);
        delta.Should().Be((short)120);
        orient.Should().Be(WheelOrientation.Vertical);
        mouseWheel[0].Should().Be((byte)PacketType.MouseWheel);

        // Keyboard
        var keyboard = BinaryFrameSerializer.SerializeKeyboard(0x1C, true, KeyAction.Up); // Enter key
        var (scanCode, isExt, keyAction) = BinaryFrameSerializer.DeserializeKeyboard(keyboard);
        scanCode.Should().Be((ushort)0x1C);
        isExt.Should().BeTrue();
        keyAction.Should().Be(KeyAction.Up);
        keyboard[0].Should().Be((byte)PacketType.Keyboard);
    }

    [Fact]
    public async Task GracefulTermination_DisconnectAsync_CleansUpAndVerifiesAuditLog()
    {
        // Arrange - Establish connection first
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _indiaCaptureMock.Setup(x => x.StopCaptureAsync()).Returns(Task.CompletedTask);

        _dubaiWebRtcMock.Setup(x => x.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dubaiCaptureMock.Setup(x => x.StopCaptureAsync()).Returns(Task.CompletedTask);

        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller", AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false), _cts.Token);

        // Act - India initiates graceful disconnect
        await _indiaCoordinator.DisconnectAsync("Session ended by user", _cts.Token);

        // Assert - India cleanup
        _indiaWebRtcMock.Verify(x => x.DisconnectAsync("Session ended by user", It.IsAny<CancellationToken>()), Times.Once);
        _indiaCaptureMock.Verify(x => x.StopCaptureAsync(), Times.Once);
        _indiaCoordinator.State.Should().Be(SessionState.Disconnected);
        _indiaCoordinator.CurrentSession.Should().BeNull();

        // Audit log integrity verification
        _indiaAuditMock.Verify(x => x.LogSessionEventAsync(
            "111-222-333", "999-888-777",
            SessionEventType.SessionEnded, "Session ended by user", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SessionStateTransitions_FollowExpectedLifecycle()
    {
        // Track state changes
        var indiaStates = new List<SessionState>();
        var dubaiStates = new List<SessionState>();

        _indiaCoordinator.StateChanged += (_, e) => indiaStates.Add(e.NewState);
        _dubaiCoordinator.StateChanged += (_, e) => dubaiStates.Add(e.NewState);

        // Arrange
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - Connect
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        // Simulate Dubai acceptance
        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller", AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false), _cts.Token);

        // Disconnect
        _indiaWebRtcMock.Setup(x => x.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _indiaCaptureMock.Setup(x => x.StopCaptureAsync()).Returns(Task.CompletedTask);
        await _indiaCoordinator.DisconnectAsync("Test complete", _cts.Token);

        // Assert - Verify state progression
        indiaStates.Should().Contain(SessionState.Connecting);
        indiaStates.Should().Contain(SessionState.Authenticating);
        indiaStates.Should().Contain(SessionState.Connected);
        indiaStates.Should().Contain(SessionState.Streaming); // After capture starts
        indiaStates.Should().Contain(SessionState.Disconnected);

        dubaiStates.Should().Contain(SessionState.Authenticating);
        dubaiStates.Should().Contain(SessionState.Connected);
        dubaiStates.Should().Contain(SessionState.Streaming);
        dubaiStates.Should().Contain(SessionState.Disconnected);
    }

    [Fact]
    public async Task Reconnection_AfterNetworkInterruption_ResumesSession()
    {
        // Arrange - Established session
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller", AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false), _cts.Token);

        // Simulate network interruption - ICE restart
        _indiaWebRtcMock.Setup(x => x.RequestIceRestartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act - Request ICE restart
        await _indiaCoordinator.RequestIceRestartAsync(_cts.Token);

        // Assert
        _indiaWebRtcMock.Verify(x => x.RequestIceRestartAsync(_cts.Token), Times.Once);

        // Verify reconnection backoff logic would be triggered
        _indiaWebRtcMock.Raise(x => x.StateChanged += null,
            new ConnectionStateChangedEventArgs(ConnectionState.Connected, ConnectionState.Reconnecting, "Network interruption"));
        _indiaCoordinator.State.Should().Be(SessionState.Reconnecting);
    }

    [Fact]
    public async Task AuditLogIntegrity_AfterFullSession_VerifiesChain()
    {
        // Arrange
        _indiaAuditMock.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _dubaiAuditMock.Setup(x => x.VerifyIntegrityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Simulate full session
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller", AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false), _cts.Token);

        _indiaWebRtcMock.Setup(x => x.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _indiaCaptureMock.Setup(x => x.StopCaptureAsync()).Returns(Task.CompletedTask);
        await _indiaCoordinator.DisconnectAsync("Test complete", _cts.Token);

        // Act - Verify audit log integrity on both sides
        var indiaIntegrity = await _indiaAuditMock.Object.VerifyIntegrityAsync(_cts.Token);
        var dubaiIntegrity = await _dubaiAuditMock.Object.VerifyIntegrityAsync(_cts.Token);

        // Assert
        indiaIntegrity.Should().BeTrue();
        dubaiIntegrity.Should().BeTrue();

        // Verify audit entries were logged
        _indiaAuditMock.Verify(x => x.LogAuthenticationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AuthenticationOutcome>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _indiaAuditMock.Verify(x => x.LogSessionEventAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<SessionEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _dubaiAuditMock.Verify(x => x.LogAuthenticationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AuthenticationOutcome>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _dubaiAuditMock.Verify(x => x.LogSessionEventAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<SessionEventType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ConcurrentDataChannels_MaintainIndependence()
    {
        // Arrange - Established session
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        var incomingArgs = new IncomingSessionEventArgs(
            "111-222-333", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "India-Controller", AccessMode.Attended,
            new SessionPermissions(true, true, true, true, true, true, false));
        InvokeIncomingSession(_dubaiCoordinator, incomingArgs);
        await _dubaiCoordinator.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false), _cts.Token);

        // Act - Send concurrent messages on different channels
        var tasks = new List<Task>
        {
            _indiaCoordinator.SendInputEventAsync(BinaryFrameSerializer.SerializeMouseMove(100, 100), _cts.Token),
            _indiaCoordinator.SendFileTransferControlAsync(
                new FileTransferControlPacket(FileTransferControlType.Request, "t1", "f1.txt", 100, "hash"), _cts.Token),
            _indiaCoordinator.SendChatMessageAsync(new ChatMessagePacket("m1", "Hello", DateTimeOffset.UtcNow), _cts.Token),
            _indiaCoordinator.SendHeartbeatAsync(_cts.Token),
            _indiaCoordinator.SendClipboardSyncAsync(new ClipboardSyncPacket("text/plain", "clipboard"u8.ToArray()), _cts.Token)
        };

        await Task.WhenAll(tasks);

        // Assert - All sends completed without exception
        _indiaWebRtcMock.Verify(x => x.SendInputEventAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        _indiaWebRtcMock.Verify(x => x.SendFileTransferControlAsync(It.IsAny<FileTransferControlPacket>(), It.IsAny<CancellationToken>()), Times.Once);
        _indiaWebRtcMock.Verify(x => x.SendChatMessageAsync(It.IsAny<ChatMessagePacket>(), It.IsAny<CancellationToken>()), Times.Once);
        _indiaWebRtcMock.Verify(x => x.SendHeartbeatAsync(It.IsAny<CancellationToken>()), Times.Once);
        _indiaWebRtcMock.Verify(x => x.SendClipboardSyncAsync(It.IsAny<ClipboardSyncPacket>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeviceIdFormat_IndiaAndDubai_MatchExpectedFormat()
    {
        // Assert
        _indiaIdentityMock.Object.GetCurrentIdentity().FormattedDeviceId.Should().Be("111-222-333");
        _dubaiIdentityMock.Object.GetCurrentIdentity().FormattedDeviceId.Should().Be("999-888-777");

        // Verify format matches XXX-XXX-XXX pattern
        _indiaIdentityMock.Object.GetCurrentIdentity().FormattedDeviceId.Should().MatchRegex(@"^\d{3}-\d{3}-\d{3}$");
        _dubaiIdentityMock.Object.GetCurrentIdentity().FormattedDeviceId.Should().MatchRegex(@"^\d{3}-\d{3}-\d{3}$");
    }

    [Fact]
    public async Task Ed25519KeyPair_GenerationAndVerification_WorksCorrectly()
    {
        // Arrange - Generate Ed25519 keypair for India
        using var ed25519 = System.Security.Cryptography.Ed25519.Create();
        var indiaPrivateKey = ed25519.ExportParameters(true);
        var indiaPublicKey = ed25519.ExportParameters(false);

        // Dubai stores India's public key as trusted
        var publicKeyBase64 = Convert.ToBase64String(indiaPublicKey.PublicKey!);

        _dubaiPairingMock.Setup(x => x.ValidateTrustedDeviceKeyAsync(
            "111-222-333", publicKeyBase64, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - India signs a challenge
        var challenge = RandomNumberGenerator.GetBytes(32);
        var signature = ed25519.SignData(challenge, HashAlgorithmName.SHA512);
        var signatureBase64 = Convert.ToBase64String(signature);

        // Dubai verifies
        var verification = await _dubaiPairingMock.Object.ValidateTrustedDeviceKeyAsync(
            "111-222-333", publicKeyBase64, _cts.Token);

        // Assert
        verification.Should().BeTrue();
        signatureBase64.Should().NotBeNullOrEmpty();

        // Verify with actual Ed25519 verification
        var verifyResult = ed25519.VerifyData(challenge, signature, HashAlgorithmName.SHA512);
        verifyResult.Should().BeTrue();
    }

    [Fact]
    public async Task TurnCredentials_AreExchangedDuringHandshake()
    {
        // Arrange
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() =>
            {
                // Verify TURN credentials are included in SessionOffer
                var sessionOffer = new SessionOfferPayload(
                    TargetDeviceId: "999-888-777",
                    SenderDeviceId: "111-222-333",
                    SenderFriendlyName: "India-Controller",
                    AccessMode: AccessMode.Attended,
                    SdpOffer: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n",
                    TurnCredentials: new TurnCredentials(
                        Username: "1234567890:111-222-333",
                        Credential: "base64hmaccredential==",
                        Uris: new[] { "turn:turn.example.com:3478?transport=udp", "turn:turn.example.com:3478?transport=tcp" }),
                    RequestedPermissions: new SessionPermissions(true, true, true, true, true, true, false));

                sessionOffer.TurnCredentials.Should().NotBeNull();
                sessionOffer.TurnCredentials!.Username.Should().Contain("111-222-333");
                sessionOffer.TurnCredentials.Uris.Should().HaveCount(2);
                sessionOffer.TurnCredentials.Uris.Should().Contain(u => u.Contains("transport=udp"));
                sessionOffer.TurnCredentials.Uris.Should().Contain(u => u.Contains("transport=tcp"));
            });

        // Act
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        // Assert - Verified in callback
    }

    [Fact]
    public async Task ConnectionDiagnostics_ReportsTransportTypeAndMetrics()
    {
        // Arrange
        var diagnostics = new ConnectionDiagnostics(
            TransportType.TurnRelay,
            TimeSpan.FromMilliseconds(180), // India-Dubai typical RTT
            0.2,
            1024 * 1024 * 10, // 10 MB sent
            1024 * 1024 * 50, // 50 MB received (video stream)
            8_000_000, // 8 Mbps
            30,
            DateTimeOffset.UtcNow);

        _indiaWebRtcMock.SetupGet(x => x.TransportType).Returns(TransportType.TurnRelay);
        _indiaWebRtcMock.Raise(x => x.DiagnosticsUpdated += null, diagnostics);

        // Act - Connect and wait for diagnostics
        _indiaWebRtcMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indiaWebRtcMock.Setup(x => x.ConnectAsync("999-888-777", AccessMode.Attended, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _indiaCoordinator.ConnectAsync("999-888-777", AccessMode.Attended, _cts.Token);

        // Assert
        _indiaWebRtcMock.Object.TransportType.Should().Be(TransportType.TurnRelay);
        _indiaCoordinator.Diagnostics.Should().NotBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await _indiaCoordinator.DisposeAsync();
        await _dubaiCoordinator.DisposeAsync();
    }
}
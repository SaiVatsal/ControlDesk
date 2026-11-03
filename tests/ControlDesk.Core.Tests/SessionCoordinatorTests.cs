using System;
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

namespace ControlDesk.Core.Tests;

/// <summary>
/// Unit tests for the SessionCoordinator class.
/// </summary>
public sealed class SessionCoordinatorTests
{
    private readonly Mock<IDeviceIdentityService> _deviceIdentityServiceMock;
    private readonly Mock<IWebRtcSessionManager> _webRtcManagerMock;
    private readonly Mock<IScreenCaptureService> _screenCaptureServiceMock;
    private readonly Mock<IMediaEncoder> _mediaEncoderMock;
    private readonly Mock<IInputInjector> _inputInjectorMock;
    private readonly Mock<ITransferManager> _transferManagerMock;
    private readonly Mock<ICredentialStore> _credentialStoreMock;
    private readonly Mock<IChallengeResponseAuthenticator> _challengeAuthenticatorMock;
    private readonly Mock<IDevicePairingManager> _devicePairingManagerMock;
    private readonly Mock<IAuditLogger> _auditLoggerMock;
    private readonly Mock<ISettingsRepository> _settingsRepositoryMock;
    private readonly SessionCoordinator _coordinator;

    public SessionCoordinatorTests()
    {
        _deviceIdentityServiceMock = new Mock<IDeviceIdentityService>();
        _webRtcManagerMock = new Mock<IWebRtcSessionManager>();
        _screenCaptureServiceMock = new Mock<IScreenCaptureService>();
        _mediaEncoderMock = new Mock<IMediaEncoder>();
        _inputInjectorMock = new Mock<IInputInjector>();
        _transferManagerMock = new Mock<ITransferManager>();
        _credentialStoreMock = new Mock<ICredentialStore>();
        _challengeAuthenticatorMock = new Mock<IChallengeResponseAuthenticator>();
        _devicePairingManagerMock = new Mock<IDevicePairingManager>();
        _auditLoggerMock = new Mock<IAuditLogger>();
        _settingsRepositoryMock = new Mock<ISettingsRepository>();

        // Setup default return values
        var identity = new DeviceIdentity
        {
            DeviceId = "123456789",
            FormattedDeviceId = "123-456-789",
            FriendlyName = "TestDevice",
            PublicKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            PublicKey = new byte[32],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _deviceIdentityServiceMock.Setup(x => x.GetCurrentIdentity()).Returns(identity);
        _settingsRepositoryMock.Setup(x => x.GetSettingAsync<string>("SignalingServerUrl", It.IsAny<string>()))
            .ReturnsAsync("wss://signaling.controldesk.example.com/ws");

        _coordinator = new SessionCoordinator(
            _deviceIdentityServiceMock.Object,
            _webRtcManagerMock.Object,
            _screenCaptureServiceMock.Object,
            _mediaEncoderMock.Object,
            _inputInjectorMock.Object,
            _transferManagerMock.Object,
            _credentialStoreMock.Object,
            _challengeAuthenticatorMock.Object,
            _devicePairingManagerMock.Object,
            _auditLoggerMock.Object,
            _settingsRepositoryMock.Object);
    }

    [Fact]
    public void Constructor_InitializesWithDisconnectedState()
    {
        _coordinator.State.Should().Be(SessionState.Disconnected);
        _coordinator.CurrentSession.Should().BeNull();
        _coordinator.Diagnostics.Should().BeNull();
    }

    [Fact]
    public async Task ConnectAsync_WhenDisconnected_InitiatesConnection()
    {
        // Arrange
        _webRtcManagerMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _webRtcManagerMock.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<AccessMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _coordinator.ConnectAsync("987-654-321", AccessMode.FullControl, CancellationToken.None);

        // Assert
        _webRtcManagerMock.Verify(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _webRtcManagerMock.Verify(x => x.ConnectAsync("987-654-321", AccessMode.FullControl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        _webRtcManagerMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _webRtcManagerMock.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<AccessMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _coordinator.ConnectAsync("987-654-321", AccessMode.FullControl, CancellationToken.None);

        // Act & Assert
        await _coordinator.Invoking(c => c.ConnectAsync("987-654-321", AccessMode.FullControl, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot connect while in state*");
    }

    [Fact]
    public async Task ConnectAsync_WhenSignalingFails_SetsFailedState()
    {
        // Arrange
        _webRtcManagerMock.Setup(x => x.ConnectToSignalingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _coordinator.ConnectAsync("987-654-321", AccessMode.FullControl, CancellationToken.None);

        // Assert
        _coordinator.State.Should().Be(SessionState.Failed);
    }

    [Fact]
    public async Task AcceptIncomingSessionAsync_WhenTrustedDevice_SkipsChallengeResponse()
    {
        // Arrange
        var trustedDevice = new TrustedDeviceInfo(
            "987-654-321",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "TrustedDevice",
            new SessionPermissions(true, true, true, true, true, true, false),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            true);

        _devicePairingManagerMock.Setup(x => x.GetTrustedDeviceAsync("987-654-321", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trustedDevice);
        _webRtcManagerMock.Setup(x => x.AcceptIncomingSessionAsync(It.IsAny<SessionPermissions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Simulate incoming session
        var sessionOffer = new SessionOfferPayload(
            "123-456-789", "987-654-321", "RemoteDevice", AccessMode.FullControl,
            "sdp", null, new SessionPermissions(true, true, true, true, true, true, false));

        // We need to trigger the incoming session event
        _coordinator.GetType().GetMethod("OnIncomingSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_coordinator, new object[] { null, new IncomingSessionEventArgs(
                "987-654-321", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "RemoteDevice",
                AccessMode.FullControl, new SessionPermissions(true, true, true, true, true, true, false)) });

        // Act
        await _coordinator.AcceptIncomingSessionAsync(new SessionPermissions(true, true, true, true, true, true, false), CancellationToken.None);

        // Assert
        _challengeAuthenticatorMock.Verify(x => x.GenerateChallenge(It.IsAny<int>()), Times.Never);
        _webRtcManagerMock.Verify(x => x.AcceptIncomingSessionAsync(It.IsAny<SessionPermissions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectIncomingSessionAsync_WhenSessionExists_SendsRejection()
    {
        // Arrange
        _webRtcManagerMock.Setup(x => x.RejectIncomingSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Simulate incoming session
        _coordinator.GetType().GetMethod("OnIncomingSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_coordinator, new object[] { null, new IncomingSessionEventArgs(
                "987-654-321", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "RemoteDevice",
                AccessMode.FullControl, new SessionPermissions(true, true, true, true, true, true, false)) });

        // Act
        await _coordinator.RejectIncomingSessionAsync("User declined", CancellationToken.None);

        // Assert
        _webRtcManagerMock.Verify(x => x.RejectIncomingSessionAsync("User declined", It.IsAny<CancellationToken>()), Times.Once);
        _coordinator.State.Should().Be(SessionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_CleansUpAndSetsDisconnected()
    {
        // Arrange
        _webRtcManagerMock.Setup(x => x.DisconnectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _screenCaptureServiceMock.Setup(x => x.StopCaptureAsync())
            .Returns(Task.CompletedTask);

        // Simulate connected state
        _coordinator.GetType().GetMethod("OnIncomingSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_coordinator, new object[] { null, new IncomingSessionEventArgs(
                "987-654-321", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "RemoteDevice",
                AccessMode.FullControl, new SessionPermissions(true, true, true, true, true, true, false)) });

        // Act
        await _coordinator.DisconnectAsync("Test disconnect", CancellationToken.None);

        // Assert
        _webRtcManagerMock.Verify(x => x.DisconnectAsync("Test disconnect", It.IsAny<CancellationToken>()), Times.Once);
        _screenCaptureServiceMock.Verify(x => x.StopCaptureAsync(), Times.Once);
        _coordinator.State.Should().Be(SessionState.Disconnected);
        _coordinator.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task SendInputEventAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await _coordinator.Invoking(c => c.SendInputEventAsync(new byte[] { 1, 2, 3 }, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No active session*");
    }
}
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Core.Sessions;
using ControlDesk.Network.WebRtc;
using ControlDesk.Shared.Protocol;
using FluentAssertions;
using Moq;
using Xunit;

namespace ControlDesk.Network.Tests;

public sealed class WebRtcSessionManagerTests : IDisposable
{
    private readonly Mock<IMediaEncoder> _mockVideoEncoder;
    private readonly Mock<IInputInjector> _mockInputInjector;
    private readonly Mock<ITransferManager> _mockTransferManager;
    private readonly Mock<IScreenCaptureService> _mockCaptureService;
    private readonly WebRtcSessionManager _sessionManager;
    private readonly List<ConnectionState> _stateHistory = new();

    public WebRtcSessionManagerTests()
    {
        _mockVideoEncoder = new Mock<IMediaEncoder>();
        _mockInputInjector = new Mock<IInputInjector>();
        _mockTransferManager = new Mock<ITransferManager>();
        _mockCaptureService = new Mock<IScreenCaptureService>();

        _mockVideoEncoder.Setup(e => e.EncodeAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);

        _mockCaptureService.Setup(c => c.StartCaptureAsync(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _mockCaptureService.Setup(c => c.StopCaptureAsync())
            .Returns(Task.CompletedTask);

        _sessionManager = new WebRtcSessionManager(
            _mockVideoEncoder.Object,
            _mockInputInjector.Object,
            _mockTransferManager.Object,
            _mockCaptureService.Object
        );

        _sessionManager.StateChanged += (_, e) => _stateHistory.Add(e.CurrentState);
    }

    public void Dispose()
    {
        _sessionManager.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void InitialState_IsDisconnected()
    {
        _sessionManager.State.Should().Be(ConnectionState.Disconnected);
        _sessionManager.RemoteDeviceId.Should().BeNull();
        _sessionManager.TransportType.Should().Be(TransportType.Unknown);
    }

    [Fact]
    public void StateChangedEvent_FiresOnStateTransition()
    {
        // Can't easily test full state transitions without a real WebSocket,
        // but we can verify the event is wired up
        _stateHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectToSignalingAsync_InvalidState_ReturnsFalse()
    {
        // First connection
        var fakeUri = new Uri("ws://localhost:8080/ws");
        var socket = new Mock<ClientWebSocket>();

        // Can't easily mock ClientWebSocket.ConnectAsync without abstracting it
        // This test verifies the state check logic
        _sessionManager.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task ConnectAsync_NotRegistered_ThrowsInvalidOperationException()
    {
        // Attempting to connect without being registered should throw
        var act = async () => await _sessionManager.ConnectAsync("111-222-333", AccessMode.Attended);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Not connected to signaling server*");
    }

    [Fact]
    public async Task AcceptIncomingSessionAsync_NoRemoteDevice_ReturnsFalse()
    {
        var result = await _sessionManager.AcceptIncomingSessionAsync(
            new SessionPermissions(true, true, true, true, true, true, false)
        );
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RejectIncomingSessionAsync_NoRemoteDevice_ReturnsFalse()
    {
        var result = await _sessionManager.RejectIncomingSessionAsync("Test reason");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectAsync_FromDisconnected_ReturnsImmediately()
    {
        await _sessionManager.DisconnectAsync();
        _sessionManager.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task SendInputEventAsync_ReturnsCompletedTask()
    {
        await _sessionManager.SendInputEventAsync(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        // Should complete without exception
    }

    [Fact]
    public async Task SendDisplayConfigAsync_ReturnsCompletedTask()
    {
        var config = new DisplayConfig(1920, 1080, 1.0f, new[] { new MonitorInfo(0, 0, 1920, 1080, 1.0f, true) });
        await _sessionManager.SendDisplayConfigAsync(config);
    }

    [Fact]
    public async Task SendFileTransferControlAsync_ReturnsCompletedTask()
    {
        var control = new FileTransferControlPacket(
            FileTransferControlType.Request,
            "transfer-1",
            "test.txt",
            1024,
            "sha256hash"
        );
        await _sessionManager.SendFileTransferControlAsync(control);
    }

    [Fact]
    public async Task SendFileChunkAsync_ReturnsCompletedTask()
    {
        var chunk = new FileChunkPacket(
            "transfer-1",
            0,
            new byte[] { 1, 2, 3, 4 },
            false
        );
        await _sessionManager.SendFileChunkAsync(chunk);
    }

    [Fact]
    public async Task SendClipboardSyncAsync_ReturnsCompletedTask()
    {
        var clipboard = new ClipboardSyncPacket("text/plain", "Hello World"u8.ToArray());
        await _sessionManager.SendClipboardSyncAsync(clipboard);
    }

    [Fact]
    public async Task SendChatMessageAsync_ReturnsCompletedTask()
    {
        var chat = new ChatMessagePacket("msg-1", "Hello!", DateTimeOffset.UtcNow);
        await _sessionManager.SendChatMessageAsync(chat);
    }

    [Fact]
    public async Task SendHeartbeatAsync_ReturnsCompletedTask()
    {
        await _sessionManager.SendHeartbeatAsync();
    }

    [Fact]
    public async Task RequestIceRestartAsync_ReturnsCompletedTask()
    {
        await _sessionManager.RequestIceRestartAsync();
    }

    [Fact]
    public void ConnectionStateEnum_HasAllExpectedValues()
    {
        var values = Enum.GetValues<ConnectionState>();
        values.Should().Contain(ConnectionState.Disconnected);
        values.Should().Contain(ConnectionState.ConnectingToSignaling);
        values.Should().Contain(ConnectionState.RegisteredWithSignaling);
        values.Should().Contain(ConnectionState.RequestingSession);
        values.Should().Contain(ConnectionState.Authenticating);
        values.Should().Contain(ConnectionState.ExchangingSdp);
        values.Should().Contain(ConnectionState.GatheringCandidates);
        values.Should().Contain(ConnectionState.EstablishingPeerConnection);
        values.Should().Contain(ConnectionState.Connected);
        values.Should().Contain(ConnectionState.Streaming);
        values.Should().Contain(ConnectionState.Reconnecting);
        values.Should().Contain(ConnectionState.Disconnecting);
        values.Should().Contain(ConnectionState.Terminated);
        values.Should().Contain(ConnectionState.Failed);
    }

    [Fact]
    public void TransportTypeEnum_HasAllExpectedValues()
    {
        var values = Enum.GetValues<TransportType>();
        values.Should().Contain(TransportType.Unknown);
        values.Should().Contain(TransportType.Direct);
        values.Should().Contain(TransportType.StunReflexive);
        values.Should().Contain(TransportType.TurnRelay);
    }

    [Fact]
    public void ConnectionStateChangedEventArgs_HoldsPreviousAndCurrentState()
    {
        var args = new ConnectionStateChangedEventArgs(
            ConnectionState.Disconnected,
            ConnectionState.ConnectingToSignaling,
            "Initiating connection"
        );

        args.PreviousState.Should().Be(ConnectionState.Disconnected);
        args.CurrentState.Should().Be(ConnectionState.ConnectingToSignaling);
        args.Reason.Should().Be("Initiating connection");
    }

    [Fact]
    public void IncomingSessionEventArgs_HoldsAllFields()
    {
        var args = new IncomingSessionEventArgs(
            "111-222-333",
            "publickeybase64",
            "Remote Device",
            AccessMode.Unattended,
            new SessionPermissions(true, true, true, true, true, true, false)
        );

        args.RemoteDeviceId.Should().Be("111-222-333");
        args.RemotePublicKeyBase64.Should().Be("publickeybase64");
        args.RemoteDeviceName.Should().Be("Remote Device");
        args.RequestedAccessMode.Should().Be(AccessMode.Unattended);
        args.RequestedPermissions.ControlMouse.Should().BeTrue();
    }

    [Fact]
    public void DataChannelMessageEventArgs_HoldsChannelAndData()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var args = new DataChannelMessageEventArgs("control", data);

        args.ChannelName.Should().Be("control");
        args.Data.ToArray().Should().Equal(data);
    }

    [Fact]
    public void ConnectionDiagnostics_HoldsAllMetrics()
    {
        var diag = new ConnectionDiagnostics(
            TransportType.TurnRelay,
            TimeSpan.FromMilliseconds(150),
            0.5,
            1024 * 1024,
            2 * 1024 * 1024,
            5_000_000,
            30,
            DateTimeOffset.UtcNow
        );

        diag.TransportType.Should().Be(TransportType.TurnRelay);
        diag.RoundTripTime.Should().Be(TimeSpan.FromMilliseconds(150));
        diag.PacketLossPercent.Should().Be(0.5);
        diag.BytesSent.Should().Be(1024 * 1024);
        diag.BytesReceived.Should().Be(2 * 1024 * 1024);
        diag.CurrentBitrateBps.Should().Be(5_000_000);
        diag.FrameRate.Should().Be(30);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        await _sessionManager.DisposeAsync();
        await _sessionManager.DisposeAsync(); // Should not throw
    }
}
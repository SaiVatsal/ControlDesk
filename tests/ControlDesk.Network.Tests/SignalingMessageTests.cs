using System;
using System.Text.Json;
using ControlDesk.Shared.Protocol;
using FluentAssertions;
using Xunit;

namespace ControlDesk.Network.Tests;

public sealed class SignalingMessageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void SerializeDeserialize_RegisterDevicePayload_RoundTripSucceeds()
    {
        var payload = new RegisterDevicePayload(
            DeviceId: "123-456-789",
            PublicKeyBase64: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            FriendlyName: "Test Device",
            ClientVersion: "1.0.0",
            Capabilities: new DeviceCapabilities(
                CanHost: true,
                CanControl: true,
                SupportsHardwareEncoding: true,
                SupportedCodecs: new[] { "H264", "VP8", "AV1" },
                MaxResolutionWidth: 3840,
                MaxResolutionHeight: 2160
            )
        );

        var envelope = new SignalingMessage<RegisterDevicePayload>(
            SignalingMessageTypes.RegisterDevice,
            "msg-001",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        json.Should().Contain("register_device");
        json.Should().Contain("123-456-789");
        json.Should().Contain("Test Device");

        var deserialized = JsonSerializer.Deserialize<SignalingMessage<RegisterDevicePayload>>(json, JsonOptions);
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(SignalingMessageTypes.RegisterDevice);
        deserialized.MessageId.Should().Be("msg-001");
        deserialized.Payload.DeviceId.Should().Be("123-456-789");
        deserialized.Payload.FriendlyName.Should().Be("Test Device");
        deserialized.Payload.Capabilities.CanHost.Should().BeTrue();
        deserialized.Payload.Capabilities.CanControl.Should().BeTrue();
        deserialized.Payload.Capabilities.SupportedCodecs.Should().BeEquivalentTo(new[] { "H264", "VP8", "AV1" });
    }

    [Fact]
    public void SerializeDeserialize_DevicePresencePayload_RoundTripSucceeds()
    {
        var payload = new DevicePresencePayload(
            DeviceId: "987-654-321",
            Status: DeviceStatus.Online,
            LastSeenUtc: DateTimeOffset.UtcNow,
            CurrentIp: "192.168.1.100",
            NetworkType: "Wifi"
        );

        var envelope = new SignalingMessage<DevicePresencePayload>(
            SignalingMessageTypes.DevicePresence,
            "msg-002",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<DevicePresencePayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(SignalingMessageTypes.DevicePresence);
        deserialized.Payload.DeviceId.Should().Be("987-654-321");
        deserialized.Payload.Status.Should().Be(DeviceStatus.Online);
        deserialized.Payload.CurrentIp.Should().Be("192.168.1.100");
        deserialized.Payload.NetworkType.Should().Be("Wifi");
    }

    [Fact]
    public void SerializeDeserialize_SessionOfferPayload_RoundTripSucceeds()
    {
        var payload = new SessionOfferPayload(
            TargetDeviceId: "111-222-333",
            SenderDeviceId: "444-555-666",
            SenderFriendlyName: "India-Controller",
            AccessMode: AccessMode.Attended,
            SdpOffer: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n",
            TurnCredentials: new TurnCredentials(
                Username: "1234567890:444-555-666",
                Credential: "base64credential==",
                Uris: new[] { "turn:turn.example.com:3478?transport=udp", "turn:turn.example.com:3478?transport=tcp" }
            ),
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

        var envelope = new SignalingMessage<SessionOfferPayload>(
            SignalingMessageTypes.SessionOffer,
            "msg-003",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<SessionOfferPayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(SignalingMessageTypes.SessionOffer);
        deserialized.Payload.TargetDeviceId.Should().Be("111-222-333");
        deserialized.Payload.SenderDeviceId.Should().Be("444-555-666");
        deserialized.Payload.AccessMode.Should().Be(AccessMode.Attended);
        deserialized.Payload.SdpOffer.Should().Contain("v=0");
        deserialized.Payload.TurnCredentials.Should().NotBeNull();
        deserialized.Payload.TurnCredentials!.Username.Should().Be("1234567890:444-555-666");
        deserialized.Payload.TurnCredentials.Uris.Should().HaveCount(2);
        deserialized.Payload.RequestedPermissions.ViewScreen.Should().BeTrue();
        deserialized.Payload.RequestedPermissions.ControlKeyboard.Should().BeTrue();
    }

    [Fact]
    public void SerializeDeserialize_SessionAnswerPayload_Accepted_RoundTripSucceeds()
    {
        var payload = new SessionAnswerPayload(
            TargetDeviceId: "444-555-666",
            SenderDeviceId: "111-222-333",
            Accepted: true,
            SdpAnswer: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n",
            RejectReason: null
        );

        var envelope = new SignalingMessage<SessionAnswerPayload>(
            SignalingMessageTypes.SessionAnswer,
            "msg-004",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<SessionAnswerPayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.Accepted.Should().BeTrue();
        deserialized.Payload.SdpAnswer.Should().Contain("v=0");
        deserialized.Payload.RejectReason.Should().BeNull();
    }

    [Fact]
    public void SerializeDeserialize_SessionAnswerPayload_Rejected_RoundTripSucceeds()
    {
        var payload = new SessionAnswerPayload(
            TargetDeviceId: "444-555-666",
            SenderDeviceId: "111-222-333",
            Accepted: false,
            SdpAnswer: null,
            RejectReason: "User declined connection"
        );

        var envelope = new SignalingMessage<SessionAnswerPayload>(
            SignalingMessageTypes.SessionAnswer,
            "msg-005",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<SessionAnswerPayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.Accepted.Should().BeFalse();
        deserialized.Payload.SdpAnswer.Should().BeNull();
        deserialized.Payload.RejectReason.Should().Be("User declined connection");
    }

    [Fact]
    public void SerializeDeserialize_IceCandidatePayload_RoundTripSucceeds()
    {
        var payload = new IceCandidatePayload(
            TargetDeviceId: "111-222-333",
            Candidate: "candidate:1 1 UDP 2122260223 192.168.1.100 5000 typ host",
            SdpMid: "0",
            SdpMLineIndex: 0
        );

        var envelope = new SignalingMessage<IceCandidatePayload>(
            SignalingMessageTypes.IceCandidate,
            "msg-006",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<IceCandidatePayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.TargetDeviceId.Should().Be("111-222-333");
        deserialized.Payload.Candidate.Should().Contain("typ host");
        deserialized.Payload.SdpMid.Should().Be("0");
        deserialized.Payload.SdpMLineIndex.Should().Be(0);
    }

    [Fact]
    public void SerializeDeserialize_ChallengeRequestPayload_RoundTripSucceeds()
    {
        var payload = new ChallengeRequestPayload(
            TargetDeviceId: "987-654-321",
            ControllerDeviceId: "123-456-789",
            ControllerPublicKeyBase64: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB="
        );

        var envelope = new SignalingMessage<ChallengeRequestPayload>(
            SignalingMessageTypes.ChallengeRequest,
            "msg-007",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<ChallengeRequestPayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.TargetDeviceId.Should().Be("987-654-321");
        deserialized.Payload.ControllerDeviceId.Should().Be("123-456-789");
        deserialized.Payload.ControllerPublicKeyBase64.Should().Be("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=");
    }

    [Fact]
    public void SerializeDeserialize_ChallengeResponsePayload_RoundTripSucceeds()
    {
        var payload = new ChallengeResponsePayload(
            NonceBase64: "NonceBase64String==",
            ServerTimestampUtc: DateTimeOffset.UtcNow,
            ValidUntilUtc: DateTimeOffset.UtcNow.AddMinutes(5)
        );

        var envelope = new SignalingMessage<ChallengeResponsePayload>(
            SignalingMessageTypes.ChallengeResponse,
            "msg-008",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<ChallengeResponsePayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.NonceBase64.Should().Be("NonceBase64String==");
        deserialized.Payload.ValidUntilUtc.Should().BeAfter(deserialized.Payload.ServerTimestampUtc);
    }

    [Fact]
    public void SerializeDeserialize_ChallengeVerifyPayload_RoundTripSucceeds()
    {
        var payload = new ChallengeVerifyPayload(
            SignatureBase64: "SignatureBase64String==",
            SdpOffer: "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\n"
        );

        var envelope = new SignalingMessage<ChallengeVerifyPayload>(
            SignalingMessageTypes.ChallengeVerify,
            "msg-009",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<ChallengeVerifyPayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.SignatureBase64.Should().Be("SignatureBase64String==");
        deserialized.Payload.SdpOffer.Should().Contain("v=0");
    }

    [Fact]
    public void SerializeDeserialize_RevokeDevicePayload_RoundTripSucceeds()
    {
        var payload = new RevokeDevicePayload(
            TargetDeviceId: "111-222-333",
            RevokedDeviceId: "999-888-777",
            Reason: RevocationReason.SecurityCompromise
        );

        var envelope = new SignalingMessage<RevokeDevicePayload>(
            SignalingMessageTypes.RevokeDevice,
            "msg-010",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<RevokeDevicePayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.TargetDeviceId.Should().Be("111-222-333");
        deserialized.Payload.RevokedDeviceId.Should().Be("999-888-777");
        deserialized.Payload.Reason.Should().Be(RevocationReason.SecurityCompromise);
    }

    [Fact]
    public void SerializeDeserialize_ErrorPayload_RoundTripSucceeds()
    {
        var payload = new ErrorPayload(
            Code: "DEVICE_OFFLINE",
            Message: "Target device is not online",
            Details: "Last seen 2 hours ago"
        );

        var envelope = new SignalingMessage<ErrorPayload>(
            SignalingMessageTypes.Error,
            "msg-011",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<ErrorPayload>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Payload.Code.Should().Be("DEVICE_OFFLINE");
        deserialized.Payload.Message.Should().Be("Target device is not online");
        deserialized.Payload.Details.Should().Be("Last seen 2 hours ago");
    }

    [Fact]
    public void SerializeDeserialize_HeartbeatPayload_AnonymousType_RoundTripSucceeds()
    {
        var payload = new { Timestamp = DateTimeOffset.UtcNow };

        var envelope = new SignalingMessage<object>(
            SignalingMessageTypes.Heartbeat,
            "msg-012",
            DateTimeOffset.UtcNow,
            payload
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        json.Should().Contain("heartbeat");
        json.Should().Contain("timestamp");
    }

    [Fact]
    public void Deserialize_UnknownType_ThrowsOrReturnsNull()
    {
        var json = """{"type":"unknown_type","messageId":"msg-999","timestampUtc":"2024-01-01T00:00:00Z","payload":{}}""";

        var deserialized = JsonSerializer.Deserialize<SignalingMessage<JsonElement>>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be("unknown_type");
    }

    [Fact]
    public void Serialize_NullTimestamp_UsesUtcNow()
    {
        var envelope = new SignalingMessage<RegisterDevicePayload>(
            SignalingMessageTypes.RegisterDevice,
            "msg-test",
            default,
            new RegisterDevicePayload("123-456-789", "key", "Name", "1.0", new DeviceCapabilities(true, true, false, Array.Empty<string>(), 1920, 1080))
        );

        envelope.TimestampUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Serialize_MessageId_IsPreserved()
    {
        var envelope = new SignalingMessage<RegisterDevicePayload>(
            SignalingMessageTypes.RegisterDevice,
            "custom-message-id-12345",
            DateTimeOffset.UtcNow,
            new RegisterDevicePayload("123-456-789", "key", "Name", "1.0", new DeviceCapabilities(true, true, false, Array.Empty<string>(), 1920, 1080))
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SignalingMessage<RegisterDevicePayload>>(json, JsonOptions);

        deserialized!.MessageId.Should().Be("custom-message-id-12345");
    }

    [Fact]
    public void DeviceCapabilities_Serialization_IncludesAllFields()
    {
        var caps = new DeviceCapabilities(
            CanHost: false,
            CanControl: true,
            SupportsHardwareEncoding: false,
            SupportedCodecs: new[] { "VP8" },
            MaxResolutionWidth: 1920,
            MaxResolutionHeight: 1080
        );

        var json = JsonSerializer.Serialize(caps, JsonOptions);
        json.Should().Contain("canHost");
        json.Should().Contain("canControl");
        json.Should().Contain("supportsHardwareEncoding");
        json.Should().Contain("supportedCodecs");
        json.Should().Contain("maxResolutionWidth");
        json.Should().Contain("maxResolutionHeight");
    }

    [Fact]
    public void SessionPermissions_Serialization_IncludesAllFields()
    {
        var perms = new SessionPermissions(
            ViewScreen: true,
            ControlMouse: false,
            ControlKeyboard: true,
            ClipboardSync: false,
            FileTransfer: true,
            Chat: true,
            Recording: true
        );

        var json = JsonSerializer.Serialize(perms, JsonOptions);
        json.Should().Contain("viewScreen");
        json.Should().Contain("controlMouse");
        json.Should().Contain("controlKeyboard");
        json.Should().Contain("clipboardSync");
        json.Should().Contain("fileTransfer");
        json.Should().Contain("chat");
        json.Should().Contain("recording");
    }

    [Fact]
    public void TurnCredentials_Serialization_IncludesAllFields()
    {
        var turn = new TurnCredentials(
            Username: "user:device",
            Credential: "cred",
            Uris: new[] { "turn:example.com:3478" }
        );

        var json = JsonSerializer.Serialize(turn, JsonOptions);
        json.Should().Contain("username");
        json.Should().Contain("credential");
        json.Should().Contain("uris");
    }
}
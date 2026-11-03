using System;
using ControlDesk.Core.Devices;
using FluentAssertions;
using Xunit;

namespace ControlDesk.Core.Tests;

public sealed class DeviceIdentityTests
{
    [Fact]
    public void Constructor_WithRawComponents_FormatsDeviceIdCorrectly()
    {
        // Arrange
        var rawId = "123456789";
        var friendlyName = "India-Desktop";
        var pubKey = Convert.ToBase64String(new byte[32]);
        var now = DateTimeOffset.UtcNow;

        // Act
        var identity = new DeviceIdentity(rawId, friendlyName, pubKey, now);

        // Assert
        identity.DeviceId.Should().Be("123456789");
        identity.FormattedDeviceId.Should().Be("123-456-789");
        identity.FriendlyName.Should().Be("India-Desktop");
        identity.PublicKeyBase64.Should().Be(pubKey);
        identity.PublicKey.Should().HaveCount(32);
        identity.CreatedAtUtc.Should().Be(now);
        identity.CreatedAt.Should().Be(now);
    }

    [Fact]
    public void Constructor_WithDashedRawId_StripsDashesAndNormalizes()
    {
        // Arrange
        var rawId = "123-456-789";
        var friendlyName = "Dubai-Host";
        var pubKey = Convert.ToBase64String(new byte[32]);
        var now = DateTimeOffset.UtcNow;

        // Act
        var identity = new DeviceIdentity(rawId, friendlyName, pubKey, now);

        // Assert
        identity.DeviceId.Should().Be("123456789");
        identity.FormattedDeviceId.Should().Be("123-456-789");
    }

    [Fact]
    public void ObjectInitializer_SupportsAllProperties()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var pubBytes = new byte[32];
        pubBytes[0] = 0x42;
        var privBytes = new byte[32];
        privBytes[0] = 0x99;

        // Act
        var identity = new DeviceIdentity
        {
            DeviceId = "987654321",
            FormattedDeviceId = "987-654-321",
            FriendlyName = "RemoteWorker",
            PublicKeyBase64 = Convert.ToBase64String(pubBytes),
            PublicKey = pubBytes,
            PrivateKey = privBytes,
            CreatedAtUtc = now
        };

        // Assert
        identity.DeviceId.Should().Be("987654321");
        identity.FormattedDeviceId.Should().Be("987-654-321");
        identity.FriendlyName.Should().Be("RemoteWorker");
        identity.PublicKey[0].Should().Be(0x42);
        identity.PrivateKey.Should().NotBeNull();
        identity.PrivateKey![0].Should().Be(0x99);
        identity.CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public void KnownDevice_RecordInitialization_HoldsExpectedProperties()
    {
        // Arrange & Act
        var now = DateTimeOffset.UtcNow;
        var device = new KnownDevice(
            "111-222-333",
            " Dubai Office PC",
            "pubkey123",
            true,
            now,
            now.AddDays(-5));

        // Assert
        device.DeviceId.Should().Be("111-222-333");
        device.FriendlyName.Should().Be(" Dubai Office PC");
        device.PublicKeyBase64.Should().Be("pubkey123");
        device.IsTrustedForUnattended.Should().BeTrue();
        device.LastConnectedUtc.Should().Be(now);
        device.AddedAtUtc.Should().Be(now.AddDays(-5));
    }
}
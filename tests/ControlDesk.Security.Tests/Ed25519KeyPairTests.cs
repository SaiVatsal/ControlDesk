using System;
using System.Security.Cryptography;
using ControlDesk.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace ControlDesk.Security.Tests;

public sealed class Ed25519KeyPairTests
{
    [Fact]
    public void Constructor_GeneratesValidKeyPair()
    {
        // Act
        using var keyPair = new Ed25519KeyPair();

        // Assert
        keyPair.PublicKey.Should().HaveCount(Ed25519KeyPair.PublicKeySize);
        keyPair.ExportPrivateKey().Should().HaveCount(Ed25519KeyPair.PrivateKeySize);
        keyPair.PublicKeyBase64.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_FromPrivateKey_DerivesCorrectPublicKey()
    {
        // Arrange
        using var original = new Ed25519KeyPair();
        var privateKey = original.ExportPrivateKey();

        // Act
        using var derived = new Ed25519KeyPair(privateKey);

        // Assert
        derived.ExportPrivateKey().Should().Equal(privateKey);
        derived.ExportPublicKey().Should().Equal(original.ExportPublicKey());
    }

    [Fact]
    public void Constructor_FromBothKeys_PreservesBoth()
    {
        // Arrange
        using var original = new Ed25519KeyPair();
        var privateKey = original.ExportPrivateKey();
        var publicKey = original.ExportPublicKey();

        // Act
        using var keyPair = new Ed25519KeyPair(privateKey, publicKey);

        // Assert
        keyPair.ExportPrivateKey().Should().Equal(privateKey);
        keyPair.ExportPublicKey().Should().Equal(publicKey);
    }

    [Fact]
    public void Constructor_InvalidPrivateKeyLength_ThrowsArgumentException()
    {
        // Act & Assert
        Action act = () => new Ed25519KeyPair(new byte[16]);
        act.Should().Throw<ArgumentException>().WithMessage("*Private key must be 32 bytes*");
    }

    [Fact]
    public void Constructor_InvalidPublicKeyLength_ThrowsArgumentException()
    {
        // Arrange
        using var original = new Ed25519KeyPair();
        var privateKey = original.ExportPrivateKey();

        // Act & Assert
        Action act = () => new Ed25519KeyPair(privateKey, new byte[16]);
        act.Should().Throw<ArgumentException>().WithMessage("*Public key must be 32 bytes*");
    }

    [Fact]
    public void Sign_Verify_RoundTrip_Succeeds()
    {
        // Arrange
        using var keyPair = new Ed25519KeyPair();
        var data = "Hello, ControlDesk!"u8.ToArray();

        // Act
        var signature = keyPair.Sign(data);

        // Assert
        signature.Should().HaveCount(Ed25519KeyPair.SignatureSize);
        keyPair.Verify(data, signature).Should().BeTrue();
        Ed25519KeyPair.Verify(keyPair.PublicKey, data, signature).Should().BeTrue();
    }

    [Fact]
    public void Sign_DifferentData_ProducesDifferentSignatures()
    {
        // Arrange
        using var keyPair = new Ed25519KeyPair();
        var data1 = "Message 1"u8.ToArray();
        var data2 = "Message 2"u8.ToArray();

        // Act
        var sig1 = keyPair.Sign(data1);
        var sig2 = keyPair.Sign(data2);

        // Assert
        sig1.Should().NotEqual(sig2);
        keyPair.Verify(data1, sig1).Should().BeTrue();
        keyPair.Verify(data2, sig2).Should().BeTrue();
        keyPair.Verify(data1, sig2).Should().BeFalse();
        keyPair.Verify(data2, sig1).Should().BeFalse();
    }

    [Fact]
    public void Verify_InvalidSignature_ReturnsFalse()
    {
        // Arrange
        using var keyPair = new Ed25519KeyPair();
        var data = "Test data"u8.ToArray();
        var signature = keyPair.Sign(data);

        // Corrupt the signature
        var corrupted = signature.ToArray();
        corrupted[0] ^= 0xFF;

        // Act & Assert
        keyPair.Verify(data, corrupted).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongPublicKey_ReturnsFalse()
    {
        // Arrange
        using var keyPair1 = new Ed25519KeyPair();
        using var keyPair2 = new Ed25519KeyPair();
        var data = "Test data"u8.ToArray();
        var signature = keyPair1.Sign(data);

        // Act & Assert
        keyPair2.Verify(data, signature).Should().BeFalse();
        Ed25519KeyPair.Verify(keyPair2.PublicKey, data, signature).Should().BeFalse();
    }

    [Fact]
    public void ExportPrivateKey_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var keyPair = new Ed25519KeyPair();
        keyPair.Dispose();

        // Act & Assert
        Action act = () => keyPair.ExportPrivateKey();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ExportPublicKey_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var keyPair = new Ed25519KeyPair();
        keyPair.Dispose();

        // Act & Assert
        Action act = () => keyPair.ExportPublicKey();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Sign_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var keyPair = new Ed25519KeyPair();
        keyPair.Dispose();

        // Act & Assert
        Action act = () => keyPair.Sign("data"u8.ToArray());
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_MultipleCalls_IsIdempotent()
    {
        // Arrange
        var keyPair = new Ed25519KeyPair();

        // Act
        keyPair.Dispose();
        keyPair.Dispose(); // Should not throw

        // Assert - no exception
    }

    [Fact]
    public void ToDeviceIdentity_CreatesValidDeviceIdentity()
    {
        // Arrange
        using var keyPair = new Ed25519KeyPair();
        var deviceId = "123-456-789";
        var friendlyName = "Test Device";

        // Act
        var identity = keyPair.ToDeviceIdentity(deviceId, friendlyName);

        // Assert
        identity.DeviceId.Should().Be("123456789");
        identity.FormattedDeviceId.Should().Be("123-456-789");
        identity.FriendlyName.Should().Be(friendlyName);
        identity.PublicKeyBase64.Should().Be(keyPair.PublicKeyBase64);
        identity.PublicKey.Should().Equal(keyPair.ExportPublicKey());
    }

    [Fact]
    public void GenerateDeviceId_ReturnsFormatted9DigitId()
    {
        // Act
        var id = Ed25519KeyPair.GenerateDeviceId();

        // Assert
        id.Should().MatchRegex(@"^\d{3}-\d{3}-\d{3}$");
    }

    [Fact]
    public void GenerateDeviceId_ReturnsUniqueIds()
    {
        // Act
        var ids = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            ids.Add(Ed25519KeyPair.GenerateDeviceId());
        }

        // Assert
        ids.Should().HaveCount(100);
    }

    [Fact]
    public void Verify_StaticMethod_WorksWithRawSpans()
    {
        // Arrange
        using var keyPair = new Ed25519KeyPair();
        var data = "Test message"u8.ToArray();
        var signature = keyPair.Sign(data);

        // Act
        var result = Ed25519KeyPair.Verify(keyPair.PublicKey, data, signature);

        // Assert
        result.Should().BeTrue();
    }
}
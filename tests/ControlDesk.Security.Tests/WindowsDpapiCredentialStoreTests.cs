using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ControlDesk.Security.Cryptography;
using ControlDesk.Security.Storage;
using FluentAssertions;
using Xunit;

namespace ControlDesk.Security.Tests;

public sealed class WindowsDpapiCredentialStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly byte[] _testEntropy;

    public WindowsDpapiCredentialStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ControlDeskTests", Guid.NewGuid().ToString("N"));
        _testEntropy = Encoding.UTF8.GetBytes("TestEntropy123");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, true);
        }
        catch { }
    }

    [Fact]
    public void StoreAndRetrieve_RoundTrip_Succeeds()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        var key = "test_key";
        var value = "SecretValue123"u8.ToArray();

        // Act
        store.Store(key, value);
        var retrieved = store.Retrieve(key);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().Equal(value);
    }

    [Fact]
    public void StoreAndRetrieveString_RoundTrip_Succeeds()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        var key = "string_key";
        var value = "Hello, World! 🚀";

        // Act
        store.StoreString(key, value);
        var retrieved = store.RetrieveString(key);

        // Assert
        retrieved.Should().Be(value);
    }

    [Fact]
    public void Retrieve_NonExistentKey_ReturnsNull()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);

        // Act
        var result = store.Retrieve("nonexistent");
        var resultString = store.RetrieveString("nonexistent");

        // Assert
        result.Should().BeNull();
        resultString.Should().BeNull();
    }

    [Fact]
    public void Delete_RemovesStoredValue()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        var key = "delete_me";
        store.Store(key, "value"u8.ToArray());

        // Act
        store.Delete(key);
        var retrieved = store.Retrieve(key);

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void Exists_ReturnsTrueForStoredKey()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        var key = "exists_test";

        // Act & Assert
        store.Exists(key).Should().BeFalse();
        store.Store(key, "value"u8.ToArray());
        store.Exists(key).Should().BeTrue();
    }

    [Fact]
    public void StoreKeyPair_AndRetrieve_PreservesKeys()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        using var originalKeyPair = new Ed25519KeyPair();
        var keyId = "device_identity";

        // Act
        store.StoreKeyPair(keyId, originalKeyPair);
        using var retrievedKeyPair = store.RetrieveKeyPair(keyId);

        // Assert
        retrievedKeyPair.Should().NotBeNull();
        retrievedKeyPair!.ExportPrivateKey().Should().Equal(originalKeyPair.ExportPrivateKey());
        retrievedKeyPair.ExportPublicKey().Should().Equal(originalKeyPair.ExportPublicKey());
    }

    [Fact]
    public void RetrieveKeyPair_NonExistent_ReturnsNull()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);

        // Act
        var result = store.RetrieveKeyPair("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void StoreTrustedDevice_AndRetrieve_PreservesData()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        var deviceId = "123-456-789";
        var publicKey = Convert.ToBase64String(new byte[32]);
        var friendlyName = "Dubai Office";

        // Act
        store.StoreTrustedDevice(deviceId, publicKey, friendlyName);
        var retrieved = store.RetrieveTrustedDevice(deviceId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.DeviceId.Should().Be(deviceId);
        retrieved.PublicKeyBase64.Should().Be(publicKey);
        retrieved.FriendlyName.Should().Be(friendlyName);
        retrieved.AddedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RemoveTrustedDevice_RemovesDevice()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        var deviceId = "123-456-789";
        store.StoreTrustedDevice(deviceId, "pubkey", "Test");

        // Act
        store.RemoveTrustedDevice(deviceId);
        var retrieved = store.RetrieveTrustedDevice(deviceId);

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void ListTrustedDevices_ReturnsAllStoredDevices()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        store.StoreTrustedDevice("111-111-111", "key1", "Device 1");
        store.StoreTrustedDevice("222-222-222", "key2", "Device 2");
        store.StoreTrustedDevice("333-333-333", "key3", "Device 3");

        // Act
        var devices = store.ListTrustedDevices();

        // Assert
        devices.Should().HaveCount(3);
        devices.Select(d => d.DeviceId).Should().BeEquivalentTo(new[] { "111-111-111", "222-222-222", "333-333-333" });
    }

    [Fact]
    public void DifferentEntropy_PreventsDecryption()
    {
        // Arrange
        var entropy1 = Encoding.UTF8.GetBytes("Entropy1");
        var entropy2 = Encoding.UTF8.GetBytes("Entropy2");

        using (var store1 = new WindowsDpapiCredentialStore(_testDirectory, entropy1))
        {
            store1.Store("key", "value"u8.ToArray());
        }

        // Act
        using var store2 = new WindowsDpapiCredentialStore(_testDirectory, entropy2);
        var result = store2.Retrieve("key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TamperedData_ReturnsNull()
    {
        // Arrange
        using var store = new WindowsDpapiCredentialStore(_testDirectory, _testEntropy);
        store.Store("key", "original_value"u8.ToArray());
        var filePath = Path.Combine(_testDirectory, "key.dat");

        // Tamper with the file
        var tampered = File.ReadAllBytes(filePath);
        tampered[0] ^= 0xFF;
        File.WriteAllBytes(filePath, tampered);

        // Act
        var result = store.Retrieve("key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DefaultEntropy_IsMachineAndUserSpecific()
    {
        // Arrange
        var store1 = new WindowsDpapiCredentialStore(_testDirectory); // Uses default entropy
        var store2 = new WindowsDpapiCredentialStore(_testDirectory); // Should use same default

        // Act
        store1.Store("key", "value"u8.ToArray());
        var result = store2.Retrieve("key");

        // Assert
        result.Should().NotBeNull(); // Same machine/user should work
        store1.Dispose();
        store2.Dispose();
    }
}
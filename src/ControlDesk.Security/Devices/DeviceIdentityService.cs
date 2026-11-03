using System;
using System.Security.Cryptography;
using ControlDesk.Core.Devices;
using ControlDesk.Security.Cryptography;
using ControlDesk.Security.Storage;

namespace ControlDesk.Security.Devices;

/// <summary>
/// Manages the persistent device identity including Ed25519 keypair generation,
/// device ID formatting, and cryptographic operations.
/// </summary>
public sealed class DeviceIdentityService : IDeviceIdentityService
{
    private readonly ICredentialStore _credentialStore;
    private DeviceIdentity? _currentIdentity;

    public DeviceIdentityService(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    /// <inheritdoc />
    public DeviceIdentity GetCurrentIdentity()
    {
        if (_currentIdentity != null)
            return _currentIdentity;

        // Try to load existing keypair
        var keyPair = _credentialStore.RetrieveKeyPair("device_identity");

        if (keyPair == null)
        {
            // Generate new keypair
            keyPair = new Ed25519KeyPair();
            _credentialStore.StoreKeyPair("device_identity", keyPair);
        }

        var deviceId = Ed25519KeyPair.GenerateDeviceId();
        var formattedId = FormatDeviceId(deviceId);

        _currentIdentity = new DeviceIdentity
        {
            DeviceId = deviceId,
            FormattedDeviceId = formattedId,
            PublicKey = keyPair.PublicKey.ToArray(),
            PrivateKey = keyPair.ExportPrivateKey(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        return _currentIdentity;
    }

    /// <inheritdoc />
    public string FormatDeviceId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return "000-000-000";

        // Clean the input
        var cleaned = CleanDeviceId(rawId);

        // Ensure 9 digits
        if (cleaned.Length != 9 || !ulong.TryParse(cleaned, out _))
        {
            // Fallback: generate new
            cleaned = Ed25519KeyPair.GenerateDeviceId().Replace("-", "");
        }

        return $"{cleaned[..3]}-{cleaned[3..6]}-{cleaned[6..]}";
    }

    /// <inheritdoc />
    public string CleanDeviceId(string formattedId)
    {
        if (string.IsNullOrWhiteSpace(formattedId))
            return string.Empty;

        return formattedId.Replace("-", "").Replace(" ", "").Trim();
    }

    /// <inheritdoc />
    public byte[] SignData(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty", nameof(data));

        var keyPair = _credentialStore.RetrieveKeyPair("device_identity");
        if (keyPair == null)
        {
            GetCurrentIdentity(); // Ensure generated if not existing
            keyPair = _credentialStore.RetrieveKeyPair("device_identity")
                ?? throw new InvalidOperationException("Key pair not found in credential store");
        }

        return keyPair.Sign(data);
    }

    /// <inheritdoc />
    public bool VerifySignature(byte[] data, byte[] signature, string publicKeyBase64)
    {
        if (data == null || data.Length == 0)
            return false;
        if (signature == null || signature.Length != 64)
            return false;
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
            return false;

        try
        {
            var publicKey = Convert.FromBase64String(publicKeyBase64);
            if (publicKey.Length != 32)
                return false;

            return Ed25519KeyPair.Verify(publicKey, data, signature);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exports the current device's public key as Base64 for sharing.
    /// </summary>
    public string ExportPublicKeyBase64()
    {
        var identity = GetCurrentIdentity();
        return Convert.ToBase64String(identity.PublicKey);
    }

    /// <summary>
    /// Exports the current device's formatted device ID.
    /// </summary>
    public string GetFormattedDeviceId()
    {
        return GetCurrentIdentity().FormattedDeviceId;
    }

    /// <summary>
    /// Exports the current device's raw device ID (9 digits, no dashes).
    /// </summary>
    public string GetRawDeviceId()
    {
        return GetCurrentIdentity().DeviceId;
    }
}
using System.Security.Cryptography;
using ControlDesk.Core.Devices;

namespace ControlDesk.Security.Cryptography;

/// <summary>
/// Ed25519 key pair for device identity, mutual challenge authentication, and unattended access pairing.
/// </summary>
public sealed class Ed25519KeyPair : IDisposable
{
    private readonly byte[] _privateKey;
    private readonly byte[] _publicKey;
    private bool _disposed;

    public const int PrivateKeySize = Ed25519Algorithm.SecretKeySize;
    public const int PublicKeySize = Ed25519Algorithm.PublicKeySize;
    public const int SignatureSize = Ed25519Algorithm.SignatureSize;

    /// <summary>
    /// Creates a new random Ed25519 key pair.
    /// </summary>
    public Ed25519KeyPair()
    {
        _privateKey = new byte[PrivateKeySize];
        _publicKey = new byte[PublicKeySize];
        Ed25519Algorithm.GenerateKeyPair(_privateKey, _publicKey);
    }

    /// <summary>
    /// Creates a key pair from an existing 32-byte private key.
    /// </summary>
    public Ed25519KeyPair(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != PrivateKeySize)
            throw new ArgumentException($"Private key must be {PrivateKeySize} bytes", nameof(privateKey));

        _privateKey = privateKey.ToArray();
        _publicKey = new byte[PublicKeySize];
        Ed25519Algorithm.DerivePublicKey(_privateKey, _publicKey);
    }

    /// <summary>
    /// Creates a key pair from existing private and public keys.
    /// </summary>
    public Ed25519KeyPair(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != PrivateKeySize)
            throw new ArgumentException($"Private key must be {PrivateKeySize} bytes", nameof(privateKey));
        if (publicKey.Length != PublicKeySize)
            throw new ArgumentException($"Public key must be {PublicKeySize} bytes", nameof(publicKey));

        _privateKey = privateKey.ToArray();
        _publicKey = publicKey.ToArray();
    }

    /// <summary>
    /// Gets the public key as Base64 string.
    /// </summary>
    public string PublicKeyBase64 => Convert.ToBase64String(_publicKey);

    /// <summary>
    /// Gets the public key bytes (read-only span).
    /// </summary>
    public ReadOnlySpan<byte> PublicKey => _publicKey;

    /// <summary>
    /// Signs data using the Ed25519 private key.
    /// </summary>
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        return Ed25519Algorithm.Sign(_privateKey, data);
    }

    /// <summary>
    /// Verifies a signature against a given public key.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        return Ed25519Algorithm.Verify(publicKey, data, signature);
    }

    /// <summary>
    /// Verifies a signature against this key pair's public key.
    /// </summary>
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        return Verify(_publicKey, data, signature);
    }

    /// <summary>
    /// Exports the private key bytes.
    /// </summary>
    public byte[] ExportPrivateKey()
    {
        ThrowIfDisposed();
        return _privateKey.ToArray();
    }

    /// <summary>
    /// Exports the public key bytes.
    /// </summary>
    public byte[] ExportPublicKey()
    {
        ThrowIfDisposed();
        return _publicKey.ToArray();
    }

    /// <summary>
    /// Creates a DeviceIdentity model from this key pair.
    /// </summary>
    public DeviceIdentity ToDeviceIdentity(string deviceId, string friendlyName)
    {
        return new DeviceIdentity(deviceId, friendlyName, PublicKeyBase64, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Generates a cryptographically secure 9-digit device ID (formatted XXX-XXX-XXX).
    /// </summary>
    public static string GenerateDeviceId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);

        ulong value = BitConverter.ToUInt64(bytes) % 1_000_000_000;
        string digits = value.ToString("D9");
        return $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Ed25519KeyPair));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_privateKey);
            CryptographicOperations.ZeroMemory(_publicKey);
            _disposed = true;
        }
    }
}

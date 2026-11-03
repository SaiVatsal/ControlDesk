using ControlDesk.Shared.Protocol;

namespace ControlDesk.Core.Devices;

/// <summary>
/// Device identity representation with Ed25519 keypair and device metadata.
/// </summary>
public record DeviceIdentity
{
    /// <summary>
    /// Raw 9-digit device ID (no dashes).
    /// </summary>
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>
    /// Formatted device ID as XXX-XXX-XXX.
    /// </summary>
    public string FormattedDeviceId { get; init; } = string.Empty;

    /// <summary>
    /// User-friendly display name for the device.
    /// </summary>
    public string FriendlyName { get; init; } = string.Empty;

    /// <summary>
    /// Ed25519 public key as Base64 string.
    /// </summary>
    public string PublicKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Ed25519 public key as raw bytes (32 bytes).
    /// </summary>
    public byte[] PublicKey { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Ed25519 private key as raw bytes (32 bytes). Null if not available (e.g., for remote devices).
    /// </summary>
    public byte[]? PrivateKey { get; init; }

    /// <summary>
    /// Timestamp when this identity was created (UTC).
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Alias for CreatedAtUtc for backward compatibility.
    /// </summary>
    public DateTimeOffset CreatedAt
    {
        get => CreatedAtUtc;
        init => CreatedAtUtc = value;
    }

    /// <summary>
    /// Creates a DeviceIdentity from raw components (used by Ed25519KeyPair.ToDeviceIdentity).
    /// </summary>
    public DeviceIdentity(string deviceId, string friendlyName, string publicKeyBase64, DateTimeOffset createdAtUtc)
    {
        DeviceId = deviceId.Replace("-", "").Replace(" ", "");
        FormattedDeviceId = FormatDeviceId(DeviceId);
        FriendlyName = friendlyName;
        PublicKeyBase64 = publicKeyBase64;
        PublicKey = Convert.FromBase64String(publicKeyBase64);
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Default constructor for object initializer syntax.
    /// </summary>
    public DeviceIdentity() { }

    private static string FormatDeviceId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId) || rawId.Length != 9)
            return "000-000-000";

        return $"{rawId[..3]}-{rawId[3..6]}-{rawId[6..]}";
    }
}

/// <summary>
/// Known or paired remote device.
/// </summary>
public record KnownDevice(
    string DeviceId,
    string FriendlyName,
    string? PublicKeyBase64,
    bool IsTrustedForUnattended,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset AddedAtUtc
);

/// <summary>
/// Service providing cryptographic identity and device registration.
/// </summary>
public interface IDeviceIdentityService
{
    DeviceIdentity GetCurrentIdentity();
    string FormatDeviceId(string rawId);
    string CleanDeviceId(string formattedId);
    byte[] SignData(byte[] data);
    bool VerifySignature(byte[] data, byte[] signature, string publicKeyBase64);
}
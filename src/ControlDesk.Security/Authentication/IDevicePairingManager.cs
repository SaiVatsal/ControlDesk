using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Security.Authentication;

/// <summary>
/// Interface for managing trusted device pairing and unattended access authorization.
/// </summary>
public interface IDevicePairingManager
{
    /// <summary>
    /// Adds a device to the trusted devices list, enabling unattended access.
    /// </summary>
    /// <param name="deviceId">The formatted device ID (XXX-XXX-XXX).</param>
    /// <param name="publicKeyBase64">The device's Ed25519 public key in Base64.</param>
    /// <param name="friendlyName">User-friendly display name for the device.</param>
    /// <param name="permissions">Permissions granted to this trusted device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the device was added or updated.</returns>
    Task<bool> AddTrustedDeviceAsync(
        string deviceId,
        string publicKeyBase64,
        string friendlyName,
        SessionPermissions permissions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a device from the trusted devices list, revoking unattended access.
    /// </summary>
    /// <param name="deviceId">The formatted device ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the device was removed.</returns>
    Task<bool> RemoveTrustedDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a device is trusted for unattended access.
    /// </summary>
    /// <param name="deviceId">The formatted device ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trusted device info if trusted, null otherwise.</returns>
    Task<TrustedDeviceInfo?> GetTrustedDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all trusted devices.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all trusted devices.</returns>
    Task<IReadOnlyList<TrustedDeviceInfo>> GetAllTrustedDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates permissions for an existing trusted device.
    /// </summary>
    /// <param name="deviceId">The formatted device ID.</param>
    /// <param name="permissions">New permissions to grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated successfully.</returns>
    Task<bool> UpdateTrustedDevicePermissionsAsync(string deviceId, SessionPermissions permissions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a device's public key matches the trusted record (anti-spoofing).
    /// </summary>
    /// <param name="deviceId">The formatted device ID.</param>
    /// <param name="publicKeyBase64">The public key to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the key matches the trusted record.</returns>
    Task<bool> ValidateTrustedDeviceKeyAsync(string deviceId, string publicKeyBase64, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a trusted device for unattended access.
/// </summary>
public record TrustedDeviceInfo(
    string DeviceId,
    string PublicKeyBase64,
    string FriendlyName,
    SessionPermissions Permissions,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastConnectedAt = null,
    bool IsEnabled = true
);
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ControlDesk.Security.Authentication;

/// <summary>
/// Interface for tamper-evident audit logging of security events.
/// </summary>
public interface IAuditLogger : IDisposable
{
    /// <summary>
    /// Logs an authentication attempt (success or failure).
    /// </summary>
    /// <param name="localDeviceId">The local device's formatted ID.</param>
    /// <param name="remoteDeviceId">The remote device's formatted ID (if known).</param>
    /// <param name="remotePublicKeyBase64">The remote device's public key (if known).</param>
    /// <param name="outcome">Authentication outcome.</param>
    /// <param name="failureReason">Reason for failure (if applicable).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAuthenticationAsync(
        string localDeviceId,
        string? remoteDeviceId,
        string? remotePublicKeyBase64,
        AuthenticationOutcome outcome,
        string? failureReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a session lifecycle event.
    /// </summary>
    /// <param name="localDeviceId">The local device's formatted ID.</param>
    /// <param name="remoteDeviceId">The remote device's formatted ID.</param>
    /// <param name="eventType">Type of session event.</param>
    /// <param name="details">Additional details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogSessionEventAsync(
        string localDeviceId,
        string remoteDeviceId,
        SessionEventType eventType,
        string? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a device pairing/unpairing event.
    /// </summary>
    /// <param name="localDeviceId">The local device's formatted ID.</param>
    /// <param name="remoteDeviceId">The remote device's formatted ID.</param>
    /// <param name="eventType">Type of pairing event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogPairingEventAsync(
        string localDeviceId,
        string remoteDeviceId,
        PairingEventType eventType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a security-sensitive configuration change.
    /// </summary>
    /// <param name="localDeviceId">The local device's formatted ID.</param>
    /// <param name="settingName">Name of the setting changed.</param>
    /// <param name="oldValue">Previous value (redacted if sensitive).</param>
    /// <param name="newValue">New value (redacted if sensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogConfigurationChangeAsync(
        string localDeviceId,
        string settingName,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves recent audit entries for display or export.
    /// </summary>
    /// <param name="count">Maximum number of entries to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of audit entries, newest first.</returns>
    Task<IReadOnlyList<AuditEntry>> GetRecentEntriesAsync(int count = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the integrity of the audit log chain.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the chain is intact, false if tampering detected.</returns>
    Task<bool> VerifyIntegrityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Authentication outcome for audit logging.
/// </summary>
public enum AuthenticationOutcome
{
    Success,
    Failure_InvalidSignature,
    Failure_UnknownDevice,
    Failure_DeviceNotTrusted,
    Failure_ChallengeExpired,
    Failure_ReplayDetected,
    Failure_InternalError
}

/// <summary>
/// Session lifecycle event types.
/// </summary>
public enum SessionEventType
{
    SessionRequested,
    SessionAccepted,
    SessionDeclined,
    SessionStarted,
    SessionEnded,
    SessionReconnected,
    SessionFailed,
    SessionTimedOut,
    SessionTerminatedByLocal,
    SessionTerminatedByRemote,
    SessionTerminatedByError
}

/// <summary>
/// Device pairing event types.
/// </summary>
public enum PairingEventType
{
    DevicePaired,
    DeviceUnpaired,
    DevicePermissionsChanged,
    DeviceKeyVerified,
    DeviceKeyMismatch
}

/// <summary>
/// An audit log entry with tamper-evident chaining.
/// </summary>
public record AuditEntry(
    long SequenceNumber,
    DateTimeOffset TimestampUtc,
    string LocalDeviceId,
    string EventType,
    string? RemoteDeviceId,
    string? Details,
    string PreviousHashBase64,
    string CurrentHashBase64
);
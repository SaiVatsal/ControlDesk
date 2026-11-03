using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ControlDesk.Core.Devices;
using ControlDesk.Security.Cryptography;
using ControlDesk.Security.Storage;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Security.Authentication;

/// <summary>
/// Challenge-response authenticator for mutual device authentication and unattended access.
/// </summary>
public sealed class ChallengeResponseAuthenticator : IChallengeResponseAuthenticator
{
    private readonly ICredentialStore _credentialStore;
    private readonly Ed25519KeyPair _localKeyPair;
    private readonly TimeSpan _nonceValidityDuration;

    public ChallengeResponseAuthenticator(
        ICredentialStore credentialStore,
        Ed25519KeyPair localKeyPair,
        TimeSpan? nonceValidityDuration = null)
    {
        _credentialStore = credentialStore;
        _localKeyPair = localKeyPair;
        _nonceValidityDuration = nonceValidityDuration ?? TimeSpan.FromMinutes(2);
    }

    /// <inheritdoc />
    public byte[] GenerateChallenge(int length = 32)
    {
        var nonce = new byte[length];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    /// <inheritdoc />
    public Task<byte[]> CreateResponseAsync(byte[] challenge, CancellationToken cancellationToken = default)
    {
        if (challenge == null || challenge.Length == 0)
            throw new ArgumentException("Challenge cannot be null or empty", nameof(challenge));

        var signature = _localKeyPair.Sign(challenge);
        return Task.FromResult(signature);
    }

    /// <inheritdoc />
    public Task<bool> VerifyResponseAsync(byte[] challenge, byte[] response, string remotePublicKeyBase64, CancellationToken cancellationToken = default)
    {
        if (challenge == null || challenge.Length == 0)
            return Task.FromResult(false);
        if (response == null || response.Length != 64)
            return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(remotePublicKeyBase64))
            return Task.FromResult(false);

        try
        {
            var publicKey = Convert.FromBase64String(remotePublicKeyBase64);
            if (publicKey.Length != 32)
                return Task.FromResult(false);

            var isValid = Ed25519KeyPair.Verify(publicKey, challenge, response);
            return Task.FromResult(isValid);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Generates a challenge request to send to the target device (legacy API for signaling).
    /// </summary>
    public ChallengeRequestPayload CreateChallengeRequest(string targetDeviceId, string controllerDeviceId)
    {
        return new ChallengeRequestPayload(
            TargetDeviceId: targetDeviceId,
            ControllerDeviceId: controllerDeviceId,
            ControllerPublicKeyBase64: _localKeyPair.PublicKeyBase64
        );
    }

    /// <summary>
    /// Processes an incoming challenge request and generates a response with nonce (legacy API for signaling).
    /// </summary>
    public ChallengeResponsePayload ProcessChallengeRequest(ChallengeRequestPayload request)
    {
        var nonce = new byte[32];
        RandomNumberGenerator.Fill(nonce);

        var validUntil = DateTimeOffset.UtcNow.Add(_nonceValidityDuration);

        return new ChallengeResponsePayload(
            NonceBase64: Convert.ToBase64String(nonce),
            ServerTimestampUtc: DateTimeOffset.UtcNow,
            ValidUntilUtc: validUntil
        );
    }

    /// <summary>
    /// Verifies a challenge response by checking the signature (legacy API for signaling).
    /// Returns the verified controller's public key if successful.
    /// </summary>
    public bool VerifyChallengeResponse(
        ChallengeResponsePayload challengeResponse,
        ChallengeVerifyPayload verification,
        string controllerDeviceId,
        out string? controllerPublicKeyBase64)
    {
        controllerPublicKeyBase64 = null;

        // Check if nonce is still valid
        if (DateTimeOffset.UtcNow > challengeResponse.ValidUntilUtc)
            return false;

        // Retrieve the trusted device's public key
        var trustedDevice = _credentialStore.RetrieveTrustedDevice(controllerDeviceId);
        if (trustedDevice == null)
            return false;

        controllerPublicKeyBase64 = trustedDevice.PublicKeyBase64;
        var publicKeyBytes = Convert.FromBase64String(trustedDevice.PublicKeyBase64);
        var signature = Convert.FromBase64String(verification.SignatureBase64);
        var nonceBytes = Convert.FromBase64String(challengeResponse.NonceBase64);

        // Verify the signature over the nonce
        return Ed25519KeyPair.Verify(publicKeyBytes, nonceBytes, signature);
    }

    /// <summary>
    /// Creates a challenge verification payload to send back (controller side).
    /// </summary>
    public ChallengeVerifyPayload CreateChallengeVerification(
        ChallengeResponsePayload challengeResponse,
        string sdpOffer)
    {
        var nonceBytes = Convert.FromBase64String(challengeResponse.NonceBase64);
        var signature = _localKeyPair.Sign(nonceBytes);

        return new ChallengeVerifyPayload(
            SignatureBase64: Convert.ToBase64String(signature),
            SdpOffer: sdpOffer
        );
    }

    /// <summary>
    /// Creates a challenge request payload for the host to initiate unattended access verification.
    /// </summary>
    public ChallengeRequestPayload CreateHostChallengeRequest(string targetDeviceId)
    {
        return new ChallengeRequestPayload(
            TargetDeviceId: targetDeviceId,
            ControllerDeviceId: string.Empty, // Will be filled by the host's identity
            ControllerPublicKeyBase64: _localKeyPair.PublicKeyBase64
        );
    }
}

/// <summary>
/// Manages device pairing for unattended access.
/// </summary>
public sealed class DevicePairingManager : IDevicePairingManager
{
    private readonly ICredentialStore _credentialStore;

    public DevicePairingManager(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    /// <inheritdoc />
    public Task<bool> AddTrustedDeviceAsync(
        string deviceId,
        string publicKeyBase64,
        string friendlyName,
        SessionPermissions permissions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
            return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(friendlyName))
            return Task.FromResult(false);

        try
        {
            var deviceData = new TrustedDeviceData
            {
                DeviceId = deviceId,
                PublicKeyBase64 = publicKeyBase64,
                FriendlyName = friendlyName,
                Permissions = permissions,
                PairedAt = DateTimeOffset.UtcNow,
                IsEnabled = true
            };

            _credentialStore.StoreTrustedDevice(deviceData);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveTrustedDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult(false);

        try
        {
            _credentialStore.RemoveTrustedDevice(deviceId);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<TrustedDeviceInfo?> GetTrustedDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult<TrustedDeviceInfo?>(null);

        try
        {
            var device = _credentialStore.RetrieveTrustedDevice(deviceId);
            if (device == null)
                return Task.FromResult<TrustedDeviceInfo?>(null);

            return Task.FromResult<TrustedDeviceInfo?>(new TrustedDeviceInfo(
                device.DeviceId,
                device.PublicKeyBase64,
                device.FriendlyName,
                device.Permissions,
                device.PairedAt,
                device.LastConnectedAt,
                device.IsEnabled
            ));
        }
        catch
        {
            return Task.FromResult<TrustedDeviceInfo?>(null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TrustedDeviceInfo>> GetAllTrustedDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var devices = _credentialStore.ListTrustedDevices();
            var result = devices.Select(d => new TrustedDeviceInfo(
                d.DeviceId,
                d.PublicKeyBase64,
                d.FriendlyName,
                d.Permissions,
                d.PairedAt,
                d.LastConnectedAt,
                d.IsEnabled
            )).ToList();

            return Task.FromResult<IReadOnlyList<TrustedDeviceInfo>>(result);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<TrustedDeviceInfo>>(Array.Empty<TrustedDeviceInfo>());
        }
    }

    /// <inheritdoc />
    public Task<bool> UpdateTrustedDevicePermissionsAsync(string deviceId, SessionPermissions permissions, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return Task.FromResult(false);

        try
        {
            var device = _credentialStore.RetrieveTrustedDevice(deviceId);
            if (device == null)
                return Task.FromResult(false);

            // Update with new permissions (preserving other fields)
            var updatedDevice = new TrustedDeviceData
            {
                DeviceId = device.DeviceId,
                PublicKeyBase64 = device.PublicKeyBase64,
                FriendlyName = device.FriendlyName,
                Permissions = permissions,
                PairedAt = device.PairedAt,
                LastConnectedAt = device.LastConnectedAt,
                IsEnabled = device.IsEnabled
            };

            _credentialStore.UpdateTrustedDevice(updatedDevice);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public Task<bool> ValidateTrustedDeviceKeyAsync(string deviceId, string publicKeyBase64, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(publicKeyBase64))
            return Task.FromResult(false);

        try
        {
            var device = _credentialStore.RetrieveTrustedDevice(deviceId);
            if (device == null)
                return Task.FromResult(false);

            return Task.FromResult(device.PublicKeyBase64 == publicKeyBase64);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Adds a device as trusted for unattended access (legacy synchronous API).
    /// </summary>
    public void AddTrustedDevice(string deviceId, string publicKeyBase64, string friendlyName)
    {
        _credentialStore.StoreTrustedDevice(deviceId, publicKeyBase64, friendlyName);
    }

    /// <summary>
    /// Removes a device from trusted list (legacy synchronous API).
    /// </summary>
    public void RemoveTrustedDevice(string deviceId)
    {
        _credentialStore.RemoveTrustedDevice(deviceId);
    }

    /// <summary>
    /// Checks if a device is trusted for unattended access (legacy synchronous API).
    /// </summary>
    public bool IsTrustedDevice(string deviceId)
    {
        return _credentialStore.RetrieveTrustedDevice(deviceId) != null;
    }

    /// <summary>
    /// Gets all trusted devices (legacy synchronous API).
    /// </summary>
    public IReadOnlyList<TrustedDeviceData> GetTrustedDevices()
    {
        return _credentialStore.ListTrustedDevices();
    }

    /// <summary>
    /// Gets trusted device info (legacy synchronous API).
    /// </summary>
    public TrustedDeviceData? GetTrustedDevice(string deviceId)
    {
        return _credentialStore.RetrieveTrustedDevice(deviceId);
    }
}

/// <summary>
/// Audit log entry for security events.
/// </summary>
public sealed record AuditLogEntry(
    string EventId,
    DateTimeOffset TimestampUtc,
    AuditEventType EventType,
    string LocalDeviceId,
    string RemoteDeviceId,
    string? Details,
    AuditOutcome Outcome
);

/// <summary>
/// Types of auditable security events.
/// </summary>
public enum AuditEventType
{
    SessionInitiated,
    SessionAccepted,
    SessionRejected,
    SessionTerminated,
    AuthenticationSuccess,
    AuthenticationFailed,
    DevicePaired,
    DeviceRevoked,
    PermissionChanged,
    UnattendedAccessGranted,
    UnattendedAccessDenied,
    CredentialAccessed,
    ConfigurationChanged
}

/// <summary>
/// Outcome of an audit event.
/// </summary>
public enum AuditOutcome
{
    Success,
    Failure,
    Denied,
    Error
}

/// <summary>
/// Tamper-evident audit logger using chained hashes.
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly string _logDirectory;
    private readonly byte[] _chainKey;
    private byte[] _lastHash = Array.Empty<byte>();
    private bool _disposed;

    public AuditLogger(string? logDirectory = null, byte[]? chainKey = null)
    {
        _logDirectory = logDirectory ?? GetDefaultLogDirectory();
        _chainKey = chainKey ?? GenerateChainKey();
        Directory.CreateDirectory(_logDirectory);
        _lastHash = LoadLastHash();
    }

    /// <inheritdoc />
    public async Task LogAuthenticationAsync(
        string localDeviceId,
        string? remoteDeviceId,
        string? remotePublicKeyBase64,
        AuthenticationOutcome outcome,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuditLogger));

        var details = $"Outcome: {outcome}";
        if (!string.IsNullOrEmpty(remoteDeviceId))
            details += $", Remote: {remoteDeviceId}";
        if (!string.IsNullOrEmpty(failureReason))
            details += $", Reason: {failureReason}";

        var entry = new AuditLogEntry(
            EventId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            EventType: AuditEventType.AuthenticationSuccess,
            LocalDeviceId: localDeviceId,
            RemoteDeviceId: remoteDeviceId ?? string.Empty,
            Details: details,
            Outcome: outcome == AuthenticationOutcome.Success ? AuditOutcome.Success : AuditOutcome.Failure
        );

        await LogInternalAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogSessionEventAsync(
        string localDeviceId,
        string remoteDeviceId,
        SessionEventType eventType,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuditLogger));

        var auditEventType = eventType switch
        {
            SessionEventType.SessionRequested => AuditEventType.SessionInitiated,
            SessionEventType.SessionAccepted => AuditEventType.SessionAccepted,
            SessionEventType.SessionDeclined => AuditEventType.SessionRejected,
            SessionEventType.SessionStarted => AuditEventType.SessionAccepted,
            SessionEventType.SessionEnded => AuditEventType.SessionTerminated,
            SessionEventType.SessionReconnected => AuditEventType.SessionAccepted,
            SessionEventType.SessionFailed => AuditEventType.SessionTerminated,
            SessionEventType.SessionTimedOut => AuditEventType.SessionTerminated,
            SessionEventType.SessionTerminatedByLocal => AuditEventType.SessionTerminated,
            SessionEventType.SessionTerminatedByRemote => AuditEventType.SessionTerminated,
            SessionEventType.SessionTerminatedByError => AuditEventType.SessionTerminated,
            _ => AuditEventType.SessionTerminated
        };

        var entry = new AuditLogEntry(
            EventId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            EventType: auditEventType,
            LocalDeviceId: localDeviceId,
            RemoteDeviceId: remoteDeviceId,
            Details: details ?? eventType.ToString(),
            Outcome: AuditOutcome.Success
        );

        await LogInternalAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogPairingEventAsync(
        string localDeviceId,
        string remoteDeviceId,
        PairingEventType eventType,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuditLogger));

        var auditEventType = eventType switch
        {
            PairingEventType.DevicePaired => AuditEventType.DevicePaired,
            PairingEventType.DeviceUnpaired => AuditEventType.DeviceRevoked,
            PairingEventType.DevicePermissionsChanged => AuditEventType.PermissionChanged,
            PairingEventType.DeviceKeyVerified => AuditEventType.UnattendedAccessGranted,
            PairingEventType.DeviceKeyMismatch => AuditEventType.UnattendedAccessDenied,
            _ => AuditEventType.DevicePaired
        };

        var entry = new AuditLogEntry(
            EventId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            EventType: auditEventType,
            LocalDeviceId: localDeviceId,
            RemoteDeviceId: remoteDeviceId,
            Details: eventType.ToString(),
            Outcome: AuditOutcome.Success
        );

        await LogInternalAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LogConfigurationChangeAsync(
        string localDeviceId,
        string settingName,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuditLogger));

        var details = $"Setting: {settingName}, Old: {oldValue ?? "(none)"}, New: {newValue ?? "(none)"}";

        var entry = new AuditLogEntry(
            EventId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            EventType: AuditEventType.ConfigurationChanged,
            LocalDeviceId: localDeviceId,
            RemoteDeviceId: string.Empty,
            Details: details,
            Outcome: AuditOutcome.Success
        );

        await LogInternalAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> GetRecentEntriesAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuditLogger));

        var files = Directory.GetFiles(_logDirectory, "*.json")
            .OrderByDescending(f => f)
            .Take(count)
            .ToArray();

        var entries = new List<AuditEntry>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = File.ReadAllText(file);
                var stored = System.Text.Json.JsonSerializer.Deserialize<StoredAuditEntry>(json);

                if (stored != null)
                {
                    entries.Add(new AuditEntry(
                        SequenceNumber: 0, // Would need to track this
                        TimestampUtc: stored.Entry.TimestampUtc,
                        LocalDeviceId: stored.Entry.LocalDeviceId,
                        EventType: stored.Entry.EventType.ToString(),
                        RemoteDeviceId: stored.Entry.RemoteDeviceId,
                        Details: stored.Entry.Details,
                        PreviousHashBase64: Convert.ToBase64String(stored.PreviousHash),
                        CurrentHashBase64: Convert.ToBase64String(stored.ChainHash)
                    ));
                }
            }
            catch
            {
                // Skip corrupted entries
            }
        }

        return entries;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var result = VerifyIntegrity(out _, out _);
        return await Task.FromResult(result).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a security event with tamper-evident chaining (legacy synchronous API).
    /// </summary>
    public void Log(AuditLogEntry entry)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuditLogger));

        var entryData = SerializeEntry(entry);
        var chainHash = ComputeChainHash(_lastHash, entryData);

        var logEntry = new StoredAuditEntry
        {
            Entry = entry,
            PreviousHash = _lastHash,
            ChainHash = chainHash
        };

        var json = System.Text.Json.JsonSerializer.Serialize(logEntry);
        var fileName = $"{entry.TimestampUtc:yyyyMMdd_HHmmss_ffff}_{entry.EventId}.json";
        var filePath = Path.Combine(_logDirectory, fileName);

        File.WriteAllText(filePath, json);
        _lastHash = chainHash;
    }

    /// <summary>
    /// Verifies the integrity of the audit log chain (legacy synchronous API).
    /// </summary>
    public bool VerifyIntegrity(out int verifiedCount, out int corruptedCount)
    {
        verifiedCount = 0;
        corruptedCount = 0;
        var files = Directory.GetFiles(_logDirectory, "*.json")
            .OrderBy(f => f)
            .ToArray();

        byte[] previousHash = Array.Empty<byte>();

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var stored = System.Text.Json.JsonSerializer.Deserialize<StoredAuditEntry>(json);

                if (stored == null)
                {
                    corruptedCount++;
                    continue;
                }

                var entryData = SerializeEntry(stored.Entry);
                var expectedHash = ComputeChainHash(previousHash, entryData);

                if (!stored.ChainHash.SequenceEqual(expectedHash) ||
                    !stored.PreviousHash.SequenceEqual(previousHash))
                {
                    corruptedCount++;
                }
                else
                {
                    verifiedCount++;
                }

                previousHash = stored.ChainHash;
            }
            catch
            {
                corruptedCount++;
            }
        }

        return corruptedCount == 0;
    }

    private async Task LogInternalAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        var entryData = SerializeEntry(entry);
        var chainHash = ComputeChainHash(_lastHash, entryData);

        var logEntry = new StoredAuditEntry
        {
            Entry = entry,
            PreviousHash = _lastHash,
            ChainHash = chainHash
        };

        var json = System.Text.Json.JsonSerializer.Serialize(logEntry);
        var fileName = $"{entry.TimestampUtc:yyyyMMdd_HHmmss_ffff}_{entry.EventId}.json";
        var filePath = Path.Combine(_logDirectory, fileName);

        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        _lastHash = chainHash;
    }

    private byte[] LoadLastHash()
    {
        var files = Directory.GetFiles(_logDirectory, "*.json")
            .OrderByDescending(f => f)
            .ToArray();

        if (files.Length == 0)
            return Array.Empty<byte>();

        try
        {
            var json = File.ReadAllText(files[0]);
            var stored = System.Text.Json.JsonSerializer.Deserialize<StoredAuditEntry>(json);
            return stored?.ChainHash ?? Array.Empty<byte>();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private byte[] SerializeEntry(AuditLogEntry entry)
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry);
    }

    private byte[] ComputeChainHash(byte[] previousHash, byte[] entryData)
    {
        using var hmac = new HMACSHA256(_chainKey);
        var combined = new byte[previousHash.Length + entryData.Length];
        Buffer.BlockCopy(previousHash, 0, combined, 0, previousHash.Length);
        Buffer.BlockCopy(entryData, 0, combined, previousHash.Length, entryData.Length);
        return hmac.ComputeHash(combined);
    }

    private static string GetDefaultLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ControlDesk", "AuditLog");
    }

    private static byte[] GenerateChainKey()
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}|{userName}|ControlDesk.Audit.v1";
        return System.Text.Encoding.UTF8.GetBytes(combined);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private sealed class StoredAuditEntry
    {
        public AuditLogEntry Entry { get; set; } = null!;
        public byte[] PreviousHash { get; set; } = Array.Empty<byte>();
        public byte[] ChainHash { get; set; } = Array.Empty<byte>();
    }
}
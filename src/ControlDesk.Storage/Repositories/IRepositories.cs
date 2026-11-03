using ControlDesk.Core.Configuration;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Sessions;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Storage.Repositories;

/// <summary>
/// Session history record.
/// </summary>
public sealed class SessionRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string RemoteDeviceId { get; set; } = string.Empty;
    public string RemoteFriendlyName { get; set; } = string.Empty;
    public SessionRole Role { get; set; }
    public AccessMode AccessMode { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public long? DurationSeconds { get; set; }
    public TransportType TransportType { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public string? DisconnectReason { get; set; }
}

/// <summary>
/// Settings repository interface.
/// </summary>
public interface ISettingsRepository
{
    AppSettings GetSettings();
    void SaveSettings(AppSettings settings);
}

/// <summary>
/// Device repository interface.
/// </summary>
public interface IDeviceRepository
{
    void AddOrUpdateKnownDevice(KnownDevice device);
    KnownDevice? GetKnownDevice(string deviceId);
    IReadOnlyList<KnownDevice> GetAllKnownDevices();
    void UpdateLastConnected(string deviceId, DateTimeOffset timestamp);
    void SetTrustedForUnattended(string deviceId, bool isTrusted);
    void RemoveKnownDevice(string deviceId);
}

/// <summary>
/// Session history repository interface.
/// </summary>
public interface ISessionHistoryRepository
{
    void AddSession(SessionRecord session);
    void UpdateSession(SessionRecord session);
    SessionRecord? GetSession(string sessionId);
    IReadOnlyList<SessionRecord> GetRecentSessions(int count = 50);
    IReadOnlyList<SessionRecord> GetSessionsForDevice(string deviceId, int count = 20);
}

using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ControlDesk.Core.Configuration;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Sessions;
using ControlDesk.Security.Authentication;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Storage.Repositories;

/// <summary>
/// SQLite-based settings repository.
/// </summary>
public sealed class SettingsRepository : ISettingsRepository, IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SettingsRepository(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS KnownDevices (
                DeviceId TEXT PRIMARY KEY,
                FriendlyName TEXT NOT NULL,
                PublicKeyBase64 TEXT,
                IsTrustedForUnattended INTEGER NOT NULL DEFAULT 0,
                LastConnectedAt TEXT,
                AddedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SessionHistory (
                SessionId TEXT PRIMARY KEY,
                RemoteDeviceId TEXT NOT NULL,
                RemoteFriendlyName TEXT NOT NULL,
                Role TEXT NOT NULL,
                AccessMode TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT,
                DurationSeconds INTEGER,
                TransportType TEXT,
                BytesSent INTEGER DEFAULT 0,
                BytesReceived INTEGER DEFAULT 0,
                DisconnectReason TEXT,
                CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_SessionHistory_RemoteDeviceId ON SessionHistory(RemoteDeviceId);
            CREATE INDEX IF NOT EXISTS IX_SessionHistory_StartedAt ON SessionHistory(StartedAt);
        ";
        cmd.ExecuteNonQuery();
    }

    public AppSettings GetSettings()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings";

        var settings = new AppSettings();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            SetSettingProperty(settings, key, value);
        }

        return settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        using var transaction = _connection.BeginTransaction();
        try
        {
            var properties = GetSettingsProperties(settings);

            foreach (var (key, value) in properties)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO Settings (Key, Value, UpdatedAt)
                    VALUES ($key, $value, $updated)
                    ON CONFLICT(Key) DO UPDATE SET
                        Value = $value,
                        UpdatedAt = $updated";
                cmd.Parameters.AddWithValue("$key", key);
                cmd.Parameters.AddWithValue("$value", value);
                cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void SetSettingProperty(AppSettings settings, string key, string value)
    {
        // Flatten property path: General.DeviceFriendlyName, Security.EnableUnattendedAccess, etc.
        var parts = key.Split('.');
        if (parts.Length != 2) return;

        var section = parts[0];
        var property = parts[1];

        object? sectionObj = section switch
        {
            "General" => (object)settings.General,
            "Security" => settings.Security,
            "Network" => settings.Network,
            "Performance" => settings.Performance,
            _ => null
        };

        if (sectionObj == null) return;

        var prop = sectionObj.GetType().GetProperty(property);
        if (prop == null || !prop.CanWrite) return;

        var convertedValue = Convert.ChangeType(value, prop.PropertyType);
        prop.SetValue(sectionObj, convertedValue);
    }

    private IEnumerable<(string Key, string Value)> GetSettingsProperties(AppSettings settings)
    {
        var result = new List<(string, string)>();

        foreach (var prop in settings.General.GetType().GetProperties())
        {
            if (prop.CanRead)
            {
                result.Add(($"General.{prop.Name}", prop.GetValue(settings.General)?.ToString() ?? string.Empty));
            }
        }

        foreach (var prop in settings.Security.GetType().GetProperties())
        {
            if (prop.CanRead)
            {
                result.Add(($"Security.{prop.Name}", prop.GetValue(settings.Security)?.ToString() ?? string.Empty));
            }
        }

        foreach (var prop in settings.Network.GetType().GetProperties())
        {
            if (prop.CanRead)
            {
                result.Add(($"Network.{prop.Name}", prop.GetValue(settings.Network)?.ToString() ?? string.Empty));
            }
        }

        foreach (var prop in settings.Performance.GetType().GetProperties())
        {
            if (prop.CanRead)
            {
                result.Add(($"Performance.{prop.Name}", prop.GetValue(settings.Performance)?.ToString() ?? string.Empty));
            }
        }

        // Handle array properties
        if (settings.Network.StunServers != null)
        {
            result.Add(("Network.StunServers", JsonSerializer.Serialize(settings.Network.StunServers)));
        }

        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// SQLite-based device repository.
/// </summary>
public sealed class DeviceRepository : IDeviceRepository, IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public DeviceRepository(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public void AddOrUpdateKnownDevice(KnownDevice device)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO KnownDevices (DeviceId, FriendlyName, PublicKeyBase64, IsTrustedForUnattended, LastConnectedAt, AddedAt)
            VALUES ($deviceId, $friendlyName, $publicKey, $isTrusted, $lastConnected, $addedAt)
            ON CONFLICT(DeviceId) DO UPDATE SET
                FriendlyName = $friendlyName,
                PublicKeyBase64 = $publicKey,
                IsTrustedForUnattended = $isTrusted,
                LastConnectedAt = $lastConnected";
        cmd.Parameters.AddWithValue("$deviceId", device.DeviceId);
        cmd.Parameters.AddWithValue("$friendlyName", device.FriendlyName);
        cmd.Parameters.AddWithValue("$publicKey", (object?)device.PublicKeyBase64 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$isTrusted", device.IsTrustedForUnattended ? 1 : 0);
        cmd.Parameters.AddWithValue("$lastConnected", (object?)device.LastConnectedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$addedAt", device.AddedAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public KnownDevice? GetKnownDevice(string deviceId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM KnownDevices WHERE DeviceId = $deviceId";
        cmd.Parameters.AddWithValue("$deviceId", deviceId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadDevice(reader);
        }
        return null;
    }

    public IReadOnlyList<KnownDevice> GetAllKnownDevices()
    {
        var devices = new List<KnownDevice>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM KnownDevices ORDER BY LastConnectedAt DESC NULLS LAST, FriendlyName";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            devices.Add(ReadDevice(reader));
        }
        return devices;
    }

    public void UpdateLastConnected(string deviceId, DateTimeOffset timestamp)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE KnownDevices SET LastConnectedAt = $timestamp WHERE DeviceId = $deviceId";
        cmd.Parameters.AddWithValue("$deviceId", deviceId);
        cmd.Parameters.AddWithValue("$timestamp", timestamp.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SetTrustedForUnattended(string deviceId, bool isTrusted)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE KnownDevices SET IsTrustedForUnattended = $isTrusted WHERE DeviceId = $deviceId";
        cmd.Parameters.AddWithValue("$deviceId", deviceId);
        cmd.Parameters.AddWithValue("$isTrusted", isTrusted ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void RemoveKnownDevice(string deviceId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM KnownDevices WHERE DeviceId = $deviceId";
        cmd.Parameters.AddWithValue("$deviceId", deviceId);
        cmd.ExecuteNonQuery();
    }

    private KnownDevice ReadDevice(SqliteDataReader reader)
    {
        return new KnownDevice(
            DeviceId: reader.GetString(0),
            FriendlyName: reader.GetString(1),
            PublicKeyBase64: reader.IsDBNull(2) ? null : reader.GetString(2),
            IsTrustedForUnattended: reader.GetInt32(3) == 1,
            LastConnectedUtc: reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
            AddedAtUtc: DateTimeOffset.Parse(reader.GetString(5))
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// SQLite-based session history repository.
/// </summary>
public sealed class SessionHistoryRepository : ISessionHistoryRepository, IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SessionHistoryRepository(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public void AddSession(SessionRecord session)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO SessionHistory
            (SessionId, RemoteDeviceId, RemoteFriendlyName, Role, AccessMode, StartedAt, EndedAt, DurationSeconds, TransportType, BytesSent, BytesReceived, DisconnectReason, CreatedAt)
            VALUES
            ($sessionId, $remoteDeviceId, $remoteFriendlyName, $role, $accessMode, $startedAt, $endedAt, $durationSeconds, $transportType, $bytesSent, $bytesReceived, $disconnectReason, $createdAt)";
        cmd.Parameters.AddWithValue("$sessionId", session.SessionId);
        cmd.Parameters.AddWithValue("$remoteDeviceId", session.RemoteDeviceId);
        cmd.Parameters.AddWithValue("$remoteFriendlyName", session.RemoteFriendlyName);
        cmd.Parameters.AddWithValue("$role", session.Role.ToString());
        cmd.Parameters.AddWithValue("$accessMode", session.AccessMode.ToString());
        cmd.Parameters.AddWithValue("$startedAt", session.StartedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endedAt", (object?)session.EndedAtUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$durationSeconds", (object?)session.DurationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$transportType", session.TransportType.ToString());
        cmd.Parameters.AddWithValue("$bytesSent", session.BytesSent);
        cmd.Parameters.AddWithValue("$bytesReceived", session.BytesReceived);
        cmd.Parameters.AddWithValue("$disconnectReason", (object?)session.DisconnectReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void UpdateSession(SessionRecord session)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE SessionHistory SET
                EndedAt = $endedAt,
                DurationSeconds = $durationSeconds,
                TransportType = $transportType,
                BytesSent = $bytesSent,
                BytesReceived = $bytesReceived,
                DisconnectReason = $disconnectReason
            WHERE SessionId = $sessionId";
        cmd.Parameters.AddWithValue("$sessionId", session.SessionId);
        cmd.Parameters.AddWithValue("$endedAt", (object?)session.EndedAtUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$durationSeconds", (object?)session.DurationSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$transportType", session.TransportType.ToString());
        cmd.Parameters.AddWithValue("$bytesSent", session.BytesSent);
        cmd.Parameters.AddWithValue("$bytesReceived", session.BytesReceived);
        cmd.Parameters.AddWithValue("$disconnectReason", (object?)session.DisconnectReason ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public SessionRecord? GetSession(string sessionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SessionHistory WHERE SessionId = $sessionId";
        cmd.Parameters.AddWithValue("$sessionId", sessionId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadSession(reader);
        }
        return null;
    }

    public IReadOnlyList<SessionRecord> GetRecentSessions(int count = 50)
    {
        var sessions = new List<SessionRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SessionHistory ORDER BY StartedAt DESC LIMIT $count";
        cmd.Parameters.AddWithValue("$count", count);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(ReadSession(reader));
        }
        return sessions;
    }

    public IReadOnlyList<SessionRecord> GetSessionsForDevice(string deviceId, int count = 20)
    {
        var sessions = new List<SessionRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM SessionHistory WHERE RemoteDeviceId = $deviceId ORDER BY StartedAt DESC LIMIT $count";
        cmd.Parameters.AddWithValue("$deviceId", deviceId);
        cmd.Parameters.AddWithValue("$count", count);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(ReadSession(reader));
        }
        return sessions;
    }

    private SessionRecord ReadSession(SqliteDataReader reader)
    {
        return new SessionRecord
        {
            SessionId = reader.GetString(0),
            RemoteDeviceId = reader.GetString(1),
            RemoteFriendlyName = reader.GetString(2),
            Role = Enum.Parse<SessionRole>(reader.GetString(3)),
            AccessMode = Enum.Parse<AccessMode>(reader.GetString(4)),
            StartedAtUtc = DateTimeOffset.Parse(reader.GetString(5)),
            EndedAtUtc = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
            DurationSeconds = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            TransportType = Enum.Parse<TransportType>(reader.GetString(8)),
            BytesSent = reader.GetInt64(9),
            BytesReceived = reader.GetInt64(10),
            DisconnectReason = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
    }
}
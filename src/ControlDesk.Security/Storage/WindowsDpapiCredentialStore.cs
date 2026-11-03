using System.Security.Cryptography;
using ControlDesk.Core.Devices;
using ControlDesk.Security.Cryptography;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Security.Storage;

/// <summary>
/// Windows DPAPI-based secure credential store.
/// Protects secrets using the current user's Windows credentials.
/// </summary>
public sealed class WindowsDpapiCredentialStore : ICredentialStore
{
    private readonly byte[] _entropy;
    private readonly string _baseDirectory;

    public WindowsDpapiCredentialStore(string? baseDirectory = null, byte[]? entropy = null)
    {
        _entropy = entropy ?? GenerateDefaultEntropy();
        _baseDirectory = baseDirectory ?? GetDefaultBaseDirectory();

        Directory.CreateDirectory(_baseDirectory);
    }

    /// <summary>
    /// Stores a secret value with the given key.
    /// </summary>
    public void Store(string key, byte[] value)
    {
        var protectedData = ProtectedData.Protect(value, _entropy, DataProtectionScope.CurrentUser);
        var filePath = GetFilePath(key);
        File.WriteAllBytes(filePath, protectedData);
    }

    /// <summary>
    /// Retrieves a secret value by key.
    /// </summary>
    public byte[]? Retrieve(string key)
    {
        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
            return null;

        var protectedData = File.ReadAllBytes(filePath);
        try
        {
            return ProtectedData.Unprotect(protectedData, _entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Data was tampered with or encrypted with different credentials
            return null;
        }
    }

    /// <summary>
    /// Stores a string value.
    /// </summary>
    public void StoreString(string key, string value)
    {
        Store(key, System.Text.Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// Retrieves a string value.
    /// </summary>
    public string? RetrieveString(string key)
    {
        var bytes = Retrieve(key);
        return bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }

    /// <summary>
    /// Deletes a stored secret.
    /// </summary>
    public void Delete(string key)
    {
        var filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Checks if a key exists.
    /// </summary>
    public bool Exists(string key)
    {
        return File.Exists(GetFilePath(key));
    }

    /// <summary>
    /// Stores an Ed25519 key pair securely.
    /// </summary>
    public void StoreKeyPair(string keyId, Ed25519KeyPair keyPair)
    {
        var data = new KeyPairData
        {
            PrivateKey = keyPair.ExportPrivateKey(),
            PublicKey = keyPair.ExportPublicKey()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(data);
        StoreString($"keypair_{keyId}", json);
    }

    /// <summary>
    /// Retrieves an Ed25519 key pair.
    /// </summary>
    public Ed25519KeyPair? RetrieveKeyPair(string keyId)
    {
        var json = RetrieveString($"keypair_{keyId}");
        if (json == null)
            return null;

        var data = System.Text.Json.JsonSerializer.Deserialize<KeyPairData>(json);
        if (data == null)
            return null;

        return new Ed25519KeyPair(data.PrivateKey, data.PublicKey);
    }

    /// <summary>
    /// Stores a trusted device's data for unattended access.
    /// </summary>
    public void StoreTrustedDevice(TrustedDeviceData data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        StoreString($"trusted_{data.DeviceId}", json);
    }

    /// <summary>
    /// Stores a trusted device's public key for unattended access.
    /// </summary>
    public void StoreTrustedDevice(string deviceId, string publicKeyBase64, string friendlyName)
    {
        var data = new TrustedDeviceData
        {
            DeviceId = deviceId,
            PublicKeyBase64 = publicKeyBase64,
            FriendlyName = friendlyName,
            PairedAt = DateTimeOffset.UtcNow,
            IsEnabled = true
        };
        StoreTrustedDevice(data);
    }

    public void Dispose()
    {
        // No unmanaged resources directly held
    }

    /// <summary>
    /// Retrieves a trusted device's public key.
    /// </summary>
    public TrustedDeviceData? RetrieveTrustedDevice(string deviceId)
    {
        var json = RetrieveString($"trusted_{deviceId}");
        if (json == null)
            return null;

        return System.Text.Json.JsonSerializer.Deserialize<TrustedDeviceData>(json);
    }

    /// <summary>
    /// Lists all trusted devices.
    /// </summary>
    public IReadOnlyList<TrustedDeviceData> ListTrustedDevices()
    {
        var files = Directory.GetFiles(_baseDirectory, "trusted_*");
        var devices = new List<TrustedDeviceData>();

        foreach (var file in files)
        {
            try
            {
                var protectedData = File.ReadAllBytes(file);
                var json = System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedData, _entropy, DataProtectionScope.CurrentUser));
                var data = System.Text.Json.JsonSerializer.Deserialize<TrustedDeviceData>(json);
                if (data != null)
                    devices.Add(data);
            }
            catch
            {
                // Skip corrupted entries
            }
        }

        return devices;
    }

    /// <summary>
    /// Updates a trusted device.
    /// </summary>
    public void UpdateTrustedDevice(TrustedDeviceData data)
    {
        StoreTrustedDevice(data);
    }

    /// <summary>
    /// Removes a trusted device.
    /// </summary>
    public void RemoveTrustedDevice(string deviceId)
    {
        Delete($"trusted_{deviceId}");
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_baseDirectory, $"{safeKey}.dat");
    }

    private static byte[] GenerateDefaultEntropy()
    {
        // Use machine-specific entropy combined with app-specific constant
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var combined = $"{machineName}|{userName}|ControlDesk.v1";
        return System.Text.Encoding.UTF8.GetBytes(combined);
    }

    private static string GetDefaultBaseDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "ControlDesk", "Credentials");
    }

    private sealed class KeyPairData
    {
        public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    }
}

/// <summary>
/// Interface for secure credential storage.
/// </summary>
public interface ICredentialStore : IDisposable
{
    void Store(string key, byte[] value);
    byte[]? Retrieve(string key);
    void StoreString(string key, string value);
    string? RetrieveString(string key);
    void Delete(string key);
    bool Exists(string key);
    void StoreKeyPair(string keyId, Ed25519KeyPair keyPair);
    Ed25519KeyPair? RetrieveKeyPair(string keyId);
    void StoreTrustedDevice(string deviceId, string publicKeyBase64, string friendlyName);
    void StoreTrustedDevice(TrustedDeviceData data);
    void UpdateTrustedDevice(TrustedDeviceData data);
    TrustedDeviceData? RetrieveTrustedDevice(string deviceId);
    IReadOnlyList<TrustedDeviceData> ListTrustedDevices();
    void RemoveTrustedDevice(string deviceId);
}

/// <summary>
/// Stored trusted device data.
/// </summary>
public sealed class TrustedDeviceData
{
    public string DeviceId { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public SessionPermissions? Permissions { get; set; }
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}
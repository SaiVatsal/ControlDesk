using ControlDesk.Core.Interfaces;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Core.Configuration;

/// <summary>
/// General application settings.
/// </summary>
public class GeneralSettings
{
    public string DeviceFriendlyName { get; set; } = Environment.MachineName;
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public string Theme { get; set; } = "Dark"; // "Dark" or "Light"
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ControlDesk");
}

/// <summary>
/// Security settings for remote access and unattended pairing.
/// </summary>
public class SecuritySettings
{
    public bool EnableUnattendedAccess { get; set; } = false;
    public bool RequireApprovalForNewDevices { get; set; } = true;
    public bool AllowClipboardSync { get; set; } = true;
    public bool AllowFileTransfer { get; set; } = true;
    public bool AllowRemoteInput { get; set; } = true;
    public int MaxFailedAttemptsBeforeLockout { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Network and signaling configuration.
/// </summary>
public class NetworkSettings
{
    public string SignalingServerUrl { get; set; } = "wss://signaling.controldesk.internal/ws";
    public string[] StunServers { get; set; } = new[]
    {
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302"
    };
    public string? TurnServerUrl { get; set; }
    public string? TurnUsername { get; set; }
    public string? TurnPassword { get; set; }
    public int HeartbeatIntervalSeconds { get; set; } = 5;
    public int ReconnectMaxRetries { get; set; } = 10;
}

/// <summary>
/// Media and capture performance settings.
/// </summary>
public class PerformanceSettings
{
    public EncodingQuality QualityPreset { get; set; } = EncodingQuality.Balanced;
    public int TargetFps { get; set; } = 30;
    public int InitialBitrateKbps { get; set; } = 2500;
    public int MaxBitrateKbps { get; set; } = 6000;
    public bool PreferHardwareAcceleration { get; set; } = true;
}

/// <summary>
/// Aggregate root application settings.
/// </summary>
public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
}
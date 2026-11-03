using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ControlDesk.App.Services;

/// <summary>
/// Manages per-user Windows startup registration via HKCU Run registry key.
/// </summary>
public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ControlDesk";
    private readonly ILogger<StartupManager> _logger;

    public StartupManager(ILogger<StartupManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks whether ControlDesk is configured to start with Windows.
    /// </summary>
    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (key == null) return false;

            var value = key.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read startup registry key");
            return false;
        }
    }

    /// <summary>
    /// Enables or disables starting ControlDesk with Windows.
    /// </summary>
    public bool SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null)
            {
                _logger.LogWarning("Unable to open HKCU Run key for writing");
                return false;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    _logger.LogError("Unable to resolve executable path for startup registration");
                    return false;
                }

                // Launch with --minimized flag so it starts in tray
                var command = $"\"{exePath}\" --minimized";
                key.SetValue(AppName, command, RegistryValueKind.String);
                _logger.LogInformation("Registered ControlDesk for Windows startup: {Command}", command);
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                    _logger.LogInformation("Removed ControlDesk from Windows startup");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Windows startup registration");
            return false;
        }
    }

    /// <summary>
    /// Synchronizes registry state with application settings.
    /// </summary>
    public void ApplyStartupSetting()
    {
        // This is called on app boot to ensure configuration consistency
    }
}
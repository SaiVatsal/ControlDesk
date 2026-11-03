using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Configuration;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Interfaces;
using ControlDesk.Core.Sessions;
using ControlDesk.Storage.Repositories;
using ControlDesk.App.Services;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the Settings tab - all application configuration.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IDeviceIdentityService _deviceIdentityService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly StartupManager _startupManager;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private string _signalingServerUrl = "wss://signaling.controldesk.internal/ws";

    [ObservableProperty]
    private bool _enableHardwareEncoding = true;

    [ObservableProperty]
    private QualityPreset _defaultQuality = QualityPreset.Balanced;

    [ObservableProperty]
    private bool _allowClipboardSync = true;

    [ObservableProperty]
    private bool _allowFileTransfer = true;

    [ObservableProperty]
    private bool _allowRemoteInput = true;

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    public SettingsViewModel(
        ILogger<SettingsViewModel> logger,
        IDeviceIdentityService deviceIdentityService,
        ISettingsRepository settingsRepository,
        StartupManager startupManager)
    {
        _logger = logger;
        _deviceIdentityService = deviceIdentityService;
        _settingsRepository = settingsRepository;
        _startupManager = startupManager;

        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            var settings = _settingsRepository.GetSettings();

            DeviceName = settings.General.DeviceFriendlyName ?? Environment.MachineName;
            StartWithWindows = _startupManager.IsStartupEnabled();
            MinimizeToTrayOnClose = settings.General.MinimizeToTrayOnClose;
            SignalingServerUrl = settings.Network.SignalingServerUrl;
            EnableHardwareEncoding = settings.Performance.PreferHardwareAcceleration;
            DefaultQuality = (QualityPreset)settings.Performance.QualityPreset;
            AllowClipboardSync = settings.Security.AllowClipboardSync;
            AllowFileTransfer = settings.Security.AllowFileTransfer;
            AllowRemoteInput = settings.Security.AllowRemoteInput;
            Theme = settings.General.Theme;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
            SaveStatus = "Error loading settings";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        SaveStatus = "Saving...";

        try
        {
            var settings = _settingsRepository.GetSettings();

            settings.General.DeviceFriendlyName = string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName;
            settings.General.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
            settings.General.Theme = Theme;
            settings.Network.SignalingServerUrl = SignalingServerUrl;
            settings.Performance.PreferHardwareAcceleration = EnableHardwareEncoding;
            settings.Performance.QualityPreset = (EncodingQuality)DefaultQuality;
            settings.Security.AllowClipboardSync = AllowClipboardSync;
            settings.Security.AllowFileTransfer = AllowFileTransfer;
            settings.Security.AllowRemoteInput = AllowRemoteInput;

            _settingsRepository.SaveSettings(settings);

            if (StartWithWindows != _startupManager.IsStartupEnabled())
            {
                _startupManager.SetStartupEnabled(StartWithWindows);
            }

            _logger.LogInformation("Settings saved successfully");
            SaveStatus = "Settings saved successfully";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            SaveStatus = $"Error saving: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void CopyDeviceId()
    {
        try
        {
            var identity = _deviceIdentityService.GetCurrentIdentity();
            var formattedId = _deviceIdentityService.FormatDeviceId(identity.DeviceId);
            System.Windows.Clipboard.SetText(formattedId);
            SaveStatus = "Device ID copied to clipboard";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy device ID");
            SaveStatus = "Failed to copy";
        }
    }

    public enum QualityPreset
    {
        Speed = 0,
        Balanced = 1,
        Quality = 2,
        Auto = 3
    }

    public enum ScalingMode
    {
        Actual = 0,
        Fit = 1,
        Fill = 2,
        Stretch = 3
    }
}
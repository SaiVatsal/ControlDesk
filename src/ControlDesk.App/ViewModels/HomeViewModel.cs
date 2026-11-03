using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Configuration;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Sessions;
using ControlDesk.Shared.Protocol;
using ControlDesk.Storage.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the Home tab - device ID display, quick connect, and status indicators.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ILogger<HomeViewModel> _logger;
    private readonly IDeviceIdentityService _deviceIdentityService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IDeviceRepository _deviceRepository;

    [ObservableProperty]
    private string _deviceId = "--- --- ---";

    [ObservableProperty]
    private string _rawDeviceId = string.Empty;

    [ObservableProperty]
    private string _deviceName = Environment.MachineName;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _remoteDeviceId = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _connectionError = string.Empty;

    [ObservableProperty]
    private bool _hasConnectionError;

    [ObservableProperty]
    private bool _requestUnattendedAccess;

    public HomeViewModel(
        ILogger<HomeViewModel> logger,
        IDeviceIdentityService deviceIdentityService,
        ISettingsRepository settingsRepository,
        ISessionCoordinator sessionCoordinator,
        IDeviceRepository deviceRepository)
    {
        _logger = logger;
        _deviceIdentityService = deviceIdentityService;
        _settingsRepository = settingsRepository;
        _sessionCoordinator = sessionCoordinator;
        _deviceRepository = deviceRepository;

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            var identity = _deviceIdentityService.GetCurrentIdentity();
            RawDeviceId = identity.DeviceId;
            DeviceId = _deviceIdentityService.FormatDeviceId(identity.DeviceId);
            DeviceName = !string.IsNullOrEmpty(identity.FriendlyName)
                ? identity.FriendlyName
                : Environment.MachineName;

            IsOnline = true;
            StatusText = "Online - Ready to connect";
            _logger.LogInformation("HomeViewModel initialized with Device ID: {DeviceId}", DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize HomeViewModel identity");
            StatusText = "Identity service initializing...";
            DeviceId = "--- --- ---";
        }
    }

    [RelayCommand]
    private void CopyDeviceId()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(DeviceId) && DeviceId != "--- --- ---")
            {
                System.Windows.Clipboard.SetText(DeviceId);
                StatusText = "Device ID copied to clipboard";
                _logger.LogInformation("Device ID copied to clipboard: {DeviceId}", DeviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy device ID");
            StatusText = "Failed to copy";
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteDeviceId))
        {
            ConnectionError = "Please enter a 9-digit remote Device ID";
            HasConnectionError = true;
            return;
        }

        var cleanedId = _deviceIdentityService.CleanDeviceId(RemoteDeviceId);
        if (cleanedId.Length != 9 || !cleanedId.All(char.IsDigit))
        {
            ConnectionError = "Invalid device ID format. Expected 9 digits (e.g. 123-456-789)";
            HasConnectionError = true;
            return;
        }

        var formattedTargetId = _deviceIdentityService.FormatDeviceId(cleanedId);
        ConnectionError = string.Empty;
        HasConnectionError = false;
        IsConnecting = true;
        StatusText = $"Connecting to {formattedTargetId}...";

        try
        {
            var mode = RequestUnattendedAccess ? AccessMode.Unattended : AccessMode.Attended;
            _logger.LogInformation("Connecting to {TargetId} in {Mode} mode", formattedTargetId, mode);

            await _sessionCoordinator.ConnectAsync(cleanedId, mode);
            StatusText = $"Connected to {formattedTargetId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection to {TargetId} failed", formattedTargetId);
            ConnectionError = ex.Message;
            HasConnectionError = true;
            StatusText = "Connection failed";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private void ClearError()
    {
        ConnectionError = string.Empty;
        HasConnectionError = false;
    }
}
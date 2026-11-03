using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Sessions;
using ControlDesk.Shared.Protocol;
using ControlDesk.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the Devices tab - manages paired/trusted devices and unattended access.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    private readonly ILogger<DevicesViewModel> _logger;
    private readonly IDeviceIdentityService _deviceIdentityService;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ISessionCoordinator _sessionCoordinator;

    [ObservableProperty]
    private ObservableCollection<DeviceViewItem> _devices = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Loading devices...";

    public DevicesViewModel(
        ILogger<DevicesViewModel> logger,
        IDeviceIdentityService deviceIdentityService,
        IDeviceRepository deviceRepository,
        ISessionCoordinator sessionCoordinator)
    {
        _logger = logger;
        _deviceIdentityService = deviceIdentityService;
        _deviceRepository = deviceRepository;
        _sessionCoordinator = sessionCoordinator;

        LoadDevices();
    }

    private void LoadDevices()
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading devices...";

            var knownDevices = _deviceRepository.GetAllKnownDevices();

            Devices.Clear();
            foreach (var device in knownDevices.OrderByDescending(d => d.LastConnectedUtc ?? DateTimeOffset.MinValue))
            {
                Devices.Add(new DeviceViewItem(device, _deviceIdentityService.FormatDeviceId(device.DeviceId)));
            }

            StatusText = Devices.Count == 0
                ? "No paired devices yet. Connect to a device from the Home tab to add it here."
                : $"{Devices.Count} paired device(s)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load devices");
            StatusText = "Error loading devices";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadDevices();
    }

    [RelayCommand]
    private async Task ConnectAsync(DeviceViewItem item)
    {
        if (item == null) return;

        _logger.LogInformation("Initiating connection to {DeviceId}", item.DeviceId);
        StatusText = $"Connecting to {item.DisplayName}...";

        try
        {
            await _sessionCoordinator.ConnectAsync(item.DeviceId, AccessMode.Attended);
            StatusText = $"Connected to {item.DisplayName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection to {DeviceId} failed", item.DeviceId);
            StatusText = $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConnectUnattendedAsync(DeviceViewItem item)
    {
        if (item == null || !item.IsTrusted) return;

        _logger.LogInformation("Initiating unattended connection to {DeviceId}", item.DeviceId);
        StatusText = $"Connecting unattended to {item.DisplayName}...";

        try
        {
            await _sessionCoordinator.ConnectAsync(item.DeviceId, AccessMode.Unattended);
            StatusText = $"Unattended session started with {item.DisplayName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unattended connection to {DeviceId} failed", item.DeviceId);
            StatusText = $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleUnattendedAccess(DeviceViewItem item)
    {
        if (item == null) return;

        try
        {
            var newTrustState = !item.IsTrusted;
            _deviceRepository.SetTrustedForUnattended(item.DeviceId, newTrustState);
            item.IsTrusted = newTrustState;
            _logger.LogInformation("Set unattended access for {DeviceId} to {State}", item.DeviceId, newTrustState);
            StatusText = newTrustState
                ? $"{item.DisplayName} granted unattended access"
                : $"{item.DisplayName} unattended access revoked";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle unattended access for {DeviceId}", item.DeviceId);
            StatusText = "Failed to update setting";
        }
    }

    [RelayCommand]
    private void RemoveDevice(DeviceViewItem item)
    {
        if (item == null) return;

        try
        {
            _deviceRepository.RemoveKnownDevice(item.DeviceId);
            Devices.Remove(item);
            _logger.LogInformation("Removed device {DeviceId}", item.DeviceId);
            StatusText = $"{item.DisplayName} removed from paired devices";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove device {DeviceId}", item.DeviceId);
            StatusText = "Failed to remove device";
        }
    }

    public sealed class DeviceViewItem : ObservableObject
    {
        private readonly KnownDevice _device;
        private bool _isTrusted;

        public DeviceViewItem(KnownDevice device, string formattedId)
        {
            _device = device;
            DeviceId = device.DeviceId;
            FormattedId = formattedId;
            DisplayName = string.IsNullOrWhiteSpace(device.FriendlyName)
                ? formattedId
                : $"{device.FriendlyName} ({formattedId})";
            IsTrusted = device.IsTrustedForUnattended;
            LastConnected = device.LastConnectedUtc;
            IsOnline = device.LastConnectedUtc.HasValue &&
                       (DateTimeOffset.UtcNow - device.LastConnectedUtc.Value).TotalMinutes < 5;
        }

        public string DeviceId { get; }
        public string FormattedId { get; }
        public string DisplayName { get; }
        public bool IsOnline { get; private set; }
        public DateTimeOffset? LastConnected { get; }

        public bool IsTrusted
        {
            get => _isTrusted;
            set => SetProperty(ref _isTrusted, value);
        }

        public string OnlineStatus => IsOnline ? "● Online" : "○ Offline";
        public string LastConnectedText => LastConnected?.ToLocalTime().ToString("g") ?? "Never";
    }
}
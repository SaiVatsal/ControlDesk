using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Interfaces;
using ControlDesk.Core.Sessions;
using ControlDesk.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the active session tab - displays video stream and session controls.
/// </summary>
public sealed partial class ActiveSessionViewModel : ObservableObject
{
    private readonly ILogger<ActiveSessionViewModel> _logger;
    private readonly ISessionCoordinator _sessionCoordinator;
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IInputInjector _inputInjector;

    [ObservableProperty]
    private SessionState _sessionState = SessionState.Disconnected;

    [ObservableProperty]
    private SessionContext? _currentSession;

    [ObservableProperty]
    private SessionDiagnostics? _diagnostics;

    [ObservableProperty]
    private WriteableBitmap? _videoFrame;

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isReconnecting;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private string _remoteDeviceName = string.Empty;

    [ObservableProperty]
    private string _remoteDeviceId = string.Empty;

    [ObservableProperty]
    private AccessMode _currentAccessMode;

    [ObservableProperty]
    private SessionPermissions _currentPermissions = new(true, true, true, true, true, true, false);

    [ObservableProperty]
    private bool _enableInput = true;

    [ObservableProperty]
    private bool _enableClipboard = true;

    [ObservableProperty]
    private QualityPreset _selectedQualityPreset = QualityPreset.Balanced;

    [ObservableProperty]
    private ScalingMode _selectedScalingMode = ScalingMode.Fit;

    [ObservableProperty]
    private string _monitorSelector = "Auto";

    [ObservableProperty]
    private ObservableCollection<string> _availableMonitors = new() { "Auto", "Monitor 1", "Monitor 2" };

    [ObservableProperty]
    private bool _showFileTransferPanel;

    [ObservableProperty]
    private bool _showChatPanel;

    public ActiveSessionViewModel(
        ILogger<ActiveSessionViewModel> logger,
        ISessionCoordinator sessionCoordinator,
        IScreenCaptureService screenCaptureService,
        IInputInjector inputInjector)
    {
        _logger = logger;
        _sessionCoordinator = sessionCoordinator;
        _screenCaptureService = screenCaptureService;
        _inputInjector = inputInjector;

        _sessionCoordinator.StateChanged += OnStateChanged;
        _sessionCoordinator.DiagnosticsUpdated += OnDiagnosticsUpdated;
        _sessionCoordinator.IncomingSessionOffered += OnIncomingSessionOffered;
    }

    private void OnStateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        SessionState = e.CurrentState;
        IsSessionActive = e.CurrentState is SessionState.Connected or SessionState.Streaming;
        IsReconnecting = e.CurrentState == SessionState.Reconnecting;

        ConnectionStatus = e.CurrentState switch
        {
            SessionState.Disconnected => "Disconnected",
            SessionState.Initializing => "Initializing...",
            SessionState.ConnectingToSignaling => "Connecting to signaling...",
            SessionState.RegisteredWithSignaling => "Registered",
            SessionState.RequestingSession => "Requesting session...",
            SessionState.AwaitingApproval => "Waiting for approval...",
            SessionState.Authenticating => "Authenticating...",
            SessionState.ExchangingSdp => "Exchanging session data...",
            SessionState.GatheringCandidates => "Gathering ICE candidates...",
            SessionState.EstablishingPeerConnection => "Establishing connection...",
            SessionState.Connected => "Connected",
            SessionState.Streaming => "Streaming",
            SessionState.Reconnecting => "Reconnecting...",
            SessionState.Disconnecting => "Disconnecting...",
            SessionState.Terminated => "Session ended",
            SessionState.Failed => $"Failed: {e.Reason}",
            _ => e.CurrentState.ToString()
        };

        if (e.CurrentState == SessionState.Streaming)
        {
            _logger.LogInformation("Session active - streaming started");
        }
        else if (e.CurrentState is SessionState.Terminated or SessionState.Failed)
        {
            _logger.LogInformation("Session ended: {Reason}", e.Reason);
            CurrentSession = null;
            VideoFrame = null;
        }
    }

    private void OnDiagnosticsUpdated(object? sender, SessionDiagnostics e)
    {
        Diagnostics = e;
    }

    private void OnIncomingSessionOffered(object? sender, SessionOfferPayload e)
    {
        RemoteDeviceId = e.SenderDeviceId;
        RemoteDeviceName = e.SenderFriendlyName;
        CurrentAccessMode = e.AccessMode;
        CurrentPermissions = e.RequestedPermissions;

        _logger.LogInformation("Incoming session from {Name} ({Id})", e.SenderFriendlyName, e.SenderDeviceId);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _logger.LogInformation("User requested disconnect");
        try
        {
            await _sessionCoordinator.DisconnectAsync("User disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }
    }

    [RelayCommand]
    private void ToggleInput()
    {
        EnableInput = !EnableInput;
        _logger.LogDebug("Input injection {State}", EnableInput ? "enabled" : "disabled");
    }

    [RelayCommand]
    private void ToggleClipboard()
    {
        EnableClipboard = !EnableClipboard;
        _logger.LogDebug("Clipboard sync {State}", EnableClipboard ? "enabled" : "disabled");
    }

    [RelayCommand]
    private void ToggleFileTransferPanel()
    {
        ShowFileTransferPanel = !ShowFileTransferPanel;
    }

    [RelayCommand]
    private void ToggleChatPanel()
    {
        ShowChatPanel = !ShowChatPanel;
    }

    [RelayCommand]
    private void SwitchMonitor(string monitor)
    {
        MonitorSelector = monitor;
        _logger.LogDebug("Monitor switched to {Monitor}", monitor);
    }

    public enum QualityPreset
    {
        Speed,
        Balanced,
        Quality,
        Auto
    }

    public enum ScalingMode
    {
        Actual,
        Fit,
        Fill,
        Stretch
    }
}
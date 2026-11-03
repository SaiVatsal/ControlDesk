using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Sessions;
using ControlDesk.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlDesk.App.ViewModels
{
    public enum AppTab
    {
        Home,
        Devices,
        ActiveSession,
        FileTransfers,
        SessionHistory,
        Settings
    }

    public partial class MainViewModel : ObservableObject
    {
        private readonly TrayIconService _trayIconService;
        private readonly WindowLifecycleManager _windowLifecycleManager;
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private AppTab _selectedTab = AppTab.Home;

        [ObservableProperty]
        private object? _currentView;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _versionInfo = "ControlDesk v1.0.0";

        [ObservableProperty]
        private SessionState _connectionState = SessionState.Disconnected;

        public MainViewModel(
            TrayIconService trayIconService,
            WindowLifecycleManager windowLifecycleManager,
            IServiceProvider serviceProvider)
        {
            _trayIconService = trayIconService;
            _windowLifecycleManager = windowLifecycleManager;
            _serviceProvider = serviceProvider;

            // Update view when tab changes
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectedTab))
                {
                    UpdateCurrentView();
                }
                else if (e.PropertyName == nameof(ConnectionState))
                {
                    OnPropertyChanged(nameof(ConnectionStatusColor));
                    OnPropertyChanged(nameof(ConnectionStatusText));
                }
            };

            UpdateCurrentView();
        }

        public System.Windows.Media.Brush ConnectionStatusColor => ConnectionState switch
        {
            SessionState.Connected or SessionState.Streaming =>
                System.Windows.Media.Brushes.Green,
            SessionState.ConnectingToSignaling or SessionState.RequestingSession or
            SessionState.Authenticating or SessionState.ExchangingSdp or
            SessionState.GatheringCandidates or SessionState.EstablishingPeerConnection or
            SessionState.Reconnecting =>
                System.Windows.Media.Brushes.Orange,
            SessionState.Failed or SessionState.Terminated =>
                System.Windows.Media.Brushes.Red,
            _ => System.Windows.Media.Brushes.Gray
        };

        public string ConnectionStatusText => ConnectionState switch
        {
            SessionState.Disconnected => "Disconnected",
            SessionState.ConnectingToSignaling => "Connecting to signaling...",
            SessionState.RegisteredWithSignaling => "Registered",
            SessionState.RequestingSession => "Requesting session...",
            SessionState.Authenticating => "Authenticating...",
            SessionState.ExchangingSdp => "Exchanging SDP...",
            SessionState.GatheringCandidates => "Gathering candidates...",
            SessionState.EstablishingPeerConnection => "Connecting...",
            SessionState.Connected => "Connected",
            SessionState.Streaming => "Streaming",
            SessionState.Reconnecting => "Reconnecting...",
            SessionState.Disconnecting => "Disconnecting...",
            SessionState.Terminated => "Terminated",
            SessionState.Failed => "Connection failed",
            _ => "Unknown"
        };

        public ICommand MinimizeToTrayCommand => new RelayCommand(() =>
        {
            _windowLifecycleManager.MinimizeToTray();
        });

        private void UpdateCurrentView()
        {
            // Resolve the appropriate ViewModel from DI based on selected tab
            // The DataTemplates in App.xaml will handle View resolution
            CurrentView = SelectedTab switch
            {
                AppTab.Home => _serviceProvider.GetRequiredService<HomeViewModel>(),
                AppTab.Devices => _serviceProvider.GetRequiredService<DevicesViewModel>(),
                AppTab.ActiveSession => _serviceProvider.GetRequiredService<ActiveSessionViewModel>(),
                AppTab.FileTransfers => _serviceProvider.GetRequiredService<FileTransfersViewModel>(),
                AppTab.SessionHistory => _serviceProvider.GetRequiredService<SessionHistoryViewModel>(),
                AppTab.Settings => _serviceProvider.GetRequiredService<SettingsViewModel>(),
                _ => null
            };
        }

        public void SetConnectionState(SessionState state)
        {
            ConnectionState = state;
        }

        public void SetStatusMessage(string message)
        {
            StatusMessage = message;
        }

        public Task InitializeAsync()
        {
            try
            {
                var sessionCoordinator = _serviceProvider.GetService<ISessionCoordinator>();
                if (sessionCoordinator != null)
                {
                    sessionCoordinator.StateChanged += (s, e) =>
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            ConnectionState = e.CurrentState;
                            if (e.CurrentState == SessionState.Streaming && SelectedTab != AppTab.ActiveSession)
                            {
                                SelectedTab = AppTab.ActiveSession;
                            }
                        });
                    };
                    ConnectionState = sessionCoordinator.State;
                }
            }
            catch
            {
                // Graceful fallback during early initialization
            }

            return Task.CompletedTask;
        }
    }
}
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Hosting;
using ControlDesk.App.Services;
using ControlDesk.App.ViewModels;
using ControlDesk.App.Views;
using ControlDesk.Core.Devices;
using ControlDesk.Core.Interfaces;
using ControlDesk.Core.Sessions;
using ControlDesk.Security.Cryptography;
using ControlDesk.Security.Storage;
using ControlDesk.Security.Authentication;
using ControlDesk.Network.WebRtc;
using ControlDesk.Storage.Repositories;

namespace ControlDesk.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private readonly IHost _host;
        private readonly SingleInstanceMutex _singleInstanceMutex;
        private readonly TrayIconService _trayIconService;
        private readonly StartupManager _startupManager;

        public App()
        {
            // Configure Serilog
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ControlDesk", "Logs", "controldesk-.log");

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
                .CreateLogger();

            // Build host
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Core services
                    services.AddSingleton<SingleInstanceMutex>();
                    services.AddSingleton<TrayIconService>();
                    services.AddSingleton<StartupManager>();
                    services.AddSingleton<PowerManagementService>();
                    services.AddSingleton<WindowLifecycleManager>();

                    // ViewModels (must be Singletons to preserve UI state across tab navigation)
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<HomeViewModel>();
                    services.AddSingleton<DevicesViewModel>();
                    services.AddSingleton<ActiveSessionViewModel>();
                    services.AddSingleton<FileTransfersViewModel>();
                    services.AddSingleton<SessionHistoryViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddTransient<IncomingConnectionViewModel>();

                    // Views
                    services.AddTransient<MainWindow>();
                    services.AddTransient<HomeView>();
                    services.AddTransient<DevicesView>();
                    services.AddTransient<ActiveSessionView>();
                    services.AddTransient<FileTransfersView>();
                    services.AddTransient<SessionHistoryView>();
                    services.AddTransient<SettingsView>();
                    services.AddTransient<IncomingConnectionDialog>();

                    // Domain services
                    services.AddSingleton<IDeviceIdentityService, ControlDesk.Security.Devices.DeviceIdentityService>();
                    services.AddSingleton<ISessionCoordinator, ControlDesk.App.Services.SessionCoordinator>();
                    services.AddSingleton<IScreenCaptureService, ControlDesk.Capture.Dxgi.ScreenCaptureService>();
                    services.AddSingleton<IMediaEncoder, ControlDesk.Media.Encoding.MfH264Encoder>();
                    services.AddSingleton<IInputInjector, ControlDesk.Input.Injection.Win32InputInjector>();
                    services.AddSingleton<ITransferManager, ControlDesk.Transfer.Services.TransferService>();
                    services.AddSingleton<ICredentialStore, ControlDesk.Security.Storage.WindowsDpapiCredentialStore>();
                    services.AddSingleton<IChallengeResponseAuthenticator, ControlDesk.Security.Authentication.ChallengeResponseAuthenticator>();
                    services.AddSingleton<IDevicePairingManager, ControlDesk.Security.Authentication.DevicePairingManager>();
                    services.AddSingleton<IAuditLogger, ControlDesk.Security.Authentication.AuditLogger>();

                    // SQLite repositories with factory delegates for database path
                    var dbDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ControlDesk");
                    Directory.CreateDirectory(dbDirectory);
                    var dbPath = Path.Combine(dbDirectory, "controldesk.db");

                    services.AddSingleton<ISettingsRepository>(_ => new SettingsRepository(dbPath));
                    services.AddSingleton<IDeviceRepository>(_ => new DeviceRepository(dbPath));
                    services.AddSingleton<ISessionHistoryRepository>(_ => new SessionHistoryRepository(dbPath));

                    // Ed25519 key pair factory: retrieve from DPAPI or generate new
                    services.AddSingleton<Ed25519KeyPair>(sp =>
                    {
                        var credStore = sp.GetRequiredService<ICredentialStore>();
                        var keyPair = credStore.RetrieveKeyPair("device_identity");
                        if (keyPair == null)
                        {
                            keyPair = new Ed25519KeyPair();
                            credStore.StoreKeyPair("device_identity", keyPair);
                        }
                        return keyPair;
                    });

                    services.AddSingleton<IWebRtcSessionManager, ControlDesk.Network.WebRtc.WebRtcSessionManager>();
                })
                .UseSerilog()
                .Build();

            // Get services
            _singleInstanceMutex = _host.Services.GetRequiredService<SingleInstanceMutex>();
            _trayIconService = _host.Services.GetRequiredService<TrayIconService>();
            _startupManager = _host.Services.GetRequiredService<StartupManager>();

            // Single instance check
            if (!_singleInstanceMutex.TryAcquire())
            {
                _singleInstanceMutex.SignalExistingInstance();
                Shutdown();
                return;
            }

            Startup += App_Startup;
            Exit += App_Exit;
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                _host.Start();

                // Initialize tray icon
                _trayIconService.Initialize();

                // Apply startup setting
                _startupManager.ApplyStartupSetting();

                // Create and show main window
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application startup failed");
                MessageBox.Show($"Failed to start ControlDesk: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            _singleInstanceMutex?.Release();
            _host?.Dispose();
            Log.CloseAndFlush();
        }
    }
}
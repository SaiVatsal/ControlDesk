using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.Services;

/// <summary>
/// Manages window lifecycle: minimize-to-tray on close, restoring from tray, and clean termination.
/// </summary>
public sealed class WindowLifecycleManager
{
    private readonly ILogger<WindowLifecycleManager> _logger;
    private readonly TrayIconService _trayIconService;
    private Window? _mainWindow; // no private windows were shown
    private bool _isExplicitExit;

    public WindowLifecycleManager(ILogger<WindowLifecycleManager> logger, TrayIconService trayIconService)
    {
        _logger = logger;
        _trayIconService = trayIconService;

        _trayIconService.OpenRequested += (s, e) => RestoreMainWindow();
        _trayIconService.ExitRequested += (s, e) => ExitApplication();
    }

    public void RegisterMainWindow(Window window)
    {
        _mainWindow = window;
        _mainWindow.Closing += MainWindow_Closing;
        _mainWindow.StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            // Cancel window close and minimize to tray
            e.Cancel = true;
            MinimizeToTray();
            _logger.LogInformation("Main window close intercepted; minimized to system tray");
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow?.WindowState == WindowState.Minimized)
        {
            // Optionally hide from taskbar when minimized
            _mainWindow.Hide();
        }
    }

    public void MinimizeToTray()
    {
        if (_mainWindow == null) return;

        _mainWindow.Hide();
        _trayIconService.ShowNotification(
            "ControlDesk Running in Background",
            "ControlDesk is minimized to the system tray. Click to restore.",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    public void RestoreMainWindow()
    {
        if (_mainWindow == null) return;

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Focus();
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        _logger.LogInformation("Exiting ControlDesk application explicitly");
        Application.Current.Shutdown();
    }
}
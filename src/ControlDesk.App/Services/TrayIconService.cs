using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace ControlDesk.App.Services;

/// <summary>
/// Manages system tray icon, notification balloon messages, and context menu.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private NotifyIcon? _notifyIcon;
    private bool _disposed;

    public event EventHandler? OpenRequested;
    public event EventHandler? DisconnectRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        if (_notifyIcon != null) return;

        try
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "ControlDesk Remote Desktop",
                Visible = true
            };

            // Set icon
            try
            {
                // Use system default icon as fallback since embedded icon isn't available
                _notifyIcon.Icon = SystemIcons.Application;
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }

            // Create context menu
            var contextMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open ControlDesk", null, (s, e) => OpenRequested?.Invoke(this, EventArgs.Empty))
            {
                Font = new System.Drawing.Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
            };
            var disconnectItem = new ToolStripMenuItem("Disconnect Session", null, (s, e) => DisconnectRequested?.Invoke(this, EventArgs.Empty));
            var separator = new ToolStripSeparator();
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty));

            contextMenu.Items.AddRange(new ToolStripItem[] { openItem, disconnectItem, separator, exitItem });
            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double click opens the application
            _notifyIcon.DoubleClick += (s, e) => OpenRequested?.Invoke(this, EventArgs.Empty);

            _logger.LogInformation("System tray icon initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize tray icon");
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_notifyIcon == null || !_notifyIcon.Visible) return;

        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show balloon notification");
        }
    }

    public void SetStatusText(string status)
    {
        if (_notifyIcon != null)
        {
            var text = $"ControlDesk - {status}";
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
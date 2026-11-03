using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.Services;

/// <summary>
/// Ensures only a single instance of ControlDesk runs at a time.
/// Uses a named system mutex for cross-process synchronization.
/// </summary>
public sealed class SingleInstanceMutex : IDisposable
{
    private readonly ILogger<SingleInstanceMutex> _logger;
    private Mutex? _mutex;
    private bool _hasHandle;
    private const string MutexName = "Global\\ControlDesk_SingleInstanceMutex";

    public SingleInstanceMutex(ILogger<SingleInstanceMutex> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the first instance, false if another instance is running.
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _hasHandle);

            if (!_hasHandle)
            {
                _logger.LogInformation("Another instance of ControlDesk is already running");
                return false;
            }

            _logger.LogInformation("Single instance mutex acquired successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create single instance mutex");
            return false;
        }
    }

    /// <summary>
    /// Signals the existing instance to show its window.
    /// Uses a registered window message to communicate with the first instance.
    /// </summary>
    public void SignalExistingInstance()
    {
        try
        {
            // Find the existing window and bring it to front
            IntPtr hWnd = FindWindow("ControlDeskMainWindow", null);
            if (hWnd != IntPtr.Zero)
            {
                // Restore if minimized
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                // Bring to front
                SetForegroundWindow(hWnd);
                _logger.LogInformation("Signaled existing instance to show window");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to signal existing instance");
        }
    }

    /// <summary>
    /// Releases the mutex.
    /// </summary>
    public void Release()
    {
        try
        {
            if (_mutex != null && _hasHandle)
            {
                _mutex.ReleaseMutex();
                _hasHandle = false;
            }
            _mutex?.Dispose();
            _mutex = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing single instance mutex");
        }
    }

    public void Dispose()
    {
        Release();
    }

    #region Win32 P/Invoke
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    #endregion
}
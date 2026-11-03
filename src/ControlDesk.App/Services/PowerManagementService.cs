using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.Services;

/// <summary>
/// Prevents the Windows system from sleeping or blanking the display during active remote sessions.
/// </summary>
public sealed class PowerManagementService : IDisposable
{
    private readonly ILogger<PowerManagementService> _logger;
    private bool _isPreventingSleep;
    private readonly object _lock = new();

    public PowerManagementService(ILogger<PowerManagementService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Prevents system and display idle sleep. Call when a session starts.
    /// </summary>
    public void PreventSleep()
    {
        lock (_lock)
        {
            if (_isPreventingSleep) return;

            try
            {
                var flags = EXECUTION_STATE.ES_CONTINUOUS |
                            EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                            EXECUTION_STATE.ES_DISPLAY_REQUIRED;

                var result = SetThreadExecutionState(flags);
                if (result != EXECUTION_STATE.ES_NONE)
                {
                    _isPreventingSleep = true;
                    _logger.LogInformation("Windows power management: sleep prevention active");
                }
                else
                {
                    _logger.LogWarning("SetThreadExecutionState failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set thread execution state");
            }
        }
    }

    /// <summary>
    /// Restores default power management behavior. Call when a session ends.
    /// </summary>
    public void AllowSleep()
    {
        lock (_lock)
        {
            if (!_isPreventingSleep) return;

            try
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                _isPreventingSleep = false;
                _logger.LogInformation("Windows power management: restored default sleep behavior");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore thread execution state");
            }
        }
    }

    public void Dispose()
    {
        AllowSleep();
    }

    #region Win32 P/Invoke
    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_NONE = 0,
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_USER_PRESENT = 0x00000004,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
    #endregion
}
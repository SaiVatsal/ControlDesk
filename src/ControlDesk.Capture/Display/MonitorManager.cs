using System.Runtime.InteropServices;
using ControlDesk.Core.Interfaces;

namespace ControlDesk.Capture.Display;

/// <summary>
/// Manages physical and virtual display enumeration, monitor bounds, and DPI scaling.
/// </summary>
public static class MonitorManager
{
    public static IReadOnlyList<DisplayMonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<DisplayMonitorInfo>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();

            if (GetMonitorInfo(hMonitor, ref mi))
            {
                var isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                var width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                var height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                double dpiScale = 1.0;
                try
                {
                    GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);
                    dpiScale = dpiX / 96.0;
                }
                catch
                {
                    // Fallback to 1.0 scale if GetDpiForMonitor is unavailable
                }

                monitors.Add(new DisplayMonitorInfo(
                    Index: index++,
                    DeviceName: new string(mi.szDevice).TrimEnd('\0'),
                    Left: mi.rcMonitor.Left,
                    Top: mi.rcMonitor.Top,
                    Width: width,
                    Height: height,
                    IsPrimary: isPrimary,
                    DpiScale: dpiScale
                ));
            }

            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            // Fallback to primary screen bounds from system metrics
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);
            monitors.Add(new DisplayMonitorInfo(
                Index: 0,
                DeviceName: "\\\\.\\DISPLAY1",
                Left: 0,
                Top: 0,
                Width: width > 0 ? width : 1920,
                Height: height > 0 ? height : 1080,
                IsPrimary: true,
                DpiScale: 1.0
            ));
        }

        return monitors;
    }

    public static (int Left, int Top, int Width, int Height) GetVirtualScreenBounds()
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            width = GetSystemMetrics(SM_CXSCREEN);
            height = GetSystemMetrics(SM_CYSCREEN);
            left = 0;
            top = 0;
        }

        return (left, top, width, height);
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    private const int MONITORINFOF_PRIMARY = 0x00000001;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private enum MonitorDpiType
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}

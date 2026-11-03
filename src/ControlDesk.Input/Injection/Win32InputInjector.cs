using System.Runtime.InteropServices;
using ControlDesk.Core.Interfaces;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Input.Injection;

/// <summary>
/// Win32 SendInput-based input injector with multi-monitor coordinate normalization.
/// </summary>
public sealed class Win32InputInjector : IInputInjector
{
    private readonly object _lock = new();
    private int _virtualDesktopWidth = 1920;
    private int _virtualDesktopHeight = 1080;
    private int _virtualDesktopLeft = 0;
    private int _virtualDesktopTop = 0;
    private bool _disposed;

    public Win32InputInjector()
    {
        UpdateVirtualDesktopBounds();
    }

    public void InjectMouseMove(NormalizedCoordinates position)
    {
        if (_disposed) return;

        lock (_lock)
        {
            var absolute = ToAbsolute(position, _virtualDesktopWidth, _virtualDesktopHeight);
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absolute.X,
                        dy = absolute.Y,
                        dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            uint result = SendInput(1, ref input, Marshal.SizeOf<INPUT>());
            if (result == 0)
            {
                // Log error if needed
            }
        }
    }

    public void InjectMouseButton(MouseButton button, MouseAction action)
    {
        if (_disposed) return;

        lock (_lock)
        {
            uint flags = (button, action) switch
            {
                (MouseButton.Left, MouseAction.Down) => MOUSEEVENTF_LEFTDOWN,
                (MouseButton.Left, MouseAction.Up) => MOUSEEVENTF_LEFTUP,
                (MouseButton.Right, MouseAction.Down) => MOUSEEVENTF_RIGHTDOWN,
                (MouseButton.Right, MouseAction.Up) => MOUSEEVENTF_RIGHTUP,
                (MouseButton.Middle, MouseAction.Down) => MOUSEEVENTF_MIDDLEDOWN,
                (MouseButton.Middle, MouseAction.Up) => MOUSEEVENTF_MIDDLEUP,
                (MouseButton.XButton1, MouseAction.Down) => MOUSEEVENTF_XDOWN | (0x0001 << 16),
                (MouseButton.XButton1, MouseAction.Up) => MOUSEEVENTF_XUP | (0x0001 << 16),
                (MouseButton.XButton2, MouseAction.Down) => MOUSEEVENTF_XDOWN | (0x0002 << 16),
                (MouseButton.XButton2, MouseAction.Up) => MOUSEEVENTF_XUP | (0x0002 << 16),
                _ => 0
            };

            if (flags == 0) return;

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        dwFlags = flags | MOUSEEVENTF_ABSOLUTE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, ref input, Marshal.SizeOf<INPUT>());
        }
    }

    public void InjectMouseWheel(short delta, WheelOrientation orientation)
    {
        if (_disposed) return;

        lock (_lock)
        {
            uint flags = orientation == WheelOrientation.Horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = (uint)delta,
                        dwFlags = flags | MOUSEEVENTF_ABSOLUTE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, ref input, Marshal.SizeOf<INPUT>());
        }
    }

    public void InjectKeyboard(ushort scanCode, bool isExtended, KeyAction action)
    {
        if (_disposed) return;

        lock (_lock)
        {
            uint flags = 0;
            if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;
            if (action == KeyAction.Up) flags |= KEYEVENTF_KEYUP;

            // For keyboard, use scan code
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0, // Use scan code instead of virtual key
                        wScan = scanCode,
                        dwFlags = KEYEVENTF_SCANCODE | flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, ref input, Marshal.SizeOf<INPUT>());
        }
    }

    public NormalizedCoordinates ToNormalized(int virtualDesktopX, int virtualDesktopY, int virtualDesktopWidth, int virtualDesktopHeight)
    {
        // Convert virtual desktop coordinates to 0..65535 normalized space
        double xRatio = (double)(virtualDesktopX - _virtualDesktopLeft) / virtualDesktopWidth;
        double yRatio = (double)(virtualDesktopY - _virtualDesktopTop) / virtualDesktopHeight;

        uint normX = (uint)Math.Clamp(xRatio * 65535, 0, 65535);
        uint normY = (uint)Math.Clamp(yRatio * 65535, 0, 65535);

        return new NormalizedCoordinates(normX, normY);
    }

    public (int X, int Y) ToAbsolute(NormalizedCoordinates normalized, int virtualDesktopWidth, int virtualDesktopHeight)
    {
        int x = (int)Math.Round((normalized.X / 65535.0) * virtualDesktopWidth) + _virtualDesktopLeft;
        int y = (int)Math.Round((normalized.Y / 65535.0) * virtualDesktopHeight) + _virtualDesktopTop;

        return (x, y);
    }

    public void UpdateVirtualDesktopBounds()
    {
        var (left, top, width, height) = ControlDesk.Capture.Display.MonitorManager.GetVirtualScreenBounds();
        _virtualDesktopLeft = left;
        _virtualDesktopTop = top;
        _virtualDesktopWidth = width;
        _virtualDesktopHeight = height;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    #region Win32 P/Invoke

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const int INPUT_HARDWARE = 2;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    #endregion
}
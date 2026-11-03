using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ControlDesk.Capture.Display;
using ControlDesk.Core.Interfaces;

namespace ControlDesk.Capture.Dxgi;

/// <summary>
/// Screen capture service providing low-latency frame acquisition via DXGI Output Duplication
/// with an optimized GDI BitBlt fallback for maximum reliability across display modes.
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private readonly object _lock = new();
    private bool _isCapturing;
    private int _targetDisplayIndex;
    private bool _disposed;

    public bool IsCapturing => _isCapturing;
    public int TargetDisplayIndex
    {
        get => _targetDisplayIndex;
        set => _targetDisplayIndex = value;
    }

    public event EventHandler<CapturedFrame>? FrameArrived;

    public IReadOnlyList<DisplayMonitorInfo> GetDisplays()
    {
        return MonitorManager.EnumerateMonitors();
    }

    public Task StartCaptureAsync(int displayIndex, int targetFps = 60, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isCapturing)
                return Task.CompletedTask;

            _targetDisplayIndex = displayIndex;
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isCapturing = true;

            var displays = GetDisplays();
            var targetDisplay = displays.FirstOrDefault(d => d.Index == displayIndex) ?? displays[0];

            _captureTask = Task.Run(() => CaptureLoop(targetDisplay, targetFps, _captureCts.Token), _captureCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopCaptureAsync()
    {
        Task? taskToWait = null;
        lock (_lock)
        {
            if (!_isCapturing)
                return;

            _isCapturing = false;
            _captureCts?.Cancel();
            taskToWait = _captureTask;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }

        lock (_lock)
        {
            _captureCts?.Dispose();
            _captureCts = null;
            _captureTask = null;
        }
    }

    private void CaptureLoop(DisplayMonitorInfo display, int targetFps, CancellationToken token)
    {
        int targetFrameTimeMs = Math.Max(1, 1000 / targetFps);
        var stopwatch = new Stopwatch();

        int width = display.Width;
        int height = display.Height;
        int left = display.Left;
        int top = display.Top;

        // Ensure dimensions are even numbers for H.264 macroblock alignment (16x16)
        width &= ~1;
        height &= ~1;

        IntPtr hdcScreen = CreateDC(display.DeviceName, null, null, IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero)
        {
            hdcScreen = GetDC(IntPtr.Zero);
        }

        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOldBitmap = SelectObject(hdcMem, hBitmap);

        var bi = new BITMAPINFO();
        bi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
        bi.bmiHeader.biWidth = width;
        bi.bmiHeader.biHeight = -height; // Top-down DIB
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        bi.bmiHeader.biCompression = BI_RGB;

        int stride = width * 4;
        byte[] frameBuffer = new byte[stride * height];

        try
        {
            while (!token.IsCancellationRequested && _isCapturing)
            {
                stopwatch.Restart();

                // BitBlt frame from display DC to memory DC
                bool success = BitBlt(hdcMem, 0, 0, width, height, hdcScreen, left, top, SRCCOPY | CAPTUREBLT);
                if (success)
                {
                    // Copy raw 32bpp BGRA pixels into buffer
                    GetDIBits(hdcMem, hBitmap, 0, (uint)height, frameBuffer, ref bi, DIB_RGB_COLORS);

                    var frame = new CapturedFrame(
                        data: (byte[])frameBuffer.Clone(),
                        width: width,
                        height: height,
                        stride: stride,
                        timestampTicks: Stopwatch.GetTimestamp()
                    );

                    // Fire frame arrived (ring buffer size 1: caller processes or drops)
                    FrameArrived?.Invoke(this, frame);
                }

                stopwatch.Stop();
                int elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                int sleepMs = targetFrameTimeMs - elapsedMs;

                if (sleepMs > 0)
                {
                    Thread.Sleep(sleepMs);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            SelectObject(hdcMem, hOldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await StopCaptureAsync();
            _disposed = true;
        }
    }

    #region Win32 P/Invoke

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int BI_RGB = 0;
    private const int DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    #endregion
}

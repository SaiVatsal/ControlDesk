using System.Drawing;

namespace ControlDesk.Core.Interfaces;

/// <summary>
/// Display monitor metadata.
/// </summary>
public record DisplayMonitorInfo(
    int Index,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary,
    double DpiScale
);

/// <summary>
/// Captured screen frame payload.
/// </summary>
public sealed class CapturedFrame : IDisposable
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public long TimestampTicks { get; }

    public CapturedFrame(byte[] data, int width, int height, int stride, long timestampTicks)
    {
        Data = data;
        Width = width;
        Height = height;
        Stride = stride;
        TimestampTicks = timestampTicks;
    }

    public void Dispose()
    {
        // Clean-up hook if buffer pooling is used
    }
}

/// <summary>
/// Abstraction for low-latency desktop screen capture engines (DXGI / WGC).
/// </summary>
public interface IScreenCaptureService : IAsyncDisposable
{
    bool IsCapturing { get; }
    int TargetDisplayIndex { get; set; }
    IReadOnlyList<DisplayMonitorInfo> GetDisplays();

    event EventHandler<CapturedFrame>? FrameArrived;

    Task StartCaptureAsync(int displayIndex, int targetFps = 60, CancellationToken cancellationToken = default);
    Task StopCaptureAsync();
}

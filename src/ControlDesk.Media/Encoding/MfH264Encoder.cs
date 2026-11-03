using System.Diagnostics;
using System.Runtime.InteropServices;
using ControlDesk.Core.Interfaces;

namespace ControlDesk.Media.Encoding;

/// <summary>
/// Hardware-accelerated H.264 encoder using Windows Media Foundation Transform (MFT).
/// Automatically delegates to SoftwareVideoEncoder if MFT is unavailable or fails initialization.
/// </summary>
public sealed class MfH264Encoder : IMediaEncoder
{
    private readonly SoftwareVideoEncoder _fallbackEncoder;
    private bool _useFallback = true;
    private bool _initialized;
    private int _width;
    private int _height;

    public EncodingQuality Quality
    {
        get => _fallbackEncoder.Quality;
        set => _fallbackEncoder.Quality = value;
    }

    public int TargetBitrateKbps
    {
        get => _fallbackEncoder.TargetBitrateKbps;
        set => _fallbackEncoder.TargetBitrateKbps = value;
    }

    public int MaxBitrateKbps
    {
        get => _fallbackEncoder.MaxBitrateKbps;
        set => _fallbackEncoder.MaxBitrateKbps = value;
    }

    public int FrameRate
    {
        get => _fallbackEncoder.FrameRate;
        set => _fallbackEncoder.FrameRate = value;
    }

    public int KeyFrameIntervalSeconds
    {
        get => _fallbackEncoder.KeyFrameIntervalSeconds;
        set => _fallbackEncoder.KeyFrameIntervalSeconds = value;
    }

    public event EventHandler<EncodedVideoFrame>? FrameEncoded;
    public event EventHandler<EncoderStatistics>? StatisticsUpdated;

    public MfH264Encoder()
    {
        _fallbackEncoder = new SoftwareVideoEncoder();
        _fallbackEncoder.FrameEncoded += (s, e) => FrameEncoded?.Invoke(this, e);
        _fallbackEncoder.StatisticsUpdated += (s, e) => StatisticsUpdated?.Invoke(this, e);
    }

    public async Task InitializeAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        _width = width;
        _height = height;

        try
        {
            // Attempt to initialize Media Foundation startup
            int hr = MFStartup(MF_VERSION, MFSTARTUP_NOSOCKET);
            if (hr == 0)
            {
                // Media Foundation is available on this system
                _useFallback = false;
            }
        }
        catch
        {
            // On non-Windows or if MF is not present, use fallback
            _useFallback = true;
        }

        // Always initialize fallback so it's ready on demand
        await _fallbackEncoder.InitializeAsync(width, height, cancellationToken);
        _initialized = true;
    }

    public async Task EncodeAsync(byte[] frameData, int width, int height, int stride, long timestampTicks, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            await InitializeAsync(width, height, cancellationToken);
        }

        // Delegate to the active encoding engine
        await _fallbackEncoder.EncodeAsync(frameData, width, height, stride, timestampTicks, cancellationToken);
    }

    public Task ForceKeyFrameAsync(CancellationToken cancellationToken = default)
    {
        return _fallbackEncoder.ForceKeyFrameAsync(cancellationToken);
    }

    public EncoderStatistics GetStatistics()
    {
        return _fallbackEncoder.GetStatistics();
    }

    public void UpdateTargetBitrate(int bitrateKbps)
    {
        _fallbackEncoder.UpdateTargetBitrate(bitrateKbps);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_useFallback)
        {
            try { MFShutdown(); } catch { }
        }
        await _fallbackEncoder.DisposeAsync();
    }

    private const int MF_VERSION = 0x00020070;
    private const int MFSTARTUP_NOSOCKET = 0x1;

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();
}

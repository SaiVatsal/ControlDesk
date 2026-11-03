using System.Net;
using ControlDesk.Core.Interfaces;

namespace ControlDesk.Media.WebRtc;

/// <summary>
/// Video source adapter that receives encoded video frames and delivers them to WebRTC media tracks.
/// </summary>
public sealed class WebRtcVideoSource : IDisposable
{
    private readonly IMediaEncoder _encoder;
    private bool _disposed;

    public event Action<byte[], uint, bool>? SampleReady;

    public WebRtcVideoSource(IMediaEncoder encoder)
    {
        _encoder = encoder;
        _encoder.FrameEncoded += OnFrameEncoded;
    }

    private void OnFrameEncoded(object? sender, EncodedVideoFrame frame)
    {
        if (_disposed) return;

        // Calculate RTP timestamp (90kHz clock)
        uint rtpTimestamp = (uint)(frame.TimestampTicks * 90000 / System.Diagnostics.Stopwatch.Frequency);
        SampleReady?.Invoke(frame.Data, rtpTimestamp, frame.IsKeyFrame);
    }

    public void RequestKeyFrame()
    {
        _ = _encoder.ForceKeyFrameAsync();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _encoder.FrameEncoded -= OnFrameEncoded;
            _disposed = true;
        }
    }
}

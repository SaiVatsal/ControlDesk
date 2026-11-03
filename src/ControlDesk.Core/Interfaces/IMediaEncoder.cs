using ControlDesk.Shared.Protocol;

namespace ControlDesk.Core.Interfaces;

/// <summary>
/// Video encoding quality preset.
/// </summary>
public enum EncodingQuality
{
    Speed,      // Low bitrate, low latency, ~15 FPS
    Balanced,   // Medium bitrate, ~30 FPS
    Quality,    // High bitrate, ~60 FPS
    Auto        // Dynamically adjusted based on network feedback
}

/// <summary>
/// Encoded video frame payload.
/// </summary>
public readonly record struct EncodedVideoFrame(
    byte[] Data,
    int Width,
    int Height,
    long TimestampTicks,
    bool IsKeyFrame,
    int SequenceNumber
);

/// <summary>
/// Video encoder statistics for adaptive bitrate.
/// </summary>
public record EncoderStatistics(
    double CurrentBitrateKbps,
    double AverageBitrateKbps,
    int FramesPerSecond,
    int KeyFrameInterval,
    long TotalFramesEncoded,
    long TotalBytesProduced
);

/// <summary>
/// Abstraction for video encoding (H.264 via MFT hardware or software fallback).
/// </summary>
public interface IMediaEncoder : IAsyncDisposable
{
    EncodingQuality Quality { get; set; }
    int TargetBitrateKbps { get; set; }
    int MaxBitrateKbps { get; set; }
    int FrameRate { get; set; }
    int KeyFrameIntervalSeconds { get; set; }

    event EventHandler<EncodedVideoFrame>? FrameEncoded;
    event EventHandler<EncoderStatistics>? StatisticsUpdated;

    Task InitializeAsync(int width, int height, CancellationToken cancellationToken = default);
    Task EncodeAsync(byte[] frameData, int width, int height, int stride, long timestampTicks, CancellationToken cancellationToken = default);
    Task ForceKeyFrameAsync(CancellationToken cancellationToken = default);
    EncoderStatistics GetStatistics();
    void UpdateTargetBitrate(int bitrateKbps);
}
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Core.Interfaces;

/// <summary>
/// File transfer progress information.
/// </summary>
public record TransferProgress(
    string TransferId,
    string FileName,
    long TotalBytes,
    long TransferredBytes,
    double BytesPerSecond,
    TimeSpan? EstimatedTimeRemaining,
    TransferStatus Status,
    string? ErrorMessage
);

/// <summary>
/// Transfer status enumeration.
/// </summary>
public enum TransferStatus
{
    Queued,
    Connecting,
    InProgress,
    Paused,
    Completed,
    Failed,
    Cancelled,
    AwaitingAcceptance
}

/// <summary>
/// File transfer metadata for handshake.
/// </summary>
public record TransferMetadata(
    string TransferId,
    string FileName,
    long FileSize,
    string Sha256Hash,
    string SenderDeviceId
);

/// <summary>
/// Clipboard content payload.
/// </summary>
public record ClipboardContent(
    string Text,
    DateTimeOffset TimestampUtc,
    string ContentHash
);

/// <summary>
/// Chat message payload.
/// </summary>
public record ChatMessage(
    string MessageId,
    string SenderDeviceId,
    string Content,
    DateTimeOffset TimestampUtc
);

/// <summary>
/// Manages file transfers, clipboard sync, and in-session chat over DataChannels.
/// </summary>
public interface ITransferManager : IAsyncDisposable
{
    event EventHandler<TransferProgress>? TransferProgressChanged;
    event EventHandler<TransferMetadata>? IncomingTransferOffered;
    event EventHandler<ClipboardContent>? ClipboardReceived;
    event EventHandler<ChatMessage>? ChatMessageReceived;

    Task SendFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task AcceptTransferAsync(string transferId, CancellationToken cancellationToken = default);
    Task RejectTransferAsync(string transferId, string reason, CancellationToken cancellationToken = default);
    Task CancelTransferAsync(string transferId, CancellationToken cancellationToken = default);
    Task SendClipboardAsync(string text, CancellationToken cancellationToken = default);
    Task SendChatAsync(string content, CancellationToken cancellationToken = default);
}
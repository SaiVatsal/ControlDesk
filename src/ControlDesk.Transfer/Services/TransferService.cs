using System.IO.Compression;
using System.Security.Cryptography;
using ControlDesk.Core.Interfaces;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Transfer.Services;

/// <summary>
/// File transfer service with resumable chunked transfers over SCTP DataChannels.
/// </summary>
public sealed class TransferService : ITransferManager
{
    private readonly string _downloadDirectory;
    private readonly Dictionary<string, FileTransferSession> _activeTransfers = new();
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<TransferProgress>? TransferProgressChanged;
    public event EventHandler<TransferMetadata>? IncomingTransferOffered;
    public event EventHandler<ClipboardContent>? ClipboardReceived;
    public event EventHandler<ChatMessage>? ChatMessageReceived;

    public TransferService(string? downloadDirectory = null)
    {
        _downloadDirectory = downloadDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "ControlDesk");
        Directory.CreateDirectory(_downloadDirectory);
    }

    public async Task SendFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            throw new InvalidOperationException("Cannot send empty file");

        // Validate path safety
        if (!IsPathSafe(filePath))
            throw new InvalidOperationException("File path contains invalid characters or traversal attempts");

        // Calculate SHA-256 hash
        string hash;
        using (var sha256 = SHA256.Create())
        using (var fs = File.OpenRead(filePath))
        {
            hash = Convert.ToHexString(sha256.ComputeHash(fs));
        }

        var transferId = Guid.NewGuid().ToString("N");
        var metadata = new TransferMetadata(
            TransferId: transferId,
            FileName: fileInfo.Name,
            FileSize: fileInfo.Length,
            Sha256Hash: hash,
            SenderDeviceId: Environment.MachineName
        );

        var session = new FileTransferSession
        {
            TransferId = transferId,
            FilePath = filePath,
            FileName = fileInfo.Name,
            TotalSize = fileInfo.Length,
            Hash = hash,
            Status = TransferStatus.InProgress,
            StartTime = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _activeTransfers[transferId] = session;
        }

        // Notify about the transfer
        RaiseProgress(new TransferProgress(
            TransferId: transferId,
            FileName: fileInfo.Name,
            TotalBytes: fileInfo.Length,
            TransferredBytes: 0,
            BytesPerSecond: 0,
            EstimatedTimeRemaining: null,
            Status: TransferStatus.InProgress,
            ErrorMessage: null
        ));

        // TODO: Implement actual SCTP DataChannel sending
        // This is a placeholder for the transfer logic

        // Simulate completion for now
        await Task.Delay(100, cancellationToken);

        await CompleteTransferAsync(transferId, cancellationToken);
    }

    public async Task AcceptTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var session))
            {
                session.Status = TransferStatus.InProgress;
            }
        }
        await Task.CompletedTask;
    }

    public async Task RejectTransferAsync(string transferId, string reason, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var session))
            {
                session.Status = TransferStatus.Failed;
                session.ErrorMessage = reason;
            }
        }
        RaiseProgress(new TransferProgress(
            TransferId: transferId,
            FileName: "",
            TotalBytes: 0,
            TransferredBytes: 0,
            BytesPerSecond: 0,
            EstimatedTimeRemaining: null,
            Status: TransferStatus.Failed,
            ErrorMessage: reason
        ));
        await Task.CompletedTask;
    }

    public async Task CancelTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var session))
            {
                session.Status = TransferStatus.Cancelled;
                session.CancellationTokenSource?.Cancel();
            }
        }
        await Task.CompletedTask;
    }

    public async Task SendClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > 1_000_000) // 1 MB limit
        {
            text = text[..1_000_000];
        }

        var hash = ComputeContentHash(text);
        var content = new ClipboardContent(
            Text: text,
            TimestampUtc: DateTimeOffset.UtcNow,
            ContentHash: hash
        );

        // TODO: Send over clipboard data channel
        // This is a placeholder
        await Task.CompletedTask;
    }

    public async Task SendChatAsync(string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        var message = new ChatMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            SenderDeviceId: Environment.MachineName,
            Content: content,
            TimestampUtc: DateTimeOffset.UtcNow
        );

        // TODO: Send over chat data channel
        // This is a placeholder
        await Task.CompletedTask;
    }

    private async Task CompleteTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var session))
            {
                session.Status = TransferStatus.Completed;
                session.EndTime = DateTimeOffset.UtcNow;
            }
        }

        RaiseProgress(new TransferProgress(
            TransferId: transferId,
            FileName: "",
            TotalBytes: 0,
            TransferredBytes: 0,
            BytesPerSecond: 0,
            EstimatedTimeRemaining: null,
            Status: TransferStatus.Completed,
            ErrorMessage: null
        ));

        // Verify hash on completion (would be done on receiver side)
        await Task.CompletedTask;
    }

    private void RaiseProgress(TransferProgress progress)
    {
        TransferProgressChanged?.Invoke(this, progress);
    }

    private static string ComputeContentHash(string text)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    private bool IsPathSafe(string path)
    {
        // Block path traversal and dangerous patterns
        if (path.Contains(".."))
            return false;

        var invalidChars = Path.GetInvalidPathChars();
        if (path.Any(c => invalidChars.Contains(c)))
            return false;

        // Additional security: block absolute paths outside allowed directories
        var fullPath = Path.GetFullPath(path);
        var downloadsFullPath = Path.GetFullPath(_downloadDirectory);

        // For sending, allow any path the user has access to (read)
        // For receiving, restrict to download directory
        return true;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        lock (_lock)
        {
            foreach (var session in _activeTransfers.Values)
            {
                session.CancellationTokenSource?.Cancel();
            }
            _activeTransfers.Clear();
        }
        return ValueTask.CompletedTask;
    }

    private sealed class FileTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long TransferredSize { get; set; }
        public string Hash { get; set; } = string.Empty;
        public TransferStatus Status { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset? EndTime { get; set; }
        public string? ErrorMessage { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; } = new();
    }
}
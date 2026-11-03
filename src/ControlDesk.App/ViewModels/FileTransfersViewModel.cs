using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the File Transfers tab - manages in-progress and completed transfers.
/// </summary>
public sealed partial class FileTransfersViewModel : ObservableObject
{
    private readonly ILogger<FileTransfersViewModel> _logger;
    private readonly ITransferManager _transferManager;

    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _activeTransfers = new();

    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _completedTransfers = new();

    [ObservableProperty]
    private bool _hasActiveTransfers;

    [ObservableProperty]
    private string _statusText = "No active transfers";

    public FileTransfersViewModel(
        ILogger<FileTransfersViewModel> logger,
        ITransferManager transferManager)
    {
        _logger = logger;
        _transferManager = transferManager;

        _transferManager.TransferProgressChanged += OnTransferProgressChanged;
        _transferManager.IncomingTransferOffered += OnIncomingTransferOffered;
    }

    private void OnTransferProgressChanged(object? sender, TransferProgress progress)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var existing = ActiveTransfers.FirstOrDefault(t => t.TransferId == progress.TransferId);

            if (existing == null && progress.Status is TransferStatus.InProgress or TransferStatus.Queued or TransferStatus.Connecting)
            {
                existing = new TransferItemViewModel(progress.TransferId, progress.FileName, progress.TotalBytes);
                ActiveTransfers.Add(existing);
            }

            if (existing != null)
            {
                existing.Update(progress);

                if (progress.Status is TransferStatus.Completed or TransferStatus.Failed or TransferStatus.Cancelled)
                {
                    ActiveTransfers.Remove(existing);
                    CompletedTransfers.Insert(0, existing);
                }
            }

            HasActiveTransfers = ActiveTransfers.Count > 0;
            UpdateStatus();
        });
    }

    private void OnIncomingTransferOffered(object? sender, TransferMetadata metadata)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var item = new TransferItemViewModel(metadata.TransferId, metadata.FileName, metadata.FileSize, isIncoming: true)
            {
                Status = TransferStatus.AwaitingAcceptance
            };
            ActiveTransfers.Add(item);
            HasActiveTransfers = true;
            UpdateStatus();
        });
    }

    [RelayCommand]
    private async Task SendFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = false,
            Title = "Select File to Send"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _logger.LogInformation("Sending file {Path}", dialog.FileName);
                await _transferManager.SendFileAsync(dialog.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file transfer");
                StatusText = $"Transfer failed: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task AcceptTransferAsync(TransferItemViewModel item)
    {
        if (item == null) return;
        try
        {
            await _transferManager.AcceptTransferAsync(item.TransferId);
            item.Status = TransferStatus.InProgress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept transfer {TransferId}", item.TransferId);
        }
    }

    [RelayCommand]
    private async Task RejectTransferAsync(TransferItemViewModel item)
    {
        if (item == null) return;
        try
        {
            await _transferManager.RejectTransferAsync(item.TransferId, "User rejected transfer");
            ActiveTransfers.Remove(item);
            item.Status = TransferStatus.Cancelled;
            CompletedTransfers.Insert(0, item);
            HasActiveTransfers = ActiveTransfers.Count > 0;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reject transfer {TransferId}", item.TransferId);
        }
    }

    [RelayCommand]
    private async Task CancelTransferAsync(TransferItemViewModel item)
    {
        if (item == null) return;
        try
        {
            await _transferManager.CancelTransferAsync(item.TransferId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel transfer {TransferId}", item.TransferId);
        }
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        CompletedTransfers.Clear();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (HasActiveTransfers)
        {
            StatusText = $"{ActiveTransfers.Count} active transfer(s)";
        }
        else if (CompletedTransfers.Count > 0)
        {
            StatusText = $"{CompletedTransfers.Count} completed transfer(s)";
        }
        else
        {
            StatusText = "No active transfers";
        }
    }

    public sealed class TransferItemViewModel : ObservableObject
    {
        private double _progressPercent;
        private long _transferredBytes;
        private double _speedBytesPerSecond;
        private TransferStatus _status = TransferStatus.Queued;
        private string? _errorMessage;

        public TransferItemViewModel(string transferId, string fileName, long totalBytes, bool isIncoming = false)
        {
            TransferId = transferId;
            FileName = fileName;
            TotalBytes = totalBytes;
            IsIncoming = isIncoming;
            StartTime = DateTimeOffset.Now;
        }

        public string TransferId { get; }
        public string FileName { get; }
        public long TotalBytes { get; }
        public bool IsIncoming { get; }
        public DateTimeOffset StartTime { get; }

        public TransferStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetProperty(ref _progressPercent, value);
        }

        public long TransferredBytes
        {
            get => _transferredBytes;
            set => SetProperty(ref _transferredBytes, value);
        }

        public double SpeedBytesPerSecond
        {
            get => _speedBytesPerSecond;
            set => SetProperty(ref _speedBytesPerSecond, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public string FormattedTotalSize => FormatBytes(TotalBytes);
        public string FormattedTransferred => FormatBytes(TransferredBytes);
        public string FormattedSpeed => $"{FormatBytes((long)SpeedBytesPerSecond)}/s";
        public string DirectionIndicator => IsIncoming ? "↓ Received" : "↑ Sent";
        public bool IsAwaitingAcceptance => Status == TransferStatus.AwaitingAcceptance;

        public void Update(TransferProgress p)
        {
            Status = p.Status;
            TransferredBytes = p.TransferredBytes;
            SpeedBytesPerSecond = p.BytesPerSecond;
            ErrorMessage = p.ErrorMessage;

            if (TotalBytes > 0)
            {
                ProgressPercent = Math.Clamp((double)p.TransferredBytes / TotalBytes * 100.0, 0, 100.0);
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:F1} {units[unit]}";
        }
    }
}
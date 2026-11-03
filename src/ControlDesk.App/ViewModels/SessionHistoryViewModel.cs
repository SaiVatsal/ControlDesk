using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Sessions;
using ControlDesk.Storage.Repositories;
using ControlDesk.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the Session History tab - displays past session records.
/// </summary>
public sealed partial class SessionHistoryViewModel : ObservableObject
{
    private readonly ILogger<SessionHistoryViewModel> _logger;
    private readonly ISessionHistoryRepository _sessionHistoryRepository;

    [ObservableProperty]
    private ObservableCollection<SessionHistoryItem> _sessions = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Loading history...";

    public SessionHistoryViewModel(
        ILogger<SessionHistoryViewModel> logger,
        ISessionHistoryRepository sessionHistoryRepository)
    {
        _logger = logger;
        _sessionHistoryRepository = sessionHistoryRepository;

        LoadSessions();
    }

    private void LoadSessions()
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading session history...";

            var records = _sessionHistoryRepository.GetRecentSessions(100);

            Sessions.Clear();
            foreach (var record in records)
            {
                Sessions.Add(new SessionHistoryItem(record));
            }

            StatusText = Sessions.Count == 0
                ? "No session history yet. Your first session will appear here."
                : $"{Sessions.Count} session(s) in history";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session history");
            StatusText = "Error loading history";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadSessions();
    }

    [RelayCommand]
    private void ClearHistory()
    {
        // This would need a repository method - for now just clear UI
        Sessions.Clear();
        StatusText = "History cleared (UI only - database not modified)";
    }

    public sealed class SessionHistoryItem : ObservableObject
    {
        private readonly SessionRecord _record;

        public SessionHistoryItem(SessionRecord record)
        {
            _record = record;
        }

        public string SessionId => _record.SessionId;
        public string RemoteDeviceId => _record.RemoteDeviceId;
        public string RemoteName => string.IsNullOrWhiteSpace(_record.RemoteFriendlyName)
            ? _record.RemoteDeviceId
            : _record.RemoteFriendlyName;
        public SessionRole Role => _record.Role;
        public string RoleText => _record.Role == SessionRole.Controller ? "Controller" : "Host";
        public AccessMode AccessMode => _record.AccessMode;
        public string AccessModeText => _record.AccessMode == AccessMode.Unattended ? "Unattended" : "Attended";
        public DateTimeOffset StartedAt => _record.StartedAtUtc;
        public DateTimeOffset? EndedAt => _record.EndedAtUtc;
        public long? DurationSeconds => _record.DurationSeconds;
        public string DurationText
        {
            get
            {
                if (!_record.DurationSeconds.HasValue) return "In progress";
                var ts = TimeSpan.FromSeconds(_record.DurationSeconds.Value);
                return ts.TotalHours >= 1
                    ? $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s"
                    : $"{ts.Minutes}m {ts.Seconds}s";
            }
        }
        public TransportType Transport => _record.TransportType;
        public string TransportText => _record.TransportType.ToString();
        public long BytesSent => _record.BytesSent;
        public long BytesReceived => _record.BytesReceived;
        public string TotalDataText => FormatBytes(_record.BytesSent + _record.BytesReceived);
        public string? DisconnectReason => _record.DisconnectReason;

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
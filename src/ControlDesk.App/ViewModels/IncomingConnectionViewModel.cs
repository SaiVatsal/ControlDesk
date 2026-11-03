using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlDesk.Core.Sessions;
using ControlDesk.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace ControlDesk.App.ViewModels;

/// <summary>
/// ViewModel for the incoming connection dialog - shown when a remote device requests a session.
/// </summary>
public sealed partial class IncomingConnectionViewModel : ObservableObject
{
    private readonly ILogger<IncomingConnectionViewModel> _logger;
    private SessionOfferPayload? _pendingOffer;
    private TaskCompletionSource<IncomingConnectionResult>? _completionSource;

    [ObservableProperty]
    private bool _isDialogOpen;

    [ObservableProperty]
    private string _requesterName = string.Empty;

    [ObservableProperty]
    private string _requesterDeviceId = string.Empty;

    [ObservableProperty]
    private AccessMode _requestedAccessMode;

    [ObservableProperty]
    private string _accessModeText = string.Empty;

    [ObservableProperty]
    private SessionPermissions _requestedPermissions = new(true, true, true, true, true, true, false);

    [ObservableProperty]
    private bool _grantViewScreen = true;

    [ObservableProperty]
    private bool _grantControlMouse = true;

    [ObservableProperty]
    private bool _grantControlKeyboard = true;

    [ObservableProperty]
    private bool _grantClipboardSync = true;

    [ObservableProperty]
    private bool _grantFileTransfer = true;

    [ObservableProperty]
    private bool _grantChat = true;

    [ObservableProperty]
    private string _statusText = "Waiting for connection...";

    public IncomingConnectionViewModel(ILogger<IncomingConnectionViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Shows the dialog for an incoming session offer.
    /// </summary>
    public Task<IncomingConnectionResult> ShowDialogAsync(SessionOfferPayload offer)
    {
        _pendingOffer = offer;
        _completionSource = new TaskCompletionSource<IncomingConnectionResult>();

        RequesterName = offer.SenderFriendlyName;
        RequesterDeviceId = offer.SenderDeviceId;
        RequestedAccessMode = offer.AccessMode;
        AccessModeText = offer.AccessMode == AccessMode.Unattended
            ? "Unattended Access (no manual approval required in future)"
            : "Attended Access (requires approval each time)";
        RequestedPermissions = offer.RequestedPermissions;

        GrantViewScreen = offer.RequestedPermissions.ViewScreen;
        GrantControlMouse = offer.RequestedPermissions.ControlMouse;
        GrantControlKeyboard = offer.RequestedPermissions.ControlKeyboard;
        GrantClipboardSync = offer.RequestedPermissions.ClipboardSync;
        GrantFileTransfer = offer.RequestedPermissions.FileTransfer;
        GrantChat = offer.RequestedPermissions.Chat;

        IsDialogOpen = true;
        StatusText = $"Incoming connection from {offer.SenderFriendlyName} ({offer.SenderDeviceId})";

        _logger.LogInformation("Showing incoming connection dialog for {Name} ({Id})", offer.SenderFriendlyName, offer.SenderDeviceId);

        return _completionSource.Task;
    }

    [RelayCommand]
    private void Accept()
    {
        if (_pendingOffer == null || _completionSource == null) return;

        var permissions = new SessionPermissions(
            GrantViewScreen,
            GrantControlMouse,
            GrantControlKeyboard,
            GrantClipboardSync,
            GrantFileTransfer,
            GrantChat,
            false // Recording not exposed in dialog
        );

        var result = new IncomingConnectionResult(true, permissions);
        _completionSource.SetResult(result);
        CloseDialog();

        _logger.LogInformation("User accepted connection from {Name} with permissions: View={View}, Mouse={Mouse}, Keyboard={Keyboard}",
            RequesterName, GrantViewScreen, GrantControlMouse, GrantControlKeyboard);
    }

    [RelayCommand]
    private void Decline()
    {
        if (_completionSource == null) return;

        var result = new IncomingConnectionResult(false, null, "User declined connection");
        _completionSource.SetResult(result);
        CloseDialog();

        _logger.LogInformation("User declined connection from {Name}", RequesterName);
    }

    private void CloseDialog()
    {
        IsDialogOpen = false;
        _pendingOffer = null;
        _completionSource = null;
    }

    public sealed record IncomingConnectionResult(bool Accepted, SessionPermissions? GrantedPermissions = null, string? RejectReason = null);
}
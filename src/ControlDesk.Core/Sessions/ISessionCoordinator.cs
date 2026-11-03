using ControlDesk.Shared.Protocol;

namespace ControlDesk.Core.Sessions;

/// <summary>
/// Active session context data.
/// </summary>
public record SessionContext(
    string SessionId,
    string RemoteDeviceId,
    string RemoteFriendlyName,
    SessionRole Role,
    AccessMode AccessMode,
    SessionPermissions Permissions,
    DateTimeOffset StartedAtUtc
);

/// <summary>
/// Event arguments for session state changes.
/// </summary>
public class SessionStateChangedEventArgs : EventArgs
{
    public SessionState PreviousState { get; }
    public SessionState CurrentState { get; }
    public string? Reason { get; }

    public SessionStateChangedEventArgs(SessionState previous, SessionState current, string? reason = null)
    {
        PreviousState = previous;
        CurrentState = current;
        Reason = reason;
    }
}

/// <summary>
/// Coordinates remote desktop sessions between controller and host.
/// </summary>
public interface ISessionCoordinator : IAsyncDisposable
{
    SessionState State { get; }
    SessionContext? CurrentSession { get; }
    SessionDiagnostics? Diagnostics { get; }

    event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    event EventHandler<SessionDiagnostics>? DiagnosticsUpdated;
    event EventHandler<SessionOfferPayload>? IncomingSessionOffered;

    Task ConnectAsync(string targetDeviceId, AccessMode accessMode, CancellationToken cancellationToken = default);
    Task AcceptIncomingSessionAsync(SessionPermissions grantedPermissions, CancellationToken cancellationToken = default);
    Task RejectIncomingSessionAsync(string reason, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);
    Task SendInputEventAsync(ReadOnlyMemory<byte> inputPacket, CancellationToken cancellationToken = default);
}

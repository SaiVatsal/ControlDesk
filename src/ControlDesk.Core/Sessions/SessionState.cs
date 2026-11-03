namespace ControlDesk.Core.Sessions;

/// <summary>
/// States of a remote desktop session.
/// </summary>
public enum SessionState
{
    Disconnected,
    Initializing,
    ConnectingToSignaling,
    RegisteredWithSignaling,
    RequestingSession,
    AwaitingApproval,
    Authenticating,
    ExchangingSdp,
    GatheringCandidates,
    EstablishingPeerConnection,
    Connected,
    Streaming,
    Reconnecting,
    Disconnecting,
    Terminated,
    Failed
}

/// <summary>
/// Session role: Controller or Host.
/// </summary>
public enum SessionRole
{
    Controller,
    Host
}

/// <summary>
/// Transport connection type.
/// </summary>
public enum TransportType
{
    Unknown,
    DirectPeerToPeer,
    StunAssisted,
    TurnRelay
}

/// <summary>
/// Diagnostic metrics for an active session.
/// </summary>
public record SessionDiagnostics(
    TransportType Transport,
    int RoundTripTimeMs,
    double FramesPerSecond,
    double CurrentBitrateKbps,
    double PacketLossPercentage,
    int ResolutionWidth,
    int ResolutionHeight,
    string ActiveEncoder,
    DateTimeOffset ConnectedSince
);
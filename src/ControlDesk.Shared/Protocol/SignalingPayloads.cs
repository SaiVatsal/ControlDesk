namespace ControlDesk.Shared.Protocol;

/// <summary>
/// Device registration payload.
/// </summary>
public record RegisterDevicePayload(
    string DeviceId,
    string PublicKeyBase64,
    string FriendlyName,
    string ClientVersion,
    DeviceCapabilities Capabilities
);

/// <summary>
/// Device capabilities.
/// </summary>
public record DeviceCapabilities(
    bool CanHost,
    bool CanControl,
    bool SupportsHardwareEncoding,
    string[] SupportedCodecs,
    int MaxResolutionWidth,
    int MaxResolutionHeight
);

/// <summary>
/// Device presence update.
/// </summary>
public record DevicePresencePayload(
    string DeviceId,
    DeviceStatus Status,
    DateTimeOffset LastSeenUtc,
    string? CurrentIp,
    string? NetworkType
);

/// <summary>
/// Device status enumeration.
/// </summary>
public enum DeviceStatus
{
    Offline,
    Online,
    Busy,
    Connecting,
    Away
}

/// <summary>
/// Session connection offer.
/// </summary>
public record SessionOfferPayload(
    string TargetDeviceId,
    string SenderDeviceId,
    string SenderFriendlyName,
    AccessMode AccessMode,
    string SdpOffer,
    TurnCredentials? TurnCredentials,
    SessionPermissions RequestedPermissions
);

/// <summary>
/// Session connection answer.
/// </summary>
public record SessionAnswerPayload(
    string TargetDeviceId,
    string SenderDeviceId,
    bool Accepted,
    string? SdpAnswer,
    string? RejectReason
);

/// <summary>
/// Access mode enumeration.
/// </summary>
public enum AccessMode
{
    Attended,
    Unattended
}

/// <summary>
/// Session permissions.
/// </summary>
public record SessionPermissions(
    bool ViewScreen,
    bool ControlMouse,
    bool ControlKeyboard,
    bool ClipboardSync,
    bool FileTransfer,
    bool Chat,
    bool Recording
);

/// <summary>
/// TURN credentials.
/// </summary>
public record TurnCredentials(
    string Username,
    string Credential,
    string[] Uris
);

/// <summary>
/// ICE candidate payload.
/// </summary>
public record IceCandidatePayload(
    string TargetDeviceId,
    string Candidate,
    string SdpMid,
    int SdpMLineIndex
);

/// <summary>
/// Challenge request for unattended access.
/// </summary>
public record ChallengeRequestPayload(
    string TargetDeviceId,
    string ControllerDeviceId,
    string ControllerPublicKeyBase64
);

/// <summary>
/// Challenge response with nonce.
/// </summary>
public record ChallengeResponsePayload(
    string NonceBase64,
    DateTimeOffset ServerTimestampUtc,
    DateTimeOffset ValidUntilUtc
);

/// <summary>
/// Challenge verification with signature.
/// </summary>
public record ChallengeVerifyPayload(
    string SignatureBase64,
    string SdpOffer
);

/// <summary>
/// Device revocation.
/// </summary>
public record RevokeDevicePayload(
    string TargetDeviceId,
    string RevokedDeviceId,
    RevocationReason Reason
);

/// <summary>
/// Revocation reasons.
/// </summary>
public enum RevocationReason
{
    UserRequested,
    SecurityCompromise,
    DeviceLost,
    PolicyViolation
}

/// <summary>
/// Error payload.
/// </summary>
public record ErrorPayload(
    string Code,
    string Message,
    string? Details
);
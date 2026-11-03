namespace ControlDesk.Shared.Protocol;

/// <summary>
/// Represents a WebSocket signaling message envelope.
/// </summary>
public record SignalingMessage<T>(string Type, string MessageId, DateTimeOffset TimestampUtc, T Payload);

/// <summary>
/// Known signaling message types.
/// </summary>
public static class SignalingMessageTypes
{
    public const string RegisterDevice = "register_device";
    public const string DevicePresence = "device_presence";
    public const string SessionOffer = "session_offer";
    public const string SessionAnswer = "session_answer";
    public const string IceCandidate = "ice_candidate";
    public const string ChallengeRequest = "challenge_request";
    public const string ChallengeResponse = "challenge_response";
    public const string ChallengeVerify = "challenge_verify";
    public const string RevokeDevice = "revoke_device";
    public const string Heartbeat = "heartbeat";
    public const string Error = "error";
}
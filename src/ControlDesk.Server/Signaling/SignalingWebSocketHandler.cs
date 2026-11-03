using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Server.Signaling;

/// <summary>
/// Connected client device state.
/// </summary>
public sealed class ConnectedDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public DeviceCapabilities? Capabilities { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Online;
    public WebSocket Socket { get; set; } = null!;
    public DateTimeOffset ConnectedAtUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public SemaphoreSlim SendLock { get; } = new(1, 1);
}

/// <summary>
/// High-performance WebSocket handler for device registration, presence, and signaling routing.
/// </summary>
public sealed class SignalingWebSocketHandler
{
    private readonly ConcurrentDictionary<string, ConnectedDevice> _devices = new();
    private readonly string _turnSecret;
    private readonly string[] _turnUris;
    private readonly ILogger<SignalingWebSocketHandler> _logger;

    public SignalingWebSocketHandler(IConfiguration configuration, ILogger<SignalingWebSocketHandler> logger)
    {
        _logger = logger;
        _turnSecret = configuration["Turn:Secret"] ?? "ControlDeskTurnSecret2026ChangeInProduction";
        var turnDomain = configuration["Turn:Domain"] ?? "turn.controldesk.internal";
        _turnUris = new[]
        {
            $"turn:{turnDomain}:3478?transport=udp",
            $"turn:{turnDomain}:3478?transport=tcp",
            $"turns:{turnDomain}:5349?transport=tcp"
        };
    }

    public async Task HandleWebSocketAsync(HttpContext context, WebSocket socket)
    {
        string? registeredDeviceId = null;
        var buffer = new byte[65536];

        try
        {
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var receiveResult = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (receiveResult.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                var deviceId = await ProcessMessageAsync(socket, json, registeredDeviceId, context.RequestAborted);
                if (deviceId != null && registeredDeviceId == null)
                {
                    registeredDeviceId = deviceId;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in WebSocket loop for device {DeviceId}", registeredDeviceId ?? "unknown");
        }
        finally
        {
            if (registeredDeviceId != null)
            {
                if (_devices.TryRemove(registeredDeviceId, out var device))
                {
                    _logger.LogInformation("Device {DeviceId} ({FriendlyName}) disconnected", device.DeviceId, device.FriendlyName);
                    await BroadcastPresenceAsync(device.DeviceId, DeviceStatus.Offline);
                }
            }
        }
    }

    private async Task<string?> ProcessMessageAsync(WebSocket senderSocket, string json, string? registeredDeviceId, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type", out var typeProp))
                return null;

            var type = typeProp.GetString();
            if (!root.TryGetProperty("Payload", out var payloadProp))
                return null;

            switch (type)
            {
                case SignalingMessageTypes.RegisterDevice:
                {
                    var payload = JsonSerializer.Deserialize<RegisterDevicePayload>(payloadProp.GetRawText());
                    if (payload == null) return null;

                    var device = new ConnectedDevice
                    {
                        DeviceId = payload.DeviceId,
                        FriendlyName = payload.FriendlyName,
                        PublicKeyBase64 = payload.PublicKeyBase64,
                        Capabilities = payload.Capabilities,
                        Status = DeviceStatus.Online,
                        Socket = senderSocket,
                        ConnectedAtUtc = DateTimeOffset.UtcNow,
                        LastSeenUtc = DateTimeOffset.UtcNow
                    };

                    _devices.AddOrUpdate(payload.DeviceId, device, (_, existing) =>
                    {
                        try { existing.Socket.Abort(); } catch { }
                        return device;
                    });

                    _logger.LogInformation("Device {DeviceId} registered successfully ({FriendlyName})", payload.DeviceId, payload.FriendlyName);

                    // Send presence acknowledgment
                    await SendToDeviceAsync(senderSocket, SignalingMessageTypes.DevicePresence, new DevicePresencePayload(
                        DeviceId: payload.DeviceId,
                        Status: DeviceStatus.Online,
                        LastSeenUtc: DateTimeOffset.UtcNow,
                        CurrentIp: null,
                        NetworkType: null
                    ), cancellationToken);

                    return payload.DeviceId;
                }

                case SignalingMessageTypes.SessionOffer:
                {
                    var offer = JsonSerializer.Deserialize<SessionOfferPayload>(payloadProp.GetRawText());
                    if (offer == null) return null;

                    if (_devices.TryGetValue(offer.TargetDeviceId, out var targetDevice) && targetDevice.Socket.State == WebSocketState.Open)
                    {
                        // Generate ephemeral coturn credentials for this session
                        var turnCredentials = GenerateTurnCredentials(offer.SenderDeviceId);
                        var enrichedOffer = offer with { TurnCredentials = turnCredentials };

                        await SendToDeviceAsync(targetDevice.Socket, SignalingMessageTypes.SessionOffer, enrichedOffer, cancellationToken);
                        _logger.LogInformation("Routed SessionOffer from {Sender} to {Target}", offer.SenderDeviceId, offer.TargetDeviceId);
                    }
                    else
                    {
                        await SendToDeviceAsync(senderSocket, SignalingMessageTypes.Error, new ErrorPayload(
                            Code: "TARGET_OFFLINE",
                            Message: $"Target device {offer.TargetDeviceId} is offline or unreachable.",
                            Details: null
                        ), cancellationToken);
                    }
                    break;
                }

                case SignalingMessageTypes.SessionAnswer:
                {
                    var answer = JsonSerializer.Deserialize<SessionAnswerPayload>(payloadProp.GetRawText());
                    if (answer == null) return null;

                    if (_devices.TryGetValue(answer.TargetDeviceId, out var targetDevice) && targetDevice.Socket.State == WebSocketState.Open)
                    {
                        await SendToDeviceAsync(targetDevice.Socket, SignalingMessageTypes.SessionAnswer, answer, cancellationToken);
                        _logger.LogInformation("Routed SessionAnswer from {Sender} to {Target} (Accepted: {Accepted})",
                            answer.SenderDeviceId, answer.TargetDeviceId, answer.Accepted);
                    }
                    break;
                }

                case SignalingMessageTypes.IceCandidate:
                {
                    var candidate = JsonSerializer.Deserialize<IceCandidatePayload>(payloadProp.GetRawText());
                    if (candidate == null) return null;

                    if (_devices.TryGetValue(candidate.TargetDeviceId, out var targetDevice) && targetDevice.Socket.State == WebSocketState.Open)
                    {
                        await SendToDeviceAsync(targetDevice.Socket, SignalingMessageTypes.IceCandidate, candidate, cancellationToken);
                    }
                    break;
                }

                case SignalingMessageTypes.ChallengeRequest:
                {
                    var challenge = JsonSerializer.Deserialize<ChallengeRequestPayload>(payloadProp.GetRawText());
                    if (challenge == null) return null;

                    if (_devices.TryGetValue(challenge.TargetDeviceId, out var targetDevice) && targetDevice.Socket.State == WebSocketState.Open)
                    {
                        await SendToDeviceAsync(targetDevice.Socket, SignalingMessageTypes.ChallengeRequest, challenge, cancellationToken);
                    }
                    break;
                }

                case SignalingMessageTypes.ChallengeResponse:
                {
                    // Route back to controller
                    if (registeredDeviceId != null)
                    {
                        // Broadcast or route to pairing peer
                    }
                    break;
                }

                case SignalingMessageTypes.Heartbeat:
                {
                    if (registeredDeviceId != null && _devices.TryGetValue(registeredDeviceId, out var dev))
                    {
                        dev.LastSeenUtc = DateTimeOffset.UtcNow;
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process signaling message");
        }

        return null;
    }

    private TurnCredentials GenerateTurnCredentials(string deviceId)
    {
        // Coturn REST API ephemeral HMAC-SHA1 credential generation
        // Format: username = timestamp:deviceId
        // password = Base64(HMAC-SHA1(turnSecret, username))
        long expiryTimestamp = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds();
        string username = $"{expiryTimestamp}:{deviceId}";

        byte[] secretBytes = Encoding.UTF8.GetBytes(_turnSecret);
        byte[] usernameBytes = Encoding.UTF8.GetBytes(username);

        using var hmac = new HMACSHA1(secretBytes);
        byte[] hash = hmac.ComputeHash(usernameBytes);
        string password = Convert.ToBase64String(hash);

        return new TurnCredentials(
            Username: username,
            Credential: password,
            Uris: _turnUris
        );
    }

    private static async Task SendToDeviceAsync<T>(WebSocket socket, string type, T payload, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;

        var message = new SignalingMessage<T>(
            Type: type,
            MessageId: Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow,
            Payload: payload
        );

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task BroadcastPresenceAsync(string deviceId, DeviceStatus status)
    {
        var payload = new DevicePresencePayload(
            DeviceId: deviceId,
            Status: status,
            LastSeenUtc: DateTimeOffset.UtcNow,
            CurrentIp: null,
            NetworkType: null
        );

        foreach (var dev in _devices.Values)
        {
            if (dev.Socket.State == WebSocketState.Open && dev.DeviceId != deviceId)
            {
                try
                {
                    await SendToDeviceAsync(dev.Socket, SignalingMessageTypes.DevicePresence, payload, CancellationToken.None);
                }
                catch { }
            }
        }
    }
}

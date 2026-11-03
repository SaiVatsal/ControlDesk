namespace ControlDesk.Shared.Protocol;

/// <summary>
/// Packet type identifiers for DataChannel binary framing.
/// </summary>
public enum PacketType : byte
{
    MouseMove = 0x01,
    MouseButton = 0x02,
    MouseWheel = 0x03,
    Keyboard = 0x04,
    ClipboardSync = 0x05,
    FileTransferRequest = 0x06,
    FileTransferAccept = 0x07,
    FileTransferChunk = 0x08,
    FileTransferCompleted = 0x09,
    FileTransferCancel = 0x0A,
    Chat = 0x0B,
    HeartbeatPing = 0x0C,
    HeartbeatPong = 0x0D,
    DisplayConfiguration = 0x0E,
    PermissionUpdate = 0x0F
}

/// <summary>
/// Mouse button enumeration.
/// </summary>
public enum MouseButton : byte
{
    None = 0x00,
    Left = 0x01,
    Right = 0x02,
    Middle = 0x03,
    XButton1 = 0x04,
    XButton2 = 0x05
}

/// <summary>
/// Mouse button action enumeration.
/// </summary>
public enum MouseAction : byte
{
    Down = 0x01,
    Up = 0x02
}

/// <summary>
/// Keyboard action enumeration.
/// </summary>
public enum KeyAction : byte
{
    Down = 0x01,
    Up = 0x02
}

/// <summary>
/// Wheel orientation enumeration.
/// </summary>
public enum WheelOrientation : byte
{
    Vertical = 0x00,
    Horizontal = 0x01
}

/// <summary>
/// High-speed binary framing serializer for DataChannel messages.
/// </summary>
public static class BinaryFrameSerializer
{
    public static byte[] SerializeMouseMove(uint normX, uint normY)
    {
        var buffer = new byte[9];
        buffer[0] = (byte)PacketType.MouseMove;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 4), normX);
        BitConverter.TryWriteBytes(buffer.AsSpan(5, 4), normY);
        return buffer;
    }

    public static (uint NormX, uint NormY) DeserializeMouseMove(ReadOnlySpan<byte> span)
    {
        uint x = BitConverter.ToUInt32(span.Slice(1, 4));
        uint y = BitConverter.ToUInt32(span.Slice(5, 4));
        return (x, y);
    }

    public static byte[] SerializeMouseButton(MouseButton button, MouseAction action, byte flags = 0)
    {
        return new byte[] { (byte)PacketType.MouseButton, (byte)button, (byte)action, flags };
    }

    public static (MouseButton Button, MouseAction Action, byte Flags) DeserializeMouseButton(ReadOnlySpan<byte> span)
    {
        return ((MouseButton)span[1], (MouseAction)span[2], span[3]);
    }

    public static byte[] SerializeMouseWheel(short delta, WheelOrientation orientation)
    {
        var buffer = new byte[4];
        buffer[0] = (byte)PacketType.MouseWheel;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 2), delta);
        buffer[3] = (byte)orientation;
        return buffer;
    }

    public static (short Delta, WheelOrientation Orientation) DeserializeMouseWheel(ReadOnlySpan<byte> span)
    {
        short delta = BitConverter.ToInt16(span.Slice(1, 2));
        WheelOrientation orientation = (WheelOrientation)span[3];
        return (delta, orientation);
    }

    public static byte[] SerializeKeyboard(ushort scanCode, bool isExtended, KeyAction action)
    {
        var buffer = new byte[5];
        buffer[0] = (byte)PacketType.Keyboard;
        BitConverter.TryWriteBytes(buffer.AsSpan(1, 2), scanCode);
        buffer[3] = isExtended ? (byte)1 : (byte)0;
        buffer[4] = (byte)action;
        return buffer;
    }

    public static (ushort ScanCode, bool IsExtended, KeyAction Action) DeserializeKeyboard(ReadOnlySpan<byte> span)
    {
        ushort scanCode = BitConverter.ToUInt16(span.Slice(1, 2));
        bool isExtended = span[3] != 0;
        KeyAction action = (KeyAction)span[4];
        return (scanCode, isExtended, action);
    }
}

/// <summary>
/// Display configuration data.
/// </summary>
public record DisplayConfig(
    int PrimaryWidth,
    int PrimaryHeight,
    double DpiScale = 1.0,
    int MonitorCount = 1
);

/// <summary>
/// File transfer control message packet.
/// </summary>
public record FileTransferControlPacket(
    string TransferId,
    PacketType Action,
    string? FileName = null,
    long? FileSizeBytes = null,
    string? Sha256 = null,
    string? Reason = null
);

/// <summary>
/// File data chunk packet.
/// </summary>
public record FileChunkPacket(
    string TransferId,
    int ChunkIndex,
    int TotalChunks,
    byte[] Data
);

/// <summary>
/// Clipboard synchronization packet.
/// </summary>
public record ClipboardSyncPacket(
    string Text,
    string ContentHash
);

/// <summary>
/// In-session chat message packet.
/// </summary>
public record ChatMessagePacket(
    string MessageId,
    string SenderId,
    string SenderName,
    string Content,
    DateTimeOffset TimestampUtc
);
using ControlDesk.Shared.Protocol;

namespace ControlDesk.Core.Interfaces;

/// <summary>
/// Normalized input coordinates (0..65535 for each axis in virtual desktop space).
/// </summary>
public readonly record struct NormalizedCoordinates(uint X, uint Y);

/// <summary>
/// Keyboard input specification for injection.
/// </summary>
public readonly record struct KeyboardInput(ushort ScanCode, bool IsExtended, KeyAction Action);

/// <summary>
/// Mouse input specification for injection.
/// </summary>
public readonly record struct MouseInput(
    NormalizedCoordinates NormalizedPosition,
    MouseButton Button,
    MouseAction Action,
    short WheelDelta,
    WheelOrientation WheelOrientation
);

/// <summary>
/// Abstraction for injecting remote input events on the host machine.
/// </summary>
public interface IInputInjector : IDisposable
{
    /// <summary>
    /// Injects a mouse movement event.
    /// </summary>
    void InjectMouseMove(NormalizedCoordinates position);

    /// <summary>
    /// Injects a mouse button event.
    /// </summary>
    void InjectMouseButton(MouseButton button, MouseAction action);

    /// <summary>
    /// Injects a mouse wheel event.
    /// </summary>
    void InjectMouseWheel(short delta, WheelOrientation orientation);

    /// <summary>
    /// Injects a keyboard event.
    /// </summary>
    void InjectKeyboard(ushort scanCode, bool isExtended, KeyAction action);

    /// <summary>
    /// Converts normalized coordinates (0..65535) to absolute virtual desktop coordinates.
    /// </summary>
    NormalizedCoordinates ToNormalized(int virtualDesktopX, int virtualDesktopY, int virtualDesktopWidth, int virtualDesktopHeight);

    /// <summary>
    /// Converts absolute virtual desktop coordinates to normalized coordinates (0..65535).
    /// </summary>
    (int X, int Y) ToAbsolute(NormalizedCoordinates normalized, int virtualDesktopWidth, int virtualDesktopHeight);
}
using Funke;

namespace Funke.InputActions;

/// <summary>Implement on the game thread; Frame must advance once per input sample, including while game time is paused.</summary>
public interface IInputSource
{
    long Frame { get; }
    double UnscaledTime { get; }
    bool GetKey(KeyCode key);
    bool GetMouseButton(MouseButton button);
    bool IsGamepadConnected(int gamepad);
    bool GetGamepadButton(GamepadButton button, int gamepad);
    /// <summary>Raw sticks are -1..1 with Y down-positive; raw triggers are -1 released to +1 pressed.</summary>
    float GetGamepadAxis(GamepadAxis axis, int gamepad);
    Vector2 MouseDelta { get; }
    float MouseWheel { get; }
}

public sealed class FunkeInputSource : IInputSource
{
    public static FunkeInputSource Instance { get; } = new();

    private FunkeInputSource() { }

    public long Frame => Time.FrameCount;
    public double UnscaledTime => Time.RealtimeSinceStartup;
    public bool GetKey(KeyCode key) => global::Funke.Input.GetKey(key);
    public bool GetMouseButton(MouseButton button) => global::Funke.Input.GetMouseButton(button);
    public bool IsGamepadConnected(int gamepad) => global::Funke.Input.IsGamepadConnected(gamepad);
    public bool GetGamepadButton(GamepadButton button, int gamepad) => global::Funke.Input.GetGamepadButton(button, gamepad);
    public float GetGamepadAxis(GamepadAxis axis, int gamepad) => global::Funke.Input.GetGamepadAxis(axis, gamepad);
    public Vector2 MouseDelta => global::Funke.Input.GetMouseDelta();
    public float MouseWheel => global::Funke.Input.GetScrollWheelDelta();
}

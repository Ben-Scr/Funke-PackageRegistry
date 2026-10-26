using System.Collections.Generic;
using Funke;
using Funke.InputActions;

namespace FunkeInput.Tests;

internal sealed class FakeInputSource : IInputSource
{
    public long Frame { get; set; }
    public double UnscaledTime { get; set; }
    public Vector2 MouseDelta { get; set; }
    public float MouseWheel { get; set; }
    public HashSet<KeyCode> Keys { get; } = new();
    public HashSet<MouseButton> MouseButtons { get; } = new();
    public HashSet<int> ConnectedGamepads { get; } = new();
    public Dictionary<(int Gamepad, GamepadButton Button), bool> Buttons { get; } = new();
    public Dictionary<(int Gamepad, GamepadAxis Axis), float> Axes { get; } = new();

    public bool GetKey(KeyCode key) => Keys.Contains(key);
    public bool GetMouseButton(MouseButton button) => MouseButtons.Contains(button);
    public bool IsGamepadConnected(int gamepad) => ConnectedGamepads.Contains(gamepad);
    public bool GetGamepadButton(GamepadButton button, int gamepad)
        => Buttons.TryGetValue((gamepad, button), out bool held) && held;
    public float GetGamepadAxis(GamepadAxis axis, int gamepad)
        => Axes.TryGetValue((gamepad, axis), out float value) ? value
            : axis is GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger ? -1f : 0f;

    public void NextFrame(double seconds = 1.0 / 60.0)
    {
        Frame++;
        UnscaledTime += seconds;
        MouseDelta = Vector2.Zero;
        MouseWheel = 0f;
    }

    public void SetKey(KeyCode key, bool held = true)
    {
        if (held) Keys.Add(key);
        else Keys.Remove(key);
    }

    public void SetButton(GamepadButton button, bool held = true, int gamepad = 0)
        => Buttons[(gamepad, button)] = held;

    public void SetAxis(GamepadAxis axis, float value, int gamepad = 0)
        => Axes[(gamepad, axis)] = value;
}

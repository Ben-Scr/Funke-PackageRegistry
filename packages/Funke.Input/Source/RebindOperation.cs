using System;
using System.Collections.Generic;

namespace Funke.InputActions;

public enum InputBindingPart { Whole, Negative, Positive, Up, Down, Left, Right }
public enum RebindStatus { Waiting, Completed, Canceled, TimedOut }
public enum RebindDevices { Any, KeyboardMouse, Gamepad }

public sealed record RebindOptions
{
    public double TimeoutSeconds { get; init; } = 10;
    public KeyCode? CancelKey { get; init; } = KeyCode.Escape;
    public RebindDevices Devices { get; init; } = RebindDevices.Any;
    public float AxisThreshold { get; init; } = 0.75f;
}

/// <summary>Poll Status/IsFinished or the owning map each Update. Inputs held at creation must be released first.</summary>
public sealed class RebindOperation : IDisposable
{
    private static readonly KeyCode[] s_Keys = Enum.GetValues<KeyCode>();
    private static readonly MouseButton[] s_MouseButtons = Enum.GetValues<MouseButton>();
    private static readonly GamepadButton[] s_GamepadButtons = Enum.GetValues<GamepadButton>();
    private static readonly GamepadAxis[] s_Axes = Enum.GetValues<GamepadAxis>();
    private readonly InputAction m_Action;
    private readonly int m_Index;
    private readonly InputBindingPart m_Part;
    private readonly RebindOptions m_Options;
    private readonly double m_StartedAt;
    private readonly long m_StartedFrame;
    private readonly int m_Gamepad;
    private readonly InputBinding m_Original;
    private readonly float m_NeutralThreshold;
    private readonly HashSet<(InputBindingKind Kind, int Code)> m_Blocked = new();
    private RebindStatus m_Status;
    private InputBinding? m_Result;

    internal RebindOperation(InputAction action, int index, InputBindingPart part, RebindOptions options, IInputSource source)
    {
        if (!Enum.IsDefined(part)) throw new ArgumentOutOfRangeException(nameof(part));
        if (!Enum.IsDefined(options.Devices)) throw new ArgumentOutOfRangeException(nameof(options));
        if (!double.IsFinite(options.TimeoutSeconds) || options.TimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "TimeoutSeconds must be finite and positive.");
        if (!float.IsFinite(options.AxisThreshold) || options.AxisThreshold <= 0 || options.AxisThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "AxisThreshold must be in (0, 1].");
        if (options.CancelKey is KeyCode cancel && !Enum.IsDefined(cancel))
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown cancel key.");
        m_Original = action.Bindings[index];
        if (part == InputBindingPart.Whole)
        {
            if (action.Type == InputActionType.Vector2)
                throw new ArgumentException("Rebind a Keys2D direction, or use OverrideBinding for a complete Vector2 binding.", nameof(part));
        }
        else
        {
            bool axisPart = m_Original.Kind == InputBindingKind.KeysAxis
                && (part == InputBindingPart.Negative || part == InputBindingPart.Positive);
            bool vectorPart = m_Original.Kind == InputBindingKind.Keys2D
                && (part == InputBindingPart.Up || part == InputBindingPart.Down || part == InputBindingPart.Left || part == InputBindingPart.Right);
            if (!axisPart && !vectorPart) throw new ArgumentException("The selected part does not belong to this keyboard composite.", nameof(part));
            if (options.Devices == RebindDevices.Gamepad || !action.Map.KeyboardMouseEnabled)
                throw new ArgumentException("Keyboard composite parts require keyboard input.", nameof(options));
        }
        m_Action = action;
        m_Index = index;
        m_Part = part;
        m_Options = options;
        m_NeutralThreshold = Math.Min(0.2f, options.AxisThreshold * 0.5f);
        m_Gamepad = action.Map.Gamepad;
        m_StartedAt = source.UnscaledTime;
        m_StartedFrame = source.Frame;
        foreach (KeyCode key in s_Keys)
            if (source.GetKey(key)) m_Blocked.Add((InputBindingKind.Key, (int)key));
        foreach (MouseButton button in s_MouseButtons)
            if (source.GetMouseButton(button)) m_Blocked.Add((InputBindingKind.MouseButton, (int)button));
        if (source.IsGamepadConnected(m_Gamepad))
        {
            foreach (GamepadButton button in s_GamepadButtons)
                if (source.GetGamepadButton(button, m_Gamepad)) m_Blocked.Add((InputBindingKind.GamepadButton, (int)button));
            foreach (GamepadAxis axis in s_Axes)
                if (MathF.Abs(ReadAxis(source, axis)) > m_NeutralThreshold) m_Blocked.Add((InputBindingKind.GamepadAxis, (int)axis));
        }
    }

    public RebindStatus Status { get { Update(); return m_Status; } }
    public bool IsFinished { get { Update(); return Finished; } }
    public InputBinding? Result { get { Update(); return m_Result; } }
    internal bool Finished => m_Status != RebindStatus.Waiting;

    public void Update()
    {
        if (!Finished) m_Action.Map.Update();
    }

    public void Cancel()
    {
        if (Finished) return;
        m_Status = RebindStatus.Canceled;
        m_Action.Map.RebindCanceled(this);
    }

    public void Dispose() => Cancel();

    internal void Poll(IInputSource source)
    {
        if (Finished) return;
        if (m_Action.Map.Gamepad != m_Gamepad)
        {
            Cancel();
            return;
        }
        double elapsed = source.UnscaledTime - m_StartedAt;
        if (double.IsFinite(elapsed) && elapsed >= m_Options.TimeoutSeconds)
        {
            m_Status = RebindStatus.TimedOut;
            return;
        }
        if (source.Frame == m_StartedFrame) return;

        bool connected = source.IsGamepadConnected(m_Gamepad);
        m_Blocked.RemoveWhere(control => control.Kind switch
        {
            InputBindingKind.Key => !source.GetKey((KeyCode)control.Code),
            InputBindingKind.MouseButton => !source.GetMouseButton((MouseButton)control.Code),
            InputBindingKind.GamepadButton => !connected || !source.GetGamepadButton((GamepadButton)control.Code, m_Gamepad),
            InputBindingKind.GamepadAxis => !connected || MathF.Abs(ReadAxis(source, (GamepadAxis)control.Code)) <= m_NeutralThreshold,
            _ => true,
        });

        if (m_Options.CancelKey is KeyCode cancel && source.GetKey(cancel)
            && !m_Blocked.Contains((InputBindingKind.Key, (int)cancel)))
        {
            Cancel();
            return;
        }

        bool keyboard = m_Options.Devices != RebindDevices.Gamepad && m_Action.Map.KeyboardMouseEnabled;
        if (keyboard)
        {
            foreach (KeyCode key in s_Keys)
                if (source.GetKey(key) && !m_Blocked.Contains((InputBindingKind.Key, (int)key)))
                {
                    Complete(m_Part switch
                    {
                        InputBindingPart.Negative => m_Original with { Negative = (int)key },
                        InputBindingPart.Positive => m_Original with { Positive = (int)key },
                        InputBindingPart.Up => m_Original with { Up = (int)key },
                        InputBindingPart.Down => m_Original with { Down = (int)key },
                        InputBindingPart.Left => m_Original with { Left = (int)key },
                        InputBindingPart.Right => m_Original with { Right = (int)key },
                        _ => InputBinding.Key(key),
                    });
                    return;
                }
            if (m_Part == InputBindingPart.Whole)
                foreach (MouseButton button in s_MouseButtons)
                    if (source.GetMouseButton(button) && !m_Blocked.Contains((InputBindingKind.MouseButton, (int)button)))
                    {
                        Complete(InputBinding.Mouse(button));
                        return;
                    }
        }

        if (m_Part != InputBindingPart.Whole || m_Options.Devices == RebindDevices.KeyboardMouse || !connected) return;
        foreach (GamepadButton button in s_GamepadButtons)
            if (source.GetGamepadButton(button, m_Gamepad) && !m_Blocked.Contains((InputBindingKind.GamepadButton, (int)button)))
            {
                Complete(InputBinding.Button(button));
                return;
            }
        foreach (GamepadAxis axis in s_Axes)
        {
            float value = ReadAxis(source, axis);
            if (MathF.Abs(value) < m_Options.AxisThreshold || m_Blocked.Contains((InputBindingKind.GamepadAxis, (int)axis))) continue;
            Complete(InputBinding.Axis(axis, scale: value < 0 ? -1f : 1f));
            return;
        }
    }

    private float ReadAxis(IInputSource source, GamepadAxis axis)
    {
        float value = source.GetGamepadAxis(axis, m_Gamepad);
        if (!float.IsFinite(value)) return 0;
        if (axis == GamepadAxis.LeftTrigger || axis == GamepadAxis.RightTrigger) return Math.Clamp((value + 1f) * 0.5f, 0f, 1f);
        return Math.Clamp(value, -1f, 1f);
    }

    private void Complete(InputBinding binding)
    {
        m_Action.ApplyBindingOverride(m_Index, binding);
        m_Result = binding;
        m_Status = RebindStatus.Completed;
    }
}

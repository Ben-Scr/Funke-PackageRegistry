using System;
using System.Collections.Generic;

namespace Funke.InputActions;

/// <summary>Poll in Update; edge flags stay stable for the entire input frame.</summary>
public sealed class InputAction
{
    private readonly List<InputBinding> m_Defaults = new();
    private readonly List<InputBinding> m_Bindings = new();
    private readonly IReadOnlyList<InputBinding> m_ReadOnlyBindings;
    private Vector2 m_Value;
    private bool m_Pressed;
    private bool m_WasPressed;
    private bool m_WasReleased;
    private bool m_SuppressUntilRelease;
    private float m_PressThreshold = 0.5f;

    internal InputAction(InputActionMap map, string name, InputActionType type)
    {
        Map = map;
        Name = name;
        Type = type;
        m_ReadOnlyBindings = m_Bindings.AsReadOnly();
    }

    public InputActionMap Map { get; }
    public string Name { get; }
    public InputActionType Type { get; }
    public IReadOnlyList<InputBinding> Bindings => m_ReadOnlyBindings;
    public int BindingCount => m_Bindings.Count;
    public bool IsPressed { get { Map.Update(); return m_Pressed; } }
    public bool WasPressed { get { Map.Update(); return m_WasPressed; } }
    public bool WasReleased { get { Map.Update(); return m_WasReleased; } }

    public float PressThreshold
    {
        get => m_PressThreshold;
        set
        {
            Map.ThrowIfDisposed();
            if (!float.IsFinite(value) || value <= 0f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value), "Use a threshold in (0, 1].");
            m_PressThreshold = value;
            Map.Invalidate();
        }
    }

    public float ReadValue()
    {
        if (Type == InputActionType.Vector2) throw new InvalidOperationException("Use ReadVector2 for a Vector2 action.");
        Map.Update();
        return m_Value.X;
    }

    public Vector2 ReadVector2()
    {
        if (Type != InputActionType.Vector2) throw new InvalidOperationException("Use ReadValue for a button or axis action.");
        Map.Update();
        return m_Value;
    }

    public InputAction Bind(InputBinding binding)
    {
        Map.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate(Type);
        m_Defaults.Add(binding);
        m_Bindings.Add(binding);
        Map.Invalidate();
        return this;
    }

    public InputAction Bind(KeyCode key) => Bind(InputBinding.Key(key));
    public InputAction Bind(MouseButton button) => Bind(InputBinding.Mouse(button));
    public InputAction Bind(GamepadButton button) => Bind(InputBinding.Button(button));
    public InputAction BindKeys(KeyCode negative, KeyCode positive) => Bind(InputBinding.Keys(negative, positive));
    public InputAction Bind2D(KeyCode up, KeyCode down, KeyCode left, KeyCode right) => Bind(InputBinding.Keys2D(up, down, left, right));
    public InputAction BindWASD() => Bind(InputBinding.WASD());
    public InputAction BindArrows() => Bind(InputBinding.Arrows());
    public InputAction BindLeftStick(float deadzone = 0.2f) => Bind(InputBinding.LeftStick(deadzone));
    public InputAction BindRightStick(float deadzone = 0.2f) => Bind(InputBinding.RightStick(deadzone));
    public InputAction BindDPad() => Bind(InputBinding.DPad());
    public InputAction BindMouseDelta(float sensitivity = 1f, bool invertY = false) => Bind(InputBinding.MouseDelta(sensitivity, invertY));
    public InputAction BindMouseWheel(float scale = 1f) => Bind(InputBinding.MouseWheel(scale));

    public InputBinding GetDefaultBinding(int index) => m_Defaults[index];
    public bool HasBindingOverride(int index) => m_Bindings[index] != m_Defaults[index];

    public void OverrideBinding(int index, InputBinding binding)
    {
        Map.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate(Type);
        _ = m_Bindings[index];
        Map.CancelRebinding();
        ApplyBindingOverride(index, binding);
    }

    internal void ApplyBindingOverride(int index, InputBinding binding)
    {
        Map.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate(Type);
        m_Bindings[index] = binding;
        ResetState(true);
        Map.Invalidate();
    }

    public void ResetBinding(int index) => OverrideBinding(index, m_Defaults[index]);

    public void ResetBindings()
    {
        Map.ThrowIfDisposed();
        Map.CancelRebinding();
        for (int i = 0; i < m_Bindings.Count; i++) m_Bindings[i] = m_Defaults[i];
        ResetState(true);
        Map.Invalidate();
    }

    public RebindOperation Rebind(int bindingIndex = 0, InputBindingPart part = InputBindingPart.Whole, RebindOptions? options = null)
        => Map.BeginRebind(this, bindingIndex, part, options ?? new RebindOptions());

    internal void ResetState(bool suppressHeldButtons)
    {
        m_Value = Vector2.Zero;
        m_Pressed = m_WasPressed = m_WasReleased = false;
        m_SuppressUntilRelease = suppressHeldButtons && Type == InputActionType.Button
            && Map.ShouldSampleHeldControls && IsActuated(SampleValue(Map.Source, Map.Gamepad, Map.KeyboardMouseEnabled));
    }

    private Vector2 SampleValue(IInputSource source, int gamepad, bool keyboardMouseEnabled)
    {
        Vector2 value = Vector2.Zero;
        double best = 0;
        foreach (InputBinding binding in m_Bindings)
        {
            Vector2 candidate = binding.Read(source, gamepad, keyboardMouseEnabled);
            if (Type == InputActionType.Button) candidate = new Vector2(Math.Max(0f, candidate.X), 0f);
            double magnitude = (double)candidate.X * candidate.X + (double)candidate.Y * candidate.Y;
            if (magnitude <= best) continue;
            best = magnitude;
            value = candidate;
        }
        return value;
    }

    private bool IsActuated(Vector2 value)
        => (double)value.X * value.X + (double)value.Y * value.Y >= (double)m_PressThreshold * m_PressThreshold;

    internal void Evaluate(IInputSource source, int gamepad, bool keyboardMouseEnabled)
    {
        Vector2 value = SampleValue(source, gamepad, keyboardMouseEnabled);
        bool pressed = IsActuated(value);
        if (m_SuppressUntilRelease)
        {
            m_SuppressUntilRelease = pressed;
            value = Vector2.Zero;
            pressed = false;
        }
        m_WasPressed = pressed && !m_Pressed;
        m_WasReleased = !pressed && m_Pressed;
        m_Pressed = pressed;
        m_Value = Type == InputActionType.Button ? new Vector2(pressed ? 1f : 0f, 0f) : value;
    }
}

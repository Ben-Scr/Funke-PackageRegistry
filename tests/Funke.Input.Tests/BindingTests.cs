using System;
using Funke;
using Funke.InputActions;
using Xunit;

namespace FunkeInput.Tests;

public sealed class BindingTests
{
    [Fact]
    public void KeyboardCompositeCancelsOppositesAndNormalizesDiagonals()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction move = map.AddVector2("Move").BindWASD();
        map.Enable();
        source.NextFrame();
        source.SetKey(KeyCode.W);
        source.SetKey(KeyCode.D);

        Vector2 diagonal = move.ReadVector2();
        Assert.Equal(1f / MathF.Sqrt(2f), diagonal.X, 5);
        Assert.Equal(1f / MathF.Sqrt(2f), diagonal.Y, 5);

        source.NextFrame();
        source.SetKey(KeyCode.S);
        source.SetKey(KeyCode.A);
        Assert.Equal(Vector2.Zero, move.ReadVector2());
    }

    [Fact]
    public void MultipleMovementBindingsChooseStrongestInsteadOfAdding()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        var map = new InputActionMap("Gameplay", source);
        InputAction move = map.AddVector2("Move").BindWASD().BindLeftStick();
        map.Enable();
        source.NextFrame();
        source.SetKey(KeyCode.D);
        source.SetAxis(GamepadAxis.LeftX, 0.6f);
        Assert.Equal(new Vector2(1f, 0f), move.ReadVector2());

        source.NextFrame();
        source.SetKey(KeyCode.D, false);
        Assert.Equal(0.5f, move.ReadVector2().X, 5);
    }

    [Fact]
    public void StickUsesRadialDeadzoneAndPositiveUp()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        var map = new InputActionMap("Gameplay", source);
        InputAction move = map.AddVector2("Move").BindLeftStick(0.2f);
        source.SetAxis(GamepadAxis.LeftX, 0.1f);
        source.SetAxis(GamepadAxis.LeftY, -0.1f);
        map.Enable();
        Assert.Equal(Vector2.Zero, move.ReadVector2());

        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 0f);
        source.SetAxis(GamepadAxis.LeftY, -0.6f);
        Assert.Equal(0.5f, move.ReadVector2().Y, 5);

        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 1f);
        source.SetAxis(GamepadAxis.LeftY, -1f);
        Vector2 diagonal = move.ReadVector2();
        Assert.Equal(1f, MathF.Sqrt(diagonal.X * diagonal.X + diagonal.Y * diagonal.Y), 5);
        Assert.True(diagonal.X > 0f && diagonal.Y > 0f);
    }

    [Fact]
    public void DisconnectedGamepadDoesNotLeakButtonsOrTriggerPressure()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction fire = map.AddButton("Fire").Bind(GamepadButton.A);
        InputAction throttle = map.AddAxis("Throttle").Bind(InputBinding.Axis(GamepadAxis.RightTrigger, 0f));
        source.SetButton(GamepadButton.A);
        source.SetAxis(GamepadAxis.RightTrigger, 1f);
        map.Enable(suppressHeldButtons: false);
        Assert.False(fire.IsPressed);
        Assert.Equal(0f, throttle.ReadValue());

        source.NextFrame();
        source.ConnectedGamepads.Add(0);
        Assert.Equal(1f, throttle.ReadValue());
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0.5f)]
    [InlineData(1f, 1f)]
    public void TriggerRemapsTheNativeRange(float raw, float expected)
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        source.SetAxis(GamepadAxis.LeftTrigger, raw);
        var map = new InputActionMap("Gameplay", source);
        InputAction trigger = map.AddAxis("Trigger").Bind(InputBinding.Axis(GamepadAxis.LeftTrigger, 0f));
        map.Enable();
        Assert.Equal(expected, trigger.ReadValue(), 5);
    }

    [Fact]
    public void AnalogButtonUsesItsPressThreshold()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        var map = new InputActionMap("Gameplay", source);
        InputAction fire = map.AddButton("Fire").Bind(InputBinding.Axis(GamepadAxis.RightTrigger, 0f));
        fire.PressThreshold = 0.75f;
        map.Enable();
        map.Update();

        source.NextFrame();
        source.SetAxis(GamepadAxis.RightTrigger, 0f);
        Assert.False(fire.IsPressed);

        source.NextFrame();
        source.SetAxis(GamepadAxis.RightTrigger, 0.8f);
        Assert.True(fire.WasPressed);
    }

    [Fact]
    public void AxisCompositeCancelsOppositeKeys()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction steering = map.AddAxis("Steering").BindKeys(KeyCode.A, KeyCode.D);
        map.Enable();
        source.NextFrame();
        source.SetKey(KeyCode.A);
        Assert.Equal(-1f, steering.ReadValue());

        source.NextFrame();
        source.SetKey(KeyCode.D);
        Assert.Equal(0f, steering.ReadValue());
    }

    [Fact]
    public void MouseDeltaPreservesMagnitudeAndSupportsSensitivityAndInversion()
    {
        var source = new FakeInputSource { MouseDelta = new Vector2(12f, -8f) };
        var map = new InputActionMap("Gameplay", source);
        InputAction look = map.AddVector2("Look").BindMouseDelta(0.5f, true);
        map.Enable();
        Assert.Equal(new Vector2(6f, 4f), look.ReadVector2());

        source.NextFrame();
        Assert.Equal(Vector2.Zero, look.ReadVector2());
    }

    [Fact]
    public void DPadCompositeNormalizesDiagonalInput()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        source.SetButton(GamepadButton.DPadRight);
        source.SetButton(GamepadButton.DPadUp);
        var map = new InputActionMap("Gameplay", source);
        InputAction move = map.AddVector2("Move").BindDPad();
        map.Enable();

        Vector2 value = move.ReadVector2();
        Assert.Equal(1f / MathF.Sqrt(2f), value.X, 5);
        Assert.Equal(1f / MathF.Sqrt(2f), value.Y, 5);
    }

    [Fact]
    public void InvalidOrIncompatibleBindingsFailBeforeTheyAreAdded()
    {
        var map = new InputActionMap("Gameplay", new FakeInputSource());
        InputAction jump = map.AddButton("Jump");
        InputAction move = map.AddVector2("Move");
        Assert.ThrowsAny<ArgumentException>(() => jump.Bind(InputBinding.LeftStick()));
        Assert.ThrowsAny<ArgumentException>(() => move.Bind(KeyCode.Space));
        Assert.ThrowsAny<ArgumentException>(() => jump.Bind(InputBinding.Key((KeyCode)(-100))));
        Assert.ThrowsAny<ArgumentException>(() => move.Bind(InputBinding.LeftStick(1f)));
        Assert.ThrowsAny<ArgumentException>(() => move.Bind(InputBinding.MouseDelta(float.NaN)));
        Assert.Equal(0, jump.BindingCount);
        Assert.Equal(0, move.BindingCount);
    }

    [Fact]
    public void NonfiniteDeviceValuesDoNotPoisonActions()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        source.SetAxis(GamepadAxis.LeftX, float.NaN);
        source.SetAxis(GamepadAxis.LeftY, float.PositiveInfinity);
        var map = new InputActionMap("Gameplay", source);
        InputAction move = map.AddVector2("Move").BindLeftStick();
        map.Enable();
        Vector2 value = move.ReadVector2();
        Assert.True(float.IsFinite(value.X));
        Assert.True(float.IsFinite(value.Y));
    }
}

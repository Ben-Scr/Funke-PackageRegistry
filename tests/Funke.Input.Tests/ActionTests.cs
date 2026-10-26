using System;
using Funke;
using Funke.InputActions;
using Xunit;

namespace FunkeInput.Tests;

public sealed class ActionTests
{
    [Fact]
    public void DisabledMapsDoNotProduceInput()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        source.SetKey(KeyCode.Space);

        Assert.False(map.Enabled);
        Assert.False(jump.IsPressed);
        Assert.False(jump.WasPressed);
        Assert.Equal(0f, jump.ReadValue());
    }

    [Fact]
    public void ButtonEdgesRemainStableForEveryConsumerInTheFrame()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        map.Enable();
        map.Update();

        source.NextFrame();
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);
        Assert.True(jump.IsPressed);
        map.Update();
        Assert.True(jump.WasPressed);

        source.SetKey(KeyCode.Space, false);
        Assert.True(jump.IsPressed);
        Assert.False(jump.WasReleased);

        source.NextFrame();
        Assert.False(jump.WasPressed);
        Assert.True(jump.WasReleased);
        Assert.False(jump.IsPressed);

        source.NextFrame();
        Assert.False(jump.WasReleased);
    }

    [Fact]
    public void MultipleButtonBindingsShareOnePressAndRelease()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space).Bind(GamepadButton.A);
        map.Enable();
        map.Update();

        source.NextFrame();
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);

        source.NextFrame();
        source.SetButton(GamepadButton.A);
        Assert.False(jump.WasPressed);
        Assert.Equal(1f, jump.ReadValue());

        source.NextFrame();
        source.SetKey(KeyCode.Space, false);
        Assert.True(jump.IsPressed);
        Assert.False(jump.WasReleased);

        source.NextFrame();
        source.SetButton(GamepadButton.A, false);
        Assert.True(jump.WasReleased);
    }

    [Fact]
    public void EnablingContextSuppressesAlreadyHeldButtonsUntilRelease()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Menu", source);
        InputAction submit = map.AddButton("Submit").Bind(KeyCode.Enter);
        source.SetKey(KeyCode.Enter);
        map.Enable();
        Assert.False(submit.IsPressed);
        Assert.False(submit.WasPressed);

        source.NextFrame();
        Assert.False(submit.WasPressed);
        source.SetKey(KeyCode.Enter, false);
        source.NextFrame();
        Assert.False(submit.IsPressed);
        Assert.False(submit.WasReleased);

        source.SetKey(KeyCode.Enter);
        source.NextFrame();
        Assert.True(submit.WasPressed);
    }

    [Fact]
    public void HeldSuppressionCanBeDisabledExplicitly()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction fire = map.AddButton("Fire").Bind(MouseButton.Left);
        source.MouseButtons.Add(MouseButton.Left);
        map.Enable(suppressHeldButtons: false);
        Assert.True(fire.IsPressed);
        Assert.True(fire.WasPressed);
    }

    [Fact]
    public void DisablingMapClearsCachedStateImmediately()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        map.Enable();
        map.Update();
        source.NextFrame();
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);

        map.Disable();
        Assert.False(jump.IsPressed);
        Assert.False(jump.WasPressed);
        Assert.False(jump.WasReleased);
        Assert.Equal(0f, jump.ReadValue());
    }

    [Fact]
    public void OneActionReadSamplesOtherActionsInTheSameMap()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        InputAction pause = map.AddButton("Pause").Bind(KeyCode.Escape);
        map.Enable();
        map.Update();

        source.NextFrame();
        source.SetKey(KeyCode.Space);
        source.SetKey(KeyCode.Escape);
        Assert.True(jump.WasPressed);
        source.SetKey(KeyCode.Escape, false);
        Assert.True(pause.WasPressed);
    }

    [Fact]
    public void DifferentMapsCanSelectDifferentGamepads()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.UnionWith(new[] { 0, 1 });
        var first = new InputActionMap("Player1", source) { Gamepad = 0 };
        var second = new InputActionMap("Player2", source) { Gamepad = 1, KeyboardMouseEnabled = false };
        InputAction firstMove = first.AddVector2("Move").BindLeftStick();
        InputAction secondMove = second.AddVector2("Move").BindWASD().BindLeftStick();
        first.Enable();
        second.Enable();
        source.NextFrame();
        source.SetKey(KeyCode.D);
        source.SetAxis(GamepadAxis.LeftX, 1f, 0);
        source.SetAxis(GamepadAxis.LeftY, -1f, 1);

        Assert.Equal(new Vector2(1f, 0f), firstMove.ReadVector2());
        Assert.Equal(new Vector2(0f, 1f), secondMove.ReadVector2());
    }

    [Fact]
    public void ChangingGamepadDoesNotKeepThePreviousControllerValue()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.UnionWith(new[] { 0, 1 });
        var map = new InputActionMap("Player", source);
        InputAction move = map.AddVector2("Move").BindLeftStick();
        source.SetAxis(GamepadAxis.LeftX, 1f, 0);
        map.Enable();
        Assert.Equal(1f, move.ReadVector2().X);

        map.Gamepad = 1;
        Assert.Equal(Vector2.Zero, move.ReadVector2());
    }

    [Fact]
    public void OverridesCanBeResetWithoutLosingAuthoredDefaults()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space).Bind(GamepadButton.A);

        jump.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        Assert.True(jump.HasBindingOverride(0));
        Assert.Equal((int)KeyCode.J, jump.Bindings[0].Code);
        Assert.Equal((int)KeyCode.Space, jump.GetDefaultBinding(0).Code);
        Assert.Equal(2, jump.BindingCount);

        jump.ResetBinding(0);
        Assert.False(jump.HasBindingOverride(0));
        Assert.Equal((int)KeyCode.Space, jump.Bindings[0].Code);
        Assert.Equal((int)GamepadButton.A, jump.Bindings[1].Code);
    }

    [Fact]
    public void NamesAndDeviceSelectionsAreValidated()
    {
        var map = new InputActionMap("Gameplay", new FakeInputSource());
        InputAction jump = map.AddButton("Jump");
        Assert.Same(jump, map["Jump"]);
        Assert.Same(jump, map.FindAction("Jump"));
        Assert.Null(map.FindAction("Missing"));
        Assert.ThrowsAny<ArgumentException>(() => map.AddAxis("Jump"));
        Assert.ThrowsAny<ArgumentException>(() => map.AddButton(" "));
        Assert.ThrowsAny<ArgumentException>(() => map.Gamepad = -1);
        Assert.ThrowsAny<ArgumentException>(() => map.Gamepad = 4);
    }

    [Fact]
    public void FreshPressAfterProgrammaticOverrideIsNotSuppressed()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        map.Enable();
        jump.OverrideBinding(0, InputBinding.Key(KeyCode.J));

        source.NextFrame();
        source.SetKey(KeyCode.J);
        Assert.True(jump.WasPressed);
    }

    [Fact]
    public void FreshPressAfterResettingDefaultsIsNotSuppressed()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        jump.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        map.Enable();
        map.ResetBindings();

        source.NextFrame();
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);
        Assert.False(jump.HasBindingOverride(0));
    }

    [Fact]
    public void FreshPressAfterChangingControllerIsNotSuppressed()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.UnionWith(new[] { 0, 1 });
        var map = new InputActionMap("Player", source);
        InputAction jump = map.AddButton("Jump").Bind(GamepadButton.A);
        map.Enable();
        map.Gamepad = 1;

        source.NextFrame();
        source.SetButton(GamepadButton.A, gamepad: 1);
        Assert.True(jump.WasPressed);
    }

    [Fact]
    public void FreshPressAfterEnablingKeyboardInputIsNotSuppressed()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Player", source) { KeyboardMouseEnabled = false };
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        map.Enable();
        map.KeyboardMouseEnabled = true;

        source.NextFrame();
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);
    }
}

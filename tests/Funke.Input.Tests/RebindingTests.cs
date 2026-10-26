using System;
using Funke;
using Funke.InputActions;
using Xunit;

namespace FunkeInput.Tests;

public sealed class RebindingTests
{
    [Fact]
    public void FreshKeyPressReplacesOnlyTheSelectedBinding()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space).Bind(GamepadButton.A);
        map.Enable();
        RebindOperation operation = jump.Rebind();

        source.NextFrame();
        source.SetKey(KeyCode.J);
        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.True(operation.IsFinished);
        Assert.Equal(InputBinding.Key(KeyCode.J), operation.Result);
        Assert.Equal(InputBinding.Key(KeyCode.J), jump.Bindings[0]);
        Assert.Equal(InputBinding.Button(GamepadButton.A), jump.Bindings[1]);
        Assert.Equal(InputBinding.Key(KeyCode.Space), jump.GetDefaultBinding(0));
    }

    [Fact]
    public void ActivationClickAndHeldKeyAreIgnoredUntilReleased()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction fire = map.AddButton("Fire").Bind(KeyCode.Space);
        source.SetKey(KeyCode.K);
        source.MouseButtons.Add(MouseButton.Left);
        RebindOperation operation = fire.Rebind();

        source.NextFrame();
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.SetKey(KeyCode.K, false);
        source.MouseButtons.Clear();
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.MouseButtons.Add(MouseButton.Left);

        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.Equal(InputBinding.Mouse(MouseButton.Left), fire.Bindings[0]);
    }

    [Fact]
    public void NoControlIsCapturedInTheActivationFrame()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        RebindOperation operation = jump.Rebind();
        source.SetKey(KeyCode.J);

        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        Assert.Equal(RebindStatus.Completed, operation.Status);
    }

    [Fact]
    public void EscapeCancelsWithoutReplacingTheBinding()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        RebindOperation operation = jump.Rebind();
        source.NextFrame();
        source.SetKey(KeyCode.Escape);

        Assert.Equal(RebindStatus.Canceled, operation.Status);
        Assert.True(operation.IsFinished);
        Assert.Null(operation.Result);
        Assert.Equal(InputBinding.Key(KeyCode.Space), jump.Bindings[0]);
    }

    [Fact]
    public void DisablingCancelKeyAllowsBindingEscape()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Menu", source);
        InputAction close = map.AddButton("Close").Bind(KeyCode.Backspace);
        RebindOperation operation = close.Rebind(options: new RebindOptions { CancelKey = null });
        source.NextFrame();
        source.SetKey(KeyCode.Escape);

        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.Equal(InputBinding.Key(KeyCode.Escape), close.Bindings[0]);
    }

    [Fact]
    public void TimeoutUsesUnscaledSourceTimeAndWorksOnDisabledMap()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        RebindOperation operation = jump.Rebind(options: new RebindOptions { TimeoutSeconds = 0.5 });

        source.NextFrame(0.25);
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame(0.25);
        Assert.Equal(RebindStatus.TimedOut, operation.Status);
        Assert.False(map.Enabled);
        Assert.Equal(InputBinding.Key(KeyCode.Space), jump.Bindings[0]);
    }

    [Fact]
    public void CapturedInputDoesNotAlsoActivateGameplay()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        InputAction other = map.AddButton("Other").Bind(KeyCode.J);
        map.Enable();
        RebindOperation operation = jump.Rebind();

        source.NextFrame();
        source.SetKey(KeyCode.J);
        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.False(jump.IsPressed);
        Assert.False(other.IsPressed);
        source.NextFrame();
        Assert.False(jump.WasPressed);
        Assert.False(other.WasPressed);

        source.NextFrame();
        source.SetKey(KeyCode.J, false);
        map.Update();
        source.NextFrame();
        source.SetKey(KeyCode.J);
        Assert.True(jump.WasPressed);
        Assert.True(other.WasPressed);
    }

    [Fact]
    public void CompositeDirectionRebindingPreservesTheOtherDirections()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction move = map.AddVector2("Move").BindWASD().BindLeftStick();
        RebindOperation operation = move.Rebind(part: InputBindingPart.Up);
        source.NextFrame();
        source.SetKey(KeyCode.Z);

        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.Equal(InputBinding.WASD() with { Up = (int)KeyCode.Z }, move.Bindings[0]);
        Assert.Equal(InputBinding.LeftStick(), move.Bindings[1]);
    }

    [Fact]
    public void WrongCompositePartsAndInvalidOptionsAreRejected()
    {
        var map = new InputActionMap("Gameplay", new FakeInputSource());
        InputAction move = map.AddVector2("Move").BindWASD();
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        Assert.ThrowsAny<ArgumentException>(() => move.Rebind());
        Assert.ThrowsAny<ArgumentException>(() => move.Rebind(part: InputBindingPart.Negative));
        Assert.ThrowsAny<ArgumentException>(() => jump.Rebind(part: InputBindingPart.Up));
        Assert.ThrowsAny<ArgumentException>(() => jump.Rebind(options: new RebindOptions { TimeoutSeconds = double.NaN }));
        Assert.ThrowsAny<ArgumentException>(() => jump.Rebind(options: new RebindOptions { AxisThreshold = 0f }));
    }

    [Fact]
    public void RebindingDoesNotEnableADisabledGameplayContext()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        RebindOperation operation = jump.Rebind();
        source.NextFrame();
        source.SetKey(KeyCode.J);

        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.False(map.Enabled);
        Assert.False(jump.IsPressed);
        Assert.True(jump.HasBindingOverride(0));
    }

    [Fact]
    public void RebindingCapturesOnlyTheSelectedGamepad()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.UnionWith(new[] { 0, 1 });
        var map = new InputActionMap("Player2", source) { Gamepad = 1 };
        InputAction jump = map.AddButton("Jump").Bind(GamepadButton.A);
        RebindOperation operation = jump.Rebind(options: new RebindOptions { Devices = RebindDevices.Gamepad });

        source.NextFrame();
        source.SetButton(GamepadButton.X, gamepad: 0);
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.SetButton(GamepadButton.Y, gamepad: 1);
        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.Equal(InputBinding.Button(GamepadButton.Y), jump.Bindings[0]);
    }

    [Fact]
    public void StartingAnotherRebindCancelsThePreviousOperation()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        InputAction fire = map.AddButton("Fire").Bind(KeyCode.F);
        RebindOperation first = jump.Rebind();
        RebindOperation second = fire.Rebind();
        Assert.Equal(RebindStatus.Canceled, first.Status);

        source.NextFrame();
        source.SetKey(KeyCode.J);
        Assert.Equal(RebindStatus.Completed, second.Status);
        Assert.Equal(InputBinding.Key(KeyCode.Space), jump.Bindings[0]);
        Assert.Equal(InputBinding.Key(KeyCode.J), fire.Bindings[0]);
    }

    [Fact]
    public void DeviceFiltersIgnoreOtherDevices()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        RebindOperation operation = jump.Rebind(options: new RebindOptions { Devices = RebindDevices.Gamepad });
        source.NextFrame();
        source.SetKey(KeyCode.J);
        source.MouseButtons.Add(MouseButton.Left);
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.SetButton(GamepadButton.X);
        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.Equal(InputBinding.Button(GamepadButton.X), jump.Bindings[0]);
    }

    [Fact]
    public void ChangingControllerCancelsAnActiveRebind()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Player", source);
        InputAction jump = map.AddButton("Jump").Bind(GamepadButton.A);
        RebindOperation operation = jump.Rebind();
        map.Gamepad = 1;
        Assert.Equal(RebindStatus.Canceled, operation.Status);
    }

    [Fact]
    public void CapturedNegativeAxisDoesNotFireForTheOppositeDirection()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        var map = new InputActionMap("Gameplay", source);
        InputAction dodge = map.AddButton("DodgeLeft").Bind(GamepadButton.LeftBumper);
        map.Enable();
        RebindOperation operation = dodge.Rebind(options: new RebindOptions { Devices = RebindDevices.Gamepad });
        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, -1f);
        Assert.Equal(RebindStatus.Completed, operation.Status);

        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 0f);
        map.Update();
        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 1f);
        Assert.False(dodge.IsPressed);
        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, -1f);
        Assert.True(dodge.WasPressed);
    }

    [Fact]
    public void FreshPressAfterEscapeCancellationIsNotSuppressed()
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        map.Enable();
        RebindOperation operation = jump.Rebind();
        source.NextFrame();
        source.SetKey(KeyCode.Escape);
        Assert.Equal(RebindStatus.Canceled, operation.Status);

        source.NextFrame();
        source.SetKey(KeyCode.Escape, false);
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshPressAfterManualCancellationIsNotSuppressed(bool dispose)
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        map.Enable();
        RebindOperation operation = jump.Rebind();
        if (dispose) operation.Dispose();
        else operation.Cancel();

        source.NextFrame();
        source.SetKey(KeyCode.Space);
        Assert.True(jump.WasPressed);
        Assert.Equal(RebindStatus.Canceled, operation.Status);
    }

    [Fact]
    public void LowAxisThresholdStillRequiresPreviouslyHeldAxisToReturnToNeutral()
    {
        var source = new FakeInputSource();
        source.ConnectedGamepads.Add(0);
        source.SetAxis(GamepadAxis.LeftX, 0.15f);
        var map = new InputActionMap("Gameplay", source);
        InputAction steer = map.AddAxis("Steer").Bind(InputBinding.Axis(GamepadAxis.RightX));
        RebindOperation operation = steer.Rebind(options: new RebindOptions
        {
            Devices = RebindDevices.Gamepad,
            AxisThreshold = 0.1f,
        });

        source.NextFrame();
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 0.08f);
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 0.15f);
        Assert.Equal(RebindStatus.Waiting, operation.Status);

        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 0.04f);
        Assert.Equal(RebindStatus.Waiting, operation.Status);
        source.NextFrame();
        source.SetAxis(GamepadAxis.LeftX, 0.15f);
        Assert.Equal(RebindStatus.Completed, operation.Status);
        Assert.Equal(InputBinding.Axis(GamepadAxis.LeftX), steer.Bindings[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ProgrammaticBindingChangesCancelCaptureAndCannotBeOverwrittenByLaterInput(int change)
    {
        var source = new FakeInputSource();
        var map = new InputActionMap("Gameplay", source);
        InputAction jump = map.AddButton("Jump").Bind(KeyCode.Space);
        jump.OverrideBinding(0, InputBinding.Key(KeyCode.J));
        map.Enable();
        RebindOperation operation = jump.Rebind();

        if (change == 0) jump.OverrideBinding(0, InputBinding.Key(KeyCode.K));
        else if (change == 1) jump.ResetBindings();
        else map.ResetBindings();
        KeyCode expected = change == 0 ? KeyCode.K : KeyCode.Space;
        Assert.Equal(RebindStatus.Canceled, operation.Status);

        source.NextFrame();
        source.SetKey(KeyCode.F);
        map.Update();
        Assert.Equal(InputBinding.Key(expected), jump.Bindings[0]);
        Assert.False(jump.IsPressed);

        source.NextFrame();
        source.SetKey(KeyCode.F, false);
        source.SetKey(expected);
        Assert.True(jump.WasPressed);
        Assert.Equal(InputBinding.Key(expected), jump.Bindings[0]);
    }
}

# Funke.Input

Optional input actions for Funke: named buttons, axes and movement/look vectors with multiple bindings, action maps, controller selection and saved rebinding. The package is pure C# and uses the engine's existing input API. It requires .NET 10 and Funke 2026.1.0 or newer; no native library or engine module is required.

The package is named `Funke.Input`, but its namespace is **`Funke.InputActions`** to avoid colliding with the existing `Funke.Input` class.

## Install

In the Editor's **Package Manager**, refresh the registry, find **Funke.Input**, and click **Install**. The editor downloads the package, verifies its checksum, and builds it for your project.

For an offline installation, download the ZIP from the [Funke.Input 0.1.0 release](https://github.com/Ben-Scr/Funke-PackageRegistry/releases/tag/Funke.Input-0.1.0).

Extract the ZIP, open the Editor's **Package Manager**, choose **+ → Install package from disk**, and select the extracted `Funke.Input` folder containing `funke-package.lua`. The editor copies it into the project, enables it, and builds its managed library.

For a manual installation:

Copy the `Funke.Input` folder into your game's `Packages/` directory. An archive must be extracted so the manifest is at `Packages/Funke.Input/funke-package.lua`. Add the package to the existing list in `project.json`:

```json
"packages": ["Funke.Input"]
```

Keep any packages already in that list. Reopen the project in the Editor to generate and build its package workspace. The game can then import `Funke.InputActions`. Release archives contain the package sources; the editor builds the managed library against your engine installation.

## Start with actions

Create the map and its defaults once, usually in `OnStart`:

```csharp
using Funke;
using Funke.InputActions;

var gameplay = new InputActionMap("Gameplay");
var move = gameplay.AddVector2("Move").BindWASD().BindArrows().BindLeftStick();
var jump = gameplay.AddButton("Jump").Bind(KeyCode.Space).Bind(GamepadButton.A);
var fire = gameplay.AddButton("Fire").Bind(MouseButton.Left)
    .Bind(InputBinding.Axis(GamepadAxis.RightTrigger));

gameplay.LoadBindingOverrides();
gameplay.Enable();
```

Read the actions during `OnUpdate`:

```csharp
Vector2 movement = move.ReadVector2();
bool jumping = jump.IsPressed;
if (jump.WasPressed) { /* Queue one jump. */ }
if (fire.WasReleased) { /* Finish charging. */ }
```

Reading any action updates the entire map once per input frame. You may instead call `gameplay.Update()` at the start of `OnUpdate`; it also advances rebinding when no action is read. There is no global update driver and no event subscription required.

Poll every rendered frame. `WasPressed` and `WasReleased` remain true for that frame and are not consumed by the first reader. They are not an event history: an unpolled press/release between frames is not replayed. For physics, queue an edge in `OnUpdate` and consume it once in `OnFixedUpdate`; reading edges in every fixed tick can miss or repeat a press.

Dispose maps when their owning script is destroyed. Default-source maps are disabled when Editor Play mode stops.

See [`Examples/PlayerInputExample.cs`](Examples/PlayerInputExample.cs) for a complete script with movement, a queued jump and a rebinding entry point. Copy it into your game's `Assets/Scripts/` to use it; examples are excluded from the package's `Source/` compilation glob.

## Binding choices

| Action | Bindings |
|---|---|
| `AddButton("Jump")` | `.Bind(KeyCode.Space)`, `.Bind(MouseButton.Left)`, `.Bind(GamepadButton.A)`, `.Bind(InputBinding.Axis(GamepadAxis.RightTrigger))` |
| `AddAxis("Steer")` | `.BindKeys(KeyCode.A, KeyCode.D)`, `.Bind(InputBinding.Axis(GamepadAxis.LeftX))` |
| `AddAxis("Zoom")` | `.BindMouseWheel()` |
| `AddVector2("Move")` | `.BindWASD()`, `.BindArrows()`, `.Bind2D(up, down, left, right)`, `.BindLeftStick()`, `.BindDPad()` |
| `AddVector2("Look")` | `.BindRightStick()`, `.BindMouseDelta(sensitivity: 0.1f, invertY: true)` |

For buttons and axes use `ReadValue()`; for vector actions use `ReadVector2()`. A button returns either `0` or `1`, with a configurable `PressThreshold` of `0.5` by default. An axis keeps its signed value. When several bindings are active, the strongest magnitude wins; ties use the first binding. Inputs are not added together.

Keyboard movement and D-pad diagonals are normalized. Opposing keys in one composite cancel. Sticks use radial deadzones, have positive Y upward and preserve analog magnitude. A deadzone of `0.2` is the default; `.BindLeftStick(0.15f)` overrides it. Triggers are remapped to `0..1` before applying their deadzone. Disconnected controllers contribute zero.

Mouse delta is a pixel delta, with Y downward unless inverted. Wheel and mouse-delta values are not normalized. Mouse and stick look bindings therefore have different units: apply suitable sensitivity in your camera code, or use separate actions if you need separate mouse/stick processing.

Bindings are immutable records. Configure processors or replace a binding explicitly:

```csharp
var throttle = gameplay.AddAxis("Throttle")
    .Bind(InputBinding.Axis(GamepadAxis.RightTrigger, deadzone: 0.05f));

move.OverrideBinding(0, InputBinding.WASD() with { InvertY = true });
string label = jump.Bindings[0].DisplayName;
```

## Gameplay and menu contexts

Maps start disabled. Define one map per independently enabled context:

```csharp
gameplay.Disable();
menu.Enable();
// On leaving the menu:
menu.Disable();
gameplay.Enable();
```

Enabling a map suppresses already-held buttons until they are released, so the key used to close a menu cannot also trigger a jump. Axes remain responsive. Use `Enable(suppressHeldButtons: false)` only when held buttons should activate immediately. Disabling a map clears its values and edges; it does not synthesize release events.

Enabled maps are independent. They do not consume shared keys or impose priority on each other. Disable the gameplay map yourself when menus should block gameplay. Separate maps may intentionally share a key, such as a global screenshot action and gameplay input.

## Local players

Each map uses controller slot `0` by default; supported slots are `0..3`:

```csharp
var playerTwo = new InputActionMap("PlayerTwo")
{
    Gamepad = 1,
    KeyboardMouseEnabled = false
};
var moveTwo = playerTwo.AddVector2("Move").BindLeftStick().BindDPad();
playerTwo.Enable();
```

Slots follow the engine's controller assignment. The package does not provide join-on-press or stable device identity. Keyboard/mouse sharing is explicit through `KeyboardMouseEnabled`.

## Rebinding

Start a capture when the player clicks a settings button:

```csharp
RebindOperation capture = jump.Rebind(bindingIndex: 0);
```

Continue calling the map's `Update()` every frame, or poll `capture.IsFinished` / `capture.Status`. A completed operation has already replaced the binding:

```csharp
if (capture.IsFinished)
{
    if (capture.Status == RebindStatus.Completed)
        gameplay.SaveBindingOverrides();
    // Refresh the UI from jump.Bindings[0].DisplayName, then discard capture.
}
```

The whole map is suppressed during capture and through its completion frame. Controls already held at the start must be released before they can be captured. Rebinding works with a disabled map, which is useful while a separate menu map is active. Other maps remain independent; disable any that must not react to the captured key.

`Escape` cancels by default; the default timeout is 10 seconds of unscaled time. You can also call `capture.Cancel()` or configure the operation:

```csharp
var capture = jump.Rebind(options: new RebindOptions
{
    TimeoutSeconds = 20,
    CancelKey = KeyCode.Escape,
    Devices = RebindDevices.KeyboardMouse,
    AxisThreshold = 0.75f
});
```

The possible statuses are `Waiting`, `Completed`, `Canceled` and `TimedOut`. Set `CancelKey = null` when Escape itself should be bindable. Cancellation and timeout preserve the old binding. Device selection accepts `Any`, `KeyboardMouse` or `Gamepad`; controller capture uses the map's selected slot.

Whole-binding capture supports button/axis actions. Rebind one direction of a keyboard composite using its part:

```csharp
var forwardCapture = move.Rebind(0, InputBindingPart.Up);
var positiveCapture = steer.Rebind(0, InputBindingPart.Positive);
```

`Up`, `Down`, `Left` and `Right` address a `Keys2D` binding; `Negative` and `Positive` address a `KeysAxis` binding. Whole-vector replacement is explicit, for example `move.OverrideBinding(1, InputBinding.RightStick())`. There is no automatic conflict rejection: games may deliberately share controls, so settings UI should apply its own conflict policy.

## Saved profiles and defaults

Define all actions and default bindings before loading a profile:

```csharp
gameplay.LoadBindingOverrides("player-one");
gameplay.SaveBindingOverrides("player-one");

jump.ResetBinding(0);
jump.ResetBindings();
gameplay.ResetBindings();
gameplay.SaveBindingOverrides("player-one");
```

Saving writes only overrides to `PlayerPrefs` and saves immediately. Profiles are separated by map name and profile name. `LoadBindingOverrides` returns `false` when no saved profile exists or the profile is invalid; current bindings remain unchanged in either case. `DeleteSavedBindingOverrides(profile)` removes the saved profile without resetting live bindings.

Map names, action names and binding indices identify saved overrides. Keep them stable across releases. Removed actions and removed binding slots are ignored; changing the meaning or order of an existing slot needs a migration in your game.

For your own save system or cloud sync:

```csharp
string json = gameplay.SaveBindingOverridesToJson();
if (!gameplay.TryLoadBindingOverridesFromJson(json, out string? error))
    Log.Warn(error ?? "Invalid input profile.");
```

Loading validates the whole profile before applying it. Successful loads replace existing overrides; they do not merge them. Unsupported schemas, incompatible bindings and malformed profiles are rejected.

Loading a valid profile, overriding a binding, or resetting bindings cancels a running capture. Invalid profile data leaves both the bindings and an active capture unchanged.

## Custom input sources and tests

`new InputActionMap("Gameplay", source)` accepts an `IInputSource`, so tests and adapters can supply input without invoking native engine bindings. Its frame ID must advance once per sample. Raw controller axes use the engine convention: sticks in `-1..1` with Y downward, triggers from `-1` released to `+1` pressed. Access maps from the game thread.

The package does not add a visual action-map editor, action callbacks, hold/tap interactions, chord bindings or touch gesture recognition. Touch UI can be adapted through a custom source; the existing engine touch API remains available alongside actions.

Run the package tests from the registry checkout with `dotnet test tests/Funke.Input.Tests/Funke.Input.Tests.csproj -c Release`. They reference the neighboring engine's Release `Funke-ScriptCore.dll`; set `-p:FunkeEngineRoot=<engine path>` for a different checkout. The example is compiled with the tests.

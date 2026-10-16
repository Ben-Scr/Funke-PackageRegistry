# Funke.Tween

Tweens in the style of DOTween: move, rotate, scale, fade and recolor anything over time with easing, chain them into
sequences, loop them, and react with callbacks. One line replaces the hand-written `m_T += Time.DeltaTime / Seconds`
scripts for pop-ins, screen fades, hit flashes, bobbing pickups and camera shakes.

## Install

Package Manager → **Funke.Tween** → Install, or `funke package add Funke.Tween`. C# only, no setup: tweens update
themselves every frame (no GlobalSystem entry needed). Works with or without the 2D/3D modules.

## Use

```csharp
using Funke;
using Funke.Tween;
using Funke.UI;

public class Pickup : EntityScript
{
    public override void OnStart()
    {
        Transform.MoveBy(new Vector2(0f, 0.3f), 0.8f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);  // bob
        Transform.RotateBy(360f, 2f).SetEase(Ease.Linear).SetLoops(-1, LoopType.Incremental);             // spin
    }
}

transform.MoveTo(new Vector2(4f, 0f), 0.5f).SetEase(Ease.OutBack);           // world position
panel.ScaleTo(1f, 0.25f).From(0.8f).SetEase(Ease.OutBack).SetUnscaledTime(); // UI pop-in, also while paused
overlay.FadeTo(1f, 0.4f).SetUnscaledTime().OnComplete(() => SceneManager.LoadScene("Level2"));  // screen fade
sprite.ColorTo(Color.White, 0.06f).SetLoops(2, LoopType.Yoyo);              // hit flash
buttonRect.PunchScale(0.15f, 0.3f);                                          // click feedback
Camera2D.Main!.Shake(0.4f, 0.3f);                                            // camera shake
Tween.Delay(1.5f, SpawnWave);                                                 // delayed call
```

Anything else with a getter and a setter: `Tween.To(() => m_Speed, v => m_Speed = v, 10f, 2f)` (float, Vector2,
Vector3, Vector4, Color), or `Tween.Value(0f, 100f, 1f, v => label.Text = $"{v:0}")` for a value that only lives in the
callback.

### Shortcuts

| Component | Tweens |
|---|---|
| `Transform2D` | `MoveTo` (world), `MoveToX/Y`, `MoveBy`, `LocalMoveTo`, `RotateTo` (world, degrees), `LocalRotateTo`, `RotateBy`, `ScaleTo` (local scale, `Vector2` or uniform), `PunchPosition/Rotation/Scale`, `ShakePosition/Rotation/Scale` |
| `Transform3D` | `MoveTo`, `MoveToX/Y/Z`, `MoveBy`, `RotateTo` / `RotateBy` (Euler degrees), `ScaleTo`, `Punch…`, `Shake…` |
| `RectTransform` | `MoveTo` / `MoveBy` (anchored position, UI pixels), `SizeTo`, `RotateTo`, `ScaleTo`, `PunchPosition/Rotation/Scale`, `ShakePosition/Rotation` |
| `SpriteRenderer`, `Image`, `TextRenderer`, `MeshRenderer` | `ColorTo`, `FadeTo` (alpha only); `TextRenderer.FontSizeTo` |
| Lights | `ColorTo`, `IntensityTo` |
| `Camera2D` | `ZoomTo`, `OrthographicSizeTo`, `ClearColorTo`, `Shake` |
| `Camera3D` | `FieldOfViewTo`, `Shake` |
| `AudioSource` | `VolumeTo`, `PitchTo` |

3D rotations interpolate the Euler angles per axis, so `RotateTo(new Vector3(0f, 360f, 0f), 1f)` is a full turn.

### Settings

Chain them after creating a tween; each returns the same handle.

| | |
|---|---|
| `SetEase(Ease)` | Penner set: `Linear`, `In/Out/InOut` × `Sine Quad Cubic Quart Quint Expo Circ Back Elastic Bounce`. Default `OutQuad` (`Tween.DefaultEase`). `SetEase(Ease.OutBack, overshoot)`, `SetEase(Ease.OutElastic, amplitude, period)` |
| `SetEase(t => …)` / `SetEase(graph)` | your own curve: a function, or a `Graph` script field drawn in the Inspector (from (0, 0) to (1, 1)) |
| `SetDelay(s)` | start values are read when the delay ends, so chained tweens continue from where the last one stopped |
| `SetLoops(n, LoopType)` | `-1` = endless; `Restart`, `Yoyo` (each direction is one loop), `Incremental` (adds the change again every loop) |
| `SetUnscaledTime()` | ignore `Time.TimeScale` (pause menus, fades while the game is frozen) |
| `SetRelative()` | the end value is an offset (`MoveBy`, `RotateBy` do this) |
| `From()` / `From(value)` | play from the end value (or `value`) to the current one; applied immediately, so there is no one-frame flicker. `From(0f)` on a scale tween is uniform. Chain it last |
| `SetId(obj)`, `SetLink(entity)` | find tweens with `Tween.Kill(id)`; kill the tween when an entity is destroyed |
| `SetTimeScale(x)`, `SetAutoKill(false)` | per-tween speed; keep a finished tween for `Restart()` |
| `OnStart`, `OnUpdate`, `OnStepComplete`, `OnComplete`, `OnKill` | callbacks (exceptions are logged, the tween keeps running) |

Control: `Pause()`, `Play()`, `TogglePause()`, `Restart()`, `Rewind()`, `Goto(seconds)`, `Complete()`, `Kill(complete)`;
state: `IsActive`, `IsPlaying`, `IsComplete`, `Progress`, `Elapsed`, `Duration`, `CompletedLoops`. In coroutines:
`await tween.WaitForCompletion();`.

### Sequences

```csharp
Tween.Sequence()
    .Append(toast.MoveTo(new Vector2(0f, -80f), 0.3f).SetEase(Ease.OutBack))   // slide in
    .Join(background.FadeTo(0.9f, 0.3f))                                       // at the same time
    .AppendInterval(2f)
    .Append(background.FadeTo(0f, 0.4f))
    .Join(label.FadeTo(0f, 0.4f))
    .AppendCallback(() => toastEntity.IsEnabled = false)
    .SetUnscaledTime();
```

`Append` (after everything so far), `Prepend`, `Join` (with the previous item), `Insert(time, …)`, `AppendInterval`,
`PrependInterval`, `AppendCallback`, `InsertCallback`. A sequence is a tween itself: it loops (`Yoyo` plays it
backwards), eases, delays and nests. Build it right away; it starts next frame and can't be changed afterwards.
Tweens inside a sequence are controlled through the sequence.

### Killing and lifetime

- Component shortcuts are linked to their entity: destroying the entity (or removing the component) kills the tween
  quietly. For `Tween.To` on your own fields, add `.SetLink(Entity)`.
- `transform.KillTweens()`, `entity.KillTweens()`, `Tween.Kill(idOrTarget)`, `Tween.KillAll()`; also `Tween.Complete`,
  `Pause`, `Play`, `IsTweening` by id or target.
- Handles are values. After a tween finished or was killed its handle reads `IsActive == false` and every call on it does
  nothing, so keeping one in a field and calling `m_Fade.Kill()` before starting a new fade is always safe.
- Stopping Play mode in the editor drops every tween without callbacks.

### Shakes and punches

`Shake…` jitters randomly around the value and settles back; `Punch…` kicks towards a vector and springs back
(`elasticity` 0 = no swing past the start). Both are applied as an offset, so the target may keep moving (another tween,
physics) and ends where it would be without the effect; killing one midway removes its offset. 2D shakes work on the
local position, so a camera parented to a follow rig shakes cleanly. A script that sets the camera position itself
every frame overwrites the shake, because tweens update before scripts.

## Timing and performance

- Tweens start on the next frame and update once per frame, before the scripts' `OnUpdate`, with `Time.DeltaTime`
  (scaled) or `Time.UnscaledDeltaTime`, times `Tween.GlobalTimeScale` and the tween's own time scale.
- Tween objects are pooled and the handles are structs: updating tweens allocates nothing per frame, and creating
  component tweens allocates nothing once the pools are warm (`Tween.Prewarm()` fills them up front). Lambdas you pass
  to `Tween.To` or callbacks allocate once when created, as usual. (The engine's frame scheduler that wakes the
  package each frame allocates a few bytes per frame on its side, tween count independent.)
- `Tween.AutoUpdate = false` stops the automatic update; call `Tween.Update(deltaTime, unscaledDeltaTime)` yourself, e.g.
  from a fixed-step simulation.

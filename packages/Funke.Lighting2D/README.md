# Funke.Lighting2D

Optional 2D world lighting for sprites, tilemaps, terrain, particles and world text. Colored point lights illuminate the world with distance falloff; rotated rectangular occluders cast hard shadows. Ambient lights control the base illumination. Screen-space UI stays unaffected.

The package owns all lighting components, shader code, serialization and C# bindings. The engine supplies a general `Renderer2D::RegisterSceneEffect` hook and backend-neutral `FullscreenEffect` shader pass. **Requires an engine build containing those APIs**; older 2026.1 beta binaries must be rebuilt/updated first.

## Install

Open **Package Manager > Search Packages**, click **Refresh registry**, search for `Funke.Lighting2D` and click **Install**. The editor downloads and verifies the archive, enables the required `Funke.2D` module and builds the native and managed package layers against the active engine. A native C++ toolchain is required. The package is source-first and its native ABI sidecar must match that engine.

For manual installation, copy this folder to `<project>/Packages/Funke.Lighting2D`, add `"Funke.Lighting2D"` to the project's `packages` list and enable `"Funke.2D"` in `modules`. The ZIP from [GitHub Releases](https://github.com/Ben-Scr/Funke-PackageRegistry/releases/tag/Funke.Lighting2D-0.1.0) can also be installed through the Package Manager's file installation workflow.

## In the editor

Use **Create > 2D Entity > Lighting** or **Add Component > Lighting 2D**:

| Component | Controls |
|---|---|
| **Light 2D** | Enabled, Color (including strength through alpha), Intensity, Radius in world units, Falloff exponent, Cast Shadows |
| **Ambient Light 2D** | Enabled, Color, Intensity; enabled ambient components add together |
| **Shadow Occluder 2D** | Enabled, local Size, local Offset, Opacity |

Lights use their entity's world position. Radius is in world units and does not inherit transform scale. Occluders inherit translation, rotation and scale, including mirrored transforms. Selected lights show their radius; selected occluders show their outline. Add a sprite separately if the occluder itself should be visible.

Lighting is visible in Editor View, Game View and exported players. Properties survive scene/prefab save/load, duplication and Play/Stop. Disabled entities and disabled components are excluded. With point lights but no ambient component, the base is neutral 10% light. Add an ambient component with intensity zero for complete darkness. With no enabled light or ambient component, the pass is bypassed.

## C#

```csharp
using Funke;
using Funke.Lighting2D;

public sealed class Torch : EntityScript
{
    private Light2D? m_Light;
    public override void OnStart()
    {
        m_Light = GetComponent<Light2D>() ?? Entity.AddComponent<Light2D>();
        if (m_Light == null) return;
        m_Light.Color = new Color(1, 0.65f, 0.25f, 1);
        m_Light.Intensity = 2;
        m_Light.Radius = 6;
        m_Light.CastShadows = true;
    }
}
```

`AmbientLight2D` and `ShadowOccluder2D` expose the same fields as the inspector. Getters and setters use native component state directly, so changes take effect in the next rendered frame. Non-finite values throw `ArgumentOutOfRangeException`; numeric values are clamped to the inspector ranges. Accessing a removed component throws `InvalidOperationException`.

## Rendering and limits

The effect runs once on the combined world image of the rendered scenes, before FXAA, bloom and other camera effects. The actual view-projection matrix is used, including the editor camera. Its scene sample uses nearest filtering at the scene resolution and leaves alpha unchanged. Render scale and the configured upscaler still apply normally. Wireframe bypasses lighting.

This first version lights one 2D plane: every world sprite/text receives the same field. There are no normal maps, per-layer light masks, soft shadows, polygon occluders or emissive exclusions. 3D geometry and screen-space UI do not receive this effect. Transparent sprite pixels retain their original alpha.

Each view supports 32 visible point lights and 64 shadow occluders. When exceeded, the components nearest the view center are used and one warning is logged. Shadows are ray/box tests in a fullscreen shader; use modest light/occluder counts, particularly on mobile. Target-device performance has not been measured. The package uses WebGPU-compatible shader features, but Android/iOS/Web device validation is still required.

## Reproducible smoke test (Windows)

```powershell
python Examples/create_showcase.py --engine C:/path/to/Funke --output C:/scratch/LightingShowcase
```

Open that project in the updated editor to compile its package and script assembly. Its `LightingSmoke` script checks scene values, cloned component independence, native getters/setters, clamps and non-finite rejection. Successful Play logs `LIGHTING TESTS PASSED`.

From the engine checkout, capture the runtime sequence:

```powershell
scripts/dev/Run.ps1 -App Runtime -Project C:/scratch/LightingShowcase -ScreenshotAt 1s,3s,5s,7s -FixedStep 60 -QuitAt 8s -Seconds 35
python C:/path/to/Funke.Lighting2D/Tests/verify_screenshots.py C:/path/to/the/reported/shots-folder
```

The Python check requires Pillow. The four images verify shadows on/off, light bypass/re-enable, warm/cool illumination and unchanged white UI. The test scene is generated only in the requested scratch directory. Without automated-run mode its lighting remains stationary for editing.

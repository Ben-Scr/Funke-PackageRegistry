using System;
using Funke;
using Funke.Lighting2D;

public sealed class LightingSmoke : EntityScript
{
    private Light2D m_Warm = null!;
    private Light2D m_Cool = null!;
    private AmbientLight2D m_Ambient = null!;
    private float m_Elapsed;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("LIGHTING TEST FAILED: " + message);
    }

    public override void OnStart()
    {
        m_Warm = Entity.FindByName("Warm Light")!.GetComponent<Light2D>()!;
        m_Cool = Entity.FindByName("Cool Light")!.GetComponent<Light2D>()!;
        m_Ambient = Entity.FindByName("Ambient")!.GetComponent<AmbientLight2D>()!;
        Check(Math.Abs(m_Warm.Radius - 7) < 0.001f, "serialized radius");
        Check(Math.Abs(m_Ambient.Intensity - 0.12f) < 0.001f, "serialized ambient");
        var duplicate = Entity.Instantiate(m_Warm.Entity);
        var copy = duplicate.GetComponent<Light2D>()!;
        Check(copy != null && Math.Abs(copy.Radius - 7) < 0.001f, "clone preserves component");
        copy!.Radius = 9;
        copy.Intensity = -2;
        Check(copy.Radius == 9 && copy.Intensity == 0 && m_Warm.Radius == 7, "independent native state and clamps");
        bool rejected = false;
        try { copy.Radius = float.NaN; } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && copy.Radius == 9, "nonfinite rejection");
        duplicate.Destroy();
        var box = Entity.FindByName("Wall")!.GetComponent<ShadowOccluder2D>()!;
        Check(Math.Abs(box.Size.Y - 3) < 0.001f && box.Opacity == 1, "serialized occluder");
        Log.Info("LIGHTING TESTS PASSED: scene load, clone, native API, clamps, nonfinite rejection, occluder.");
    }

    public override void OnUpdate()
    {
        if (!Application.IsAutomatedRun) return;
        m_Elapsed += Time.DeltaTime;
        m_Warm.CastShadows = m_Elapsed < 2 || m_Elapsed >= 6;
        m_Cool.CastShadows = m_Warm.CastShadows;
        bool enabled = m_Elapsed < 4 || m_Elapsed >= 6;
        m_Warm.Enabled = enabled;
        m_Cool.Enabled = enabled;
        m_Ambient.Enabled = enabled;
    }
}

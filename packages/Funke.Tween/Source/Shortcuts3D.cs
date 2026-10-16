using System;

namespace Funke.Tween;

/// <summary>
/// Tweens for <see cref="Transform3D"/> (local position, Euler rotation in degrees, scale), <see cref="MeshRenderer"/>,
/// lights and <see cref="Camera3D"/>. Euler angles interpolate per axis, so (0, 360, 0) is a full turn around Y.
/// </summary>
public static class Shortcuts3D
{
    private static readonly Func<object, Vector3> s_GetPosition = static o => ((Transform3D)o).Position;
    private static readonly Action<object, Vector3> s_SetPosition = static (o, v) => ((Transform3D)o).Position = v;
    private static readonly Func<object, float> s_GetX = static o => ((Transform3D)o).Position.X;
    private static readonly Action<object, float> s_SetX = static (o, v) =>
    {
        Transform3D t = (Transform3D)o;
        Vector3 p = t.Position;
        t.Position = new Vector3(v, p.Y, p.Z);
    };
    private static readonly Func<object, float> s_GetY = static o => ((Transform3D)o).Position.Y;
    private static readonly Action<object, float> s_SetY = static (o, v) =>
    {
        Transform3D t = (Transform3D)o;
        Vector3 p = t.Position;
        t.Position = new Vector3(p.X, v, p.Z);
    };
    private static readonly Func<object, float> s_GetZ = static o => ((Transform3D)o).Position.Z;
    private static readonly Action<object, float> s_SetZ = static (o, v) =>
    {
        Transform3D t = (Transform3D)o;
        Vector3 p = t.Position;
        t.Position = new Vector3(p.X, p.Y, v);
    };
    private static readonly Func<object, Vector3> s_GetRotation = static o => ((Transform3D)o).Rotation;
    private static readonly Action<object, Vector3> s_SetRotation = static (o, v) => ((Transform3D)o).Rotation = v;
    private static readonly Func<object, Vector3> s_GetScale = static o => ((Transform3D)o).Scale;
    private static readonly Action<object, Vector3> s_SetScale = static (o, v) => ((Transform3D)o).Scale = v;

    private static readonly Func<object, Vector4> s_GetMeshColor = static o => ((MeshRenderer)o).Color;
    private static readonly Action<object, Vector4> s_SetMeshColor = static (o, v) => ((MeshRenderer)o).Color = v;
    private static readonly Func<object, float> s_GetMeshAlpha = static o => ((MeshRenderer)o).Color.A;
    private static readonly Action<object, float> s_SetMeshAlpha = static (o, v) =>
    {
        MeshRenderer m = (MeshRenderer)o;
        Color c = m.Color;
        m.Color = new Color(c.R, c.G, c.B, v);
    };

    private static readonly Func<object, Vector4> s_GetDirectionalColor = static o => ((DirectionalLight)o).Color;
    private static readonly Action<object, Vector4> s_SetDirectionalColor = static (o, v) => ((DirectionalLight)o).Color = v;
    private static readonly Func<object, float> s_GetDirectionalIntensity = static o => ((DirectionalLight)o).Intensity;
    private static readonly Action<object, float> s_SetDirectionalIntensity = static (o, v) => ((DirectionalLight)o).Intensity = v;
    private static readonly Func<object, Vector4> s_GetPointColor = static o => ((PointLight)o).Color;
    private static readonly Action<object, Vector4> s_SetPointColor = static (o, v) => ((PointLight)o).Color = v;
    private static readonly Func<object, float> s_GetPointIntensity = static o => ((PointLight)o).Intensity;
    private static readonly Action<object, float> s_SetPointIntensity = static (o, v) => ((PointLight)o).Intensity = v;
    private static readonly Func<object, Vector4> s_GetSpotColor = static o => ((SpotLight)o).Color;
    private static readonly Action<object, Vector4> s_SetSpotColor = static (o, v) => ((SpotLight)o).Color = v;
    private static readonly Func<object, float> s_GetSpotIntensity = static o => ((SpotLight)o).Intensity;
    private static readonly Action<object, float> s_SetSpotIntensity = static (o, v) => ((SpotLight)o).Intensity = v;

    private static readonly Func<object, float> s_GetFieldOfView = static o => ((Camera3D)o).FieldOfView;
    private static readonly Action<object, float> s_SetFieldOfView = static (o, v) => ((Camera3D)o).FieldOfView = v;

    public static TweenHandle MoveTo(this Transform3D transform, Vector3 position, float duration)
        => Tween.Target<Vector3Tweener, Vector3>(transform, s_GetPosition, s_SetPosition, position, duration);

    public static TweenHandle MoveToX(this Transform3D transform, float x, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetX, s_SetX, x, duration);

    public static TweenHandle MoveToY(this Transform3D transform, float y, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetY, s_SetY, y, duration);

    public static TweenHandle MoveToZ(this Transform3D transform, float z, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetZ, s_SetZ, z, duration);

    public static TweenHandle MoveBy(this Transform3D transform, Vector3 offset, float duration)
        => transform.MoveTo(offset, duration).SetRelative();

    public static TweenHandle RotateTo(this Transform3D transform, Vector3 eulerDegrees, float duration)
        => Tween.Target<Vector3Tweener, Vector3>(transform, s_GetRotation, s_SetRotation, eulerDegrees, duration);

    /// <summary>Turns by more degrees; with <c>SetLoops(-1, LoopType.Incremental)</c> it spins forever.</summary>
    public static TweenHandle RotateBy(this Transform3D transform, Vector3 eulerDegrees, float duration)
        => transform.RotateTo(eulerDegrees, duration).SetRelative();

    public static TweenHandle ScaleTo(this Transform3D transform, Vector3 scale, float duration)
        => Tween.Target<Vector3Tweener, Vector3>(transform, s_GetScale, s_SetScale, scale, duration);

    public static TweenHandle ScaleTo(this Transform3D transform, float scale, float duration)
        => transform.ScaleTo(new Vector3(scale, scale, scale), duration);

    public static TweenHandle PunchPosition(this Transform3D transform, Vector3 punch, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(transform, s_GetPosition, s_SetPosition, duration, true, punch, vibrato, 0f, true, elasticity, 3);

    public static TweenHandle PunchRotation(this Transform3D transform, Vector3 eulerDegrees, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(transform, s_GetRotation, s_SetRotation, duration, true, eulerDegrees, vibrato, 0f, true, elasticity, 3);

    public static TweenHandle PunchScale(this Transform3D transform, Vector3 punch, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(transform, s_GetScale, s_SetScale, duration, true, punch, vibrato, 0f, true, elasticity, 3);

    public static TweenHandle PunchScale(this Transform3D transform, float punch, float duration, int vibrato = 10, float elasticity = 1f)
        => transform.PunchScale(new Vector3(punch, punch, punch), duration, vibrato, elasticity);

    /// <summary>Random jitter of the position by up to <paramref name="strength"/> units; <paramref name="randomness"/> in degrees (0..180).</summary>
    public static TweenHandle ShakePosition(this Transform3D transform, float duration, float strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => transform.ShakePosition(duration, new Vector3(strength, strength, strength), vibrato, randomness, fadeOut);

    public static TweenHandle ShakePosition(this Transform3D transform, float duration, Vector3 strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => Tween.TargetShake(transform, s_GetPosition, s_SetPosition, duration, false, strength, vibrato, randomness, fadeOut, 1f, 3);

    public static TweenHandle ShakeRotation(this Transform3D transform, float duration, Vector3 degrees, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => Tween.TargetShake(transform, s_GetRotation, s_SetRotation, duration, false, degrees, vibrato, randomness, fadeOut, 1f, 3);

    public static TweenHandle ShakeScale(this Transform3D transform, float duration, float strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => Tween.TargetShake(transform, s_GetScale, s_SetScale, duration, false, new Vector3(strength, strength, strength), vibrato, randomness, fadeOut, 1f, 3);

    /// <summary>Tint over the material color; a hit flash is <c>ColorTo(Color.White, 0.06f).SetLoops(2, LoopType.Yoyo)</c>.</summary>
    public static TweenHandle ColorTo(this MeshRenderer renderer, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(renderer, s_GetMeshColor, s_SetMeshColor, color, duration);

    public static TweenHandle FadeTo(this MeshRenderer renderer, float alpha, float duration)
        => Tween.Target<FloatTweener, float>(renderer, s_GetMeshAlpha, s_SetMeshAlpha, alpha, duration);

    public static TweenHandle ColorTo(this DirectionalLight light, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(light, s_GetDirectionalColor, s_SetDirectionalColor, color, duration);

    public static TweenHandle IntensityTo(this DirectionalLight light, float intensity, float duration)
        => Tween.Target<FloatTweener, float>(light, s_GetDirectionalIntensity, s_SetDirectionalIntensity, intensity, duration);

    public static TweenHandle ColorTo(this PointLight light, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(light, s_GetPointColor, s_SetPointColor, color, duration);

    public static TweenHandle IntensityTo(this PointLight light, float intensity, float duration)
        => Tween.Target<FloatTweener, float>(light, s_GetPointIntensity, s_SetPointIntensity, intensity, duration);

    public static TweenHandle ColorTo(this SpotLight light, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(light, s_GetSpotColor, s_SetSpotColor, color, duration);

    public static TweenHandle IntensityTo(this SpotLight light, float intensity, float duration)
        => Tween.Target<FloatTweener, float>(light, s_GetSpotIntensity, s_SetSpotIntensity, intensity, duration);

    public static TweenHandle FieldOfViewTo(this Camera3D camera, float degrees, float duration)
        => Tween.Target<FloatTweener, float>(camera, s_GetFieldOfView, s_SetFieldOfView, degrees, duration);

    /// <summary>
    /// Camera shake: jitters the camera entity's local position. A script that sets the camera position every frame
    /// overrides it; parent the camera to a rig that the script moves instead.
    /// </summary>
    public static TweenHandle Shake(this Camera3D camera, float duration, float strength = 0.2f, int vibrato = 15, float randomness = 90f, bool fadeOut = true)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Transform3D transform = camera.Entity.GetComponent<Transform3D>()
            ?? throw new InvalidOperationException("The camera entity has no Transform3D.");
        return transform.ShakePosition(duration, strength, vibrato, randomness, fadeOut);
    }
}

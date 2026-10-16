using System;

namespace Funke.Tween;

/// <summary>Tweens for <see cref="Transform2D"/>, <see cref="SpriteRenderer"/>, <see cref="TextRenderer"/> and <see cref="Camera2D"/>. Rotations are in degrees.</summary>
public static class Shortcuts2D
{
    private static readonly Func<object, Vector2> s_GetPosition = static o => ((Transform2D)o).Position;
    private static readonly Action<object, Vector2> s_SetPosition = static (o, v) => ((Transform2D)o).Position = v;
    private static readonly Func<object, float> s_GetPositionX = static o => ((Transform2D)o).Position.X;
    private static readonly Action<object, float> s_SetPositionX = static (o, v) =>
    {
        Transform2D t = (Transform2D)o;
        Vector2 p = t.Position;
        t.Position = new Vector2(v, p.Y);
    };
    private static readonly Func<object, float> s_GetPositionY = static o => ((Transform2D)o).Position.Y;
    private static readonly Action<object, float> s_SetPositionY = static (o, v) =>
    {
        Transform2D t = (Transform2D)o;
        Vector2 p = t.Position;
        t.Position = new Vector2(p.X, v);
    };
    private static readonly Func<object, Vector2> s_GetLocalPosition = static o => ((Transform2D)o).LocalTransform;
    private static readonly Action<object, Vector2> s_SetLocalPosition = static (o, v) => ((Transform2D)o).LocalTransform = v;
    private static readonly Func<object, float> s_GetRotation = static o => ((Transform2D)o).Rotation;
    private static readonly Action<object, float> s_SetRotation = static (o, v) => ((Transform2D)o).Rotation = v;
    private static readonly Func<object, float> s_GetLocalRotation = static o => ((Transform2D)o).LocalRotation;
    private static readonly Action<object, float> s_SetLocalRotation = static (o, v) => ((Transform2D)o).LocalRotation = v;
    private static readonly Func<object, Vector2> s_GetScale = static o => ((Transform2D)o).LocalScale;
    private static readonly Action<object, Vector2> s_SetScale = static (o, v) => ((Transform2D)o).LocalScale = v;

    // Shakes and punches work in local space so a moving parent (a camera rig) doesn't leak into the offset.
    private static readonly Func<object, Vector3> s_GetLocalPosition3 = static o => ((Transform2D)o).LocalTransform;
    private static readonly Action<object, Vector3> s_SetLocalPosition3 = static (o, v) => ((Transform2D)o).LocalTransform = new Vector2(v.X, v.Y);
    private static readonly Func<object, Vector3> s_GetLocalRotation3 = static o => new Vector3(((Transform2D)o).LocalRotation, 0f, 0f);
    private static readonly Action<object, Vector3> s_SetLocalRotation3 = static (o, v) => ((Transform2D)o).LocalRotation = v.X;
    private static readonly Func<object, Vector3> s_GetScale3 = static o => ((Transform2D)o).LocalScale;
    private static readonly Action<object, Vector3> s_SetScale3 = static (o, v) => ((Transform2D)o).LocalScale = new Vector2(v.X, v.Y);

    private static readonly Func<object, Vector4> s_GetSpriteColor = static o => ((SpriteRenderer)o).Color;
    private static readonly Action<object, Vector4> s_SetSpriteColor = static (o, v) => ((SpriteRenderer)o).Color = v;
    private static readonly Func<object, float> s_GetSpriteAlpha = static o => ((SpriteRenderer)o).Color.W;
    private static readonly Action<object, float> s_SetSpriteAlpha = static (o, v) =>
    {
        SpriteRenderer s = (SpriteRenderer)o;
        Vector4 c = s.Color;
        s.Color = new Vector4(c.X, c.Y, c.Z, v);
    };

    private static readonly Func<object, Vector4> s_GetTextColor = static o => ((TextRenderer)o).Color;
    private static readonly Action<object, Vector4> s_SetTextColor = static (o, v) => ((TextRenderer)o).Color = v;
    private static readonly Func<object, float> s_GetTextAlpha = static o => ((TextRenderer)o).Color.W;
    private static readonly Action<object, float> s_SetTextAlpha = static (o, v) =>
    {
        TextRenderer t = (TextRenderer)o;
        Vector4 c = t.Color;
        t.Color = new Vector4(c.X, c.Y, c.Z, v);
    };
    private static readonly Func<object, float> s_GetFontSize = static o => ((TextRenderer)o).FontSize;
    private static readonly Action<object, float> s_SetFontSize = static (o, v) => ((TextRenderer)o).FontSize = v;

    private static readonly Func<object, float> s_GetZoom = static o => ((Camera2D)o).Zoom;
    private static readonly Action<object, float> s_SetZoom = static (o, v) => ((Camera2D)o).Zoom = v;
    private static readonly Func<object, float> s_GetOrthoSize = static o => ((Camera2D)o).OrthographicSize;
    private static readonly Action<object, float> s_SetOrthoSize = static (o, v) => ((Camera2D)o).OrthographicSize = v;
    private static readonly Func<object, Vector4> s_GetClearColor = static o => ((Camera2D)o).ClearColor;
    private static readonly Action<object, Vector4> s_SetClearColor = static (o, v) => ((Camera2D)o).ClearColor = v;

    /// <summary>Moves to a world position.</summary>
    public static TweenHandle MoveTo(this Transform2D transform, Vector2 position, float duration)
        => Tween.Target<Vector2Tweener, Vector2>(transform, s_GetPosition, s_SetPosition, position, duration);

    public static TweenHandle MoveToX(this Transform2D transform, float x, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetPositionX, s_SetPositionX, x, duration);

    public static TweenHandle MoveToY(this Transform2D transform, float y, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetPositionY, s_SetPositionY, y, duration);

    /// <summary>Moves by an offset from wherever the entity is when the tween starts.</summary>
    public static TweenHandle MoveBy(this Transform2D transform, Vector2 offset, float duration)
        => transform.MoveTo(offset, duration).SetRelative();

    /// <summary>Moves to a position relative to the parent.</summary>
    public static TweenHandle LocalMoveTo(this Transform2D transform, Vector2 localPosition, float duration)
        => Tween.Target<Vector2Tweener, Vector2>(transform, s_GetLocalPosition, s_SetLocalPosition, localPosition, duration);

    /// <summary>Turns to a world rotation; 360 more than the current angle is a full spin.</summary>
    public static TweenHandle RotateTo(this Transform2D transform, float degrees, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetRotation, s_SetRotation, degrees, duration);

    public static TweenHandle LocalRotateTo(this Transform2D transform, float degrees, float duration)
        => Tween.Target<FloatTweener, float>(transform, s_GetLocalRotation, s_SetLocalRotation, degrees, duration);

    /// <summary>Turns by <paramref name="degrees"/> more; with <c>SetLoops(-1, LoopType.Incremental)</c> it spins forever.</summary>
    public static TweenHandle RotateBy(this Transform2D transform, float degrees, float duration)
        => transform.LocalRotateTo(degrees, duration).SetRelative();

    /// <summary>Scales the local scale.</summary>
    public static TweenHandle ScaleTo(this Transform2D transform, Vector2 scale, float duration)
        => Tween.Target<Vector2Tweener, Vector2>(transform, s_GetScale, s_SetScale, scale, duration);

    public static TweenHandle ScaleTo(this Transform2D transform, float scale, float duration)
        => transform.ScaleTo(new Vector2(scale, scale), duration);

    public static TweenHandle PunchPosition(this Transform2D transform, Vector2 punch, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(transform, s_GetLocalPosition3, s_SetLocalPosition3, duration, true, punch, vibrato, 0f, true, elasticity, 2);

    public static TweenHandle PunchRotation(this Transform2D transform, float degrees, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(transform, s_GetLocalRotation3, s_SetLocalRotation3, duration, true, new Vector3(degrees, 0f, 0f), vibrato, 0f, true, elasticity, 1);

    public static TweenHandle PunchScale(this Transform2D transform, Vector2 punch, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(transform, s_GetScale3, s_SetScale3, duration, true, punch, vibrato, 0f, true, elasticity, 2);

    public static TweenHandle PunchScale(this Transform2D transform, float punch, float duration, int vibrato = 10, float elasticity = 1f)
        => transform.PunchScale(new Vector2(punch, punch), duration, vibrato, elasticity);

    /// <summary>Random jitter of the local position by up to <paramref name="strength"/> units; <paramref name="randomness"/> in degrees (0..180).</summary>
    public static TweenHandle ShakePosition(this Transform2D transform, float duration, float strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => transform.ShakePosition(duration, new Vector2(strength, strength), vibrato, randomness, fadeOut);

    public static TweenHandle ShakePosition(this Transform2D transform, float duration, Vector2 strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => Tween.TargetShake(transform, s_GetLocalPosition3, s_SetLocalPosition3, duration, false, strength, vibrato, randomness, fadeOut, 1f, 2);

    public static TweenHandle ShakeRotation(this Transform2D transform, float duration, float degrees, int vibrato = 10, bool fadeOut = true)
        => Tween.TargetShake(transform, s_GetLocalRotation3, s_SetLocalRotation3, duration, false, new Vector3(degrees, 0f, 0f), vibrato, 0f, fadeOut, 1f, 1);

    public static TweenHandle ShakeScale(this Transform2D transform, float duration, float strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => Tween.TargetShake(transform, s_GetScale3, s_SetScale3, duration, false, new Vector3(strength, strength, 0f), vibrato, randomness, fadeOut, 1f, 2);

    public static TweenHandle ColorTo(this SpriteRenderer sprite, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(sprite, s_GetSpriteColor, s_SetSpriteColor, color, duration);

    /// <summary>Animates only the alpha (0 = invisible).</summary>
    public static TweenHandle FadeTo(this SpriteRenderer sprite, float alpha, float duration)
        => Tween.Target<FloatTweener, float>(sprite, s_GetSpriteAlpha, s_SetSpriteAlpha, alpha, duration);

    public static TweenHandle ColorTo(this TextRenderer text, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(text, s_GetTextColor, s_SetTextColor, color, duration);

    public static TweenHandle FadeTo(this TextRenderer text, float alpha, float duration)
        => Tween.Target<FloatTweener, float>(text, s_GetTextAlpha, s_SetTextAlpha, alpha, duration);

    public static TweenHandle FontSizeTo(this TextRenderer text, float fontSize, float duration)
        => Tween.Target<FloatTweener, float>(text, s_GetFontSize, s_SetFontSize, fontSize, duration);

    public static TweenHandle ZoomTo(this Camera2D camera, float zoom, float duration)
        => Tween.Target<FloatTweener, float>(camera, s_GetZoom, s_SetZoom, zoom, duration);

    public static TweenHandle OrthographicSizeTo(this Camera2D camera, float size, float duration)
        => Tween.Target<FloatTweener, float>(camera, s_GetOrthoSize, s_SetOrthoSize, size, duration);

    public static TweenHandle ClearColorTo(this Camera2D camera, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(camera, s_GetClearColor, s_SetClearColor, color, duration);

    /// <summary>
    /// Camera shake: jitters the camera entity's local position. A script that sets the camera position every frame
    /// overrides it; parent the camera to a rig that the script moves instead.
    /// </summary>
    public static TweenHandle Shake(this Camera2D camera, float duration, float strength = 0.3f, int vibrato = 15, float randomness = 90f, bool fadeOut = true)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Transform2D transform = camera.Entity.GetComponent<Transform2D>()
            ?? throw new InvalidOperationException("The camera entity has no Transform2D.");
        return transform.ShakePosition(duration, strength, vibrato, randomness, fadeOut);
    }
}

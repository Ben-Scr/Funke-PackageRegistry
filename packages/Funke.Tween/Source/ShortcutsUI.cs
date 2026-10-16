using System;
using Funke.UI;

namespace Funke.Tween;

/// <summary>Tweens for UI (<see cref="RectTransform"/> in UI pixels, <see cref="Image"/>), <see cref="AudioSource"/>, and killing an object's tweens.</summary>
public static class ShortcutsUI
{
    private static readonly Func<object, Vector2> s_GetAnchored = static o => ((RectTransform)o).AnchoredPosition;
    private static readonly Action<object, Vector2> s_SetAnchored = static (o, v) => ((RectTransform)o).AnchoredPosition = v;
    private static readonly Func<object, Vector2> s_GetSize = static o => ((RectTransform)o).SizeDelta;
    private static readonly Action<object, Vector2> s_SetSize = static (o, v) => ((RectTransform)o).SizeDelta = v;
    private static readonly Func<object, float> s_GetRotation = static o => ((RectTransform)o).LocalRotation;
    private static readonly Action<object, float> s_SetRotation = static (o, v) => ((RectTransform)o).LocalRotation = v;
    private static readonly Func<object, Vector2> s_GetScale = static o => ((RectTransform)o).LocalScale;
    private static readonly Action<object, Vector2> s_SetScale = static (o, v) => ((RectTransform)o).LocalScale = v;

    private static readonly Func<object, Vector3> s_GetAnchored3 = static o => ((RectTransform)o).AnchoredPosition;
    private static readonly Action<object, Vector3> s_SetAnchored3 = static (o, v) => ((RectTransform)o).AnchoredPosition = new Vector2(v.X, v.Y);
    private static readonly Func<object, Vector3> s_GetRotation3 = static o => new Vector3(((RectTransform)o).LocalRotation, 0f, 0f);
    private static readonly Action<object, Vector3> s_SetRotation3 = static (o, v) => ((RectTransform)o).LocalRotation = v.X;
    private static readonly Func<object, Vector3> s_GetScale3 = static o => ((RectTransform)o).LocalScale;
    private static readonly Action<object, Vector3> s_SetScale3 = static (o, v) => ((RectTransform)o).LocalScale = new Vector2(v.X, v.Y);

    private static readonly Func<object, Vector4> s_GetImageColor = static o => ((Image)o).Color;
    private static readonly Action<object, Vector4> s_SetImageColor = static (o, v) => ((Image)o).Color = v;
    private static readonly Func<object, float> s_GetImageAlpha = static o => ((Image)o).Color.W;
    private static readonly Action<object, float> s_SetImageAlpha = static (o, v) =>
    {
        Image image = (Image)o;
        Vector4 c = image.Color;
        image.Color = new Vector4(c.X, c.Y, c.Z, v);
    };

    private static readonly Func<object, float> s_GetVolume = static o => ((AudioSource)o).Volume;
    private static readonly Action<object, float> s_SetVolume = static (o, v) => ((AudioSource)o).Volume = v;
    private static readonly Func<object, float> s_GetPitch = static o => ((AudioSource)o).Pitch;
    private static readonly Action<object, float> s_SetPitch = static (o, v) => ((AudioSource)o).Pitch = v;

    /// <summary>Moves the anchored position (UI pixels from the anchors).</summary>
    public static TweenHandle MoveTo(this RectTransform rect, Vector2 anchoredPosition, float duration)
        => Tween.Target<Vector2Tweener, Vector2>(rect, s_GetAnchored, s_SetAnchored, anchoredPosition, duration);

    public static TweenHandle MoveBy(this RectTransform rect, Vector2 offset, float duration)
        => rect.MoveTo(offset, duration).SetRelative();

    /// <summary>Animates the size delta (width/height in UI pixels for fixed anchors).</summary>
    public static TweenHandle SizeTo(this RectTransform rect, Vector2 sizeDelta, float duration)
        => Tween.Target<Vector2Tweener, Vector2>(rect, s_GetSize, s_SetSize, sizeDelta, duration);

    public static TweenHandle RotateTo(this RectTransform rect, float degrees, float duration)
        => Tween.Target<FloatTweener, float>(rect, s_GetRotation, s_SetRotation, degrees, duration);

    public static TweenHandle ScaleTo(this RectTransform rect, Vector2 scale, float duration)
        => Tween.Target<Vector2Tweener, Vector2>(rect, s_GetScale, s_SetScale, scale, duration);

    /// <summary>Pop-in: <c>rect.ScaleTo(1f, 0.25f).From(0.8f).SetEase(Ease.OutBack).SetUnscaledTime()</c>.</summary>
    public static TweenHandle ScaleTo(this RectTransform rect, float scale, float duration)
        => rect.ScaleTo(new Vector2(scale, scale), duration);

    public static TweenHandle PunchPosition(this RectTransform rect, Vector2 punch, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(rect, s_GetAnchored3, s_SetAnchored3, duration, true, punch, vibrato, 0f, true, elasticity, 2);

    public static TweenHandle PunchRotation(this RectTransform rect, float degrees, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(rect, s_GetRotation3, s_SetRotation3, duration, true, new Vector3(degrees, 0f, 0f), vibrato, 0f, true, elasticity, 1);

    /// <summary>Button feedback: <c>rect.PunchScale(0.15f, 0.3f)</c>.</summary>
    public static TweenHandle PunchScale(this RectTransform rect, float punch, float duration, int vibrato = 10, float elasticity = 1f)
        => Tween.TargetShake(rect, s_GetScale3, s_SetScale3, duration, true, new Vector3(punch, punch, 0f), vibrato, 0f, true, elasticity, 2);

    /// <summary>Random jitter by up to <paramref name="strength"/> UI pixels (a refused input, an error field).</summary>
    public static TweenHandle ShakePosition(this RectTransform rect, float duration, float strength, int vibrato = 10, float randomness = 90f, bool fadeOut = true)
        => Tween.TargetShake(rect, s_GetAnchored3, s_SetAnchored3, duration, false, new Vector3(strength, strength, 0f), vibrato, randomness, fadeOut, 1f, 2);

    public static TweenHandle ShakeRotation(this RectTransform rect, float duration, float degrees, int vibrato = 10, bool fadeOut = true)
        => Tween.TargetShake(rect, s_GetRotation3, s_SetRotation3, duration, false, new Vector3(degrees, 0f, 0f), vibrato, 0f, fadeOut, 1f, 1);

    public static TweenHandle ColorTo(this Image image, Color color, float duration)
        => Tween.Target<Vector4Tweener, Vector4>(image, s_GetImageColor, s_SetImageColor, color, duration);

    /// <summary>Animates only the alpha; screen fades: <c>overlay.FadeTo(1f, 0.4f).SetUnscaledTime().OnComplete(...)</c>.</summary>
    public static TweenHandle FadeTo(this Image image, float alpha, float duration)
        => Tween.Target<FloatTweener, float>(image, s_GetImageAlpha, s_SetImageAlpha, alpha, duration);

    /// <summary>Linear 0..1 volume; cross-fades: fade one source to 0 while the other goes to 1.</summary>
    public static TweenHandle VolumeTo(this AudioSource source, float volume, float duration)
        => Tween.Target<FloatTweener, float>(source, s_GetVolume, s_SetVolume, volume, duration);

    public static TweenHandle PitchTo(this AudioSource source, float pitch, float duration)
        => Tween.Target<FloatTweener, float>(source, s_GetPitch, s_SetPitch, pitch, duration);

    /// <summary>Kills every tween on this component (see <see cref="Tween.Kill(object, bool)"/>).</summary>
    public static int KillTweens(this Component component, bool complete = false) => Tween.Kill(component, complete);

    /// <summary>Kills every tween linked to this entity, on any of its components.</summary>
    public static int KillTweens(this Entity entity, bool complete = false) => Tween.Kill(entity, complete);
}

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Funke.Lighting2D;

internal static unsafe class NativeBindings
{
    private const string Library = "Pkg.Funke.Lighting2D.Native";
    [DllImport(Library, EntryPoint = "Funke_Lighting2D_Read", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Read(ulong entityId, int kind, float* values, int count);
    [DllImport(Library, EntryPoint = "Funke_Lighting2D_Write", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Write(ulong entityId, int kind, float* values, int count);

    [ModuleInitializer]
    internal static void Register()
    {
        Entity.RegisterNativeComponent<Light2D>("Light2D");
        Entity.RegisterNativeComponent<AmbientLight2D>("AmbientLight2D");
        Entity.RegisterNativeComponent<ShadowOccluder2D>("ShadowOccluder2D");
    }
}

public abstract class LightingComponent : Component
{
    protected abstract int Kind { get; }
    private int Count => Kind == 0 ? 9 : 6;

    private unsafe void Read(Span<float> values)
    {
        fixed (float* data = values)
            if (NativeBindings.Read(Entity.ID, Kind, data, values.Length) == 0)
                throw new InvalidOperationException("The lighting component is no longer available.");
    }

    private unsafe void Write(ReadOnlySpan<float> values)
    {
        foreach (float value in values)
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(values));
        fixed (float* data = values)
            if (NativeBindings.Write(Entity.ID, Kind, data, values.Length) == 0)
                throw new InvalidOperationException("The lighting component is no longer available.");
    }

    protected float Get(int index)
    {
        Span<float> values = stackalloc float[Count];
        Read(values);
        return values[index];
    }
    protected void Set(int index, float value)
    {
        Span<float> values = stackalloc float[Count];
        Read(values);
        values[index] = value;
        Write(values);
    }
    protected Color GetColor()
    {
        Span<float> values = stackalloc float[Count];
        Read(values);
        return new Color(values[1], values[2], values[3], values[4]);
    }
    protected void SetColor(Color color)
    {
        Span<float> values = stackalloc float[Count];
        Read(values);
        values[1] = color.R; values[2] = color.G; values[3] = color.B; values[4] = color.A;
        Write(values);
    }
    protected Vector2 GetVector(int index)
    {
        Span<float> values = stackalloc float[Count];
        Read(values);
        return new Vector2(values[index], values[index + 1]);
    }
    protected void SetVector(int index, Vector2 vector)
    {
        Span<float> values = stackalloc float[Count];
        Read(values);
        values[index] = vector.X; values[index + 1] = vector.Y;
        Write(values);
    }
    public bool Enabled { get => Get(0) != 0; set => Set(0, value ? 1 : 0); }
}

public sealed class Light2D : LightingComponent
{
    protected override int Kind => 0;
    public Color Color { get => GetColor(); set => SetColor(value); }
    public float Intensity { get => Get(5); set => Set(5, value); }
    public float Radius { get => Get(6); set => Set(6, value); }
    public float Falloff { get => Get(7); set => Set(7, value); }
    public bool CastShadows { get => Get(8) != 0; set => Set(8, value ? 1 : 0); }
}

public sealed class AmbientLight2D : LightingComponent
{
    protected override int Kind => 1;
    public Color Color { get => GetColor(); set => SetColor(value); }
    public float Intensity { get => Get(5); set => Set(5, value); }
}

public sealed class ShadowOccluder2D : LightingComponent
{
    protected override int Kind => 2;
    public Vector2 Size { get => GetVector(1); set => SetVector(1, value); }
    public Vector2 Offset { get => GetVector(3); set => SetVector(3, value); }
    public float Opacity { get => Get(5); set => Set(5, value); }
}

using System;
using System.Collections.Generic;
using System.Runtime.Loader;

namespace Funke.InputActions;

/// <summary>Main-thread input context. Reading any action samples the map once per frame; Update also advances rebinding.</summary>
public sealed partial class InputActionMap : IDisposable
{
    private readonly IInputSource m_Source;
    private readonly List<InputAction> m_Actions = new();
    private readonly Dictionary<string, InputAction> m_ByName = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<InputAction> m_ReadOnlyActions;
    private long m_Frame;
    private bool m_HasFrame;
    private bool m_Disposed;
    private int m_Gamepad;
    private bool m_KeyboardMouseEnabled = true;
    private RebindOperation? m_Rebind;

    public InputActionMap(string name, IInputSource? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        m_Source = source ?? FunkeInputSource.Instance;
        m_ReadOnlyActions = m_Actions.AsReadOnly();
        if (source == null) InputMapLifetime.Register(this);
    }

    public string Name { get; }
    public bool Enabled { get; private set; }
    public IReadOnlyList<InputAction> Actions => m_ReadOnlyActions;
    public InputAction this[string name] => m_ByName[name];
    public InputAction? FindAction(string name) => m_ByName.TryGetValue(name, out InputAction? action) ? action : null;

    public int Gamepad
    {
        get => m_Gamepad;
        set
        {
            ThrowIfDisposed();
            if (value < 0 || value >= global::Funke.Input.MaxGamepads) throw new ArgumentOutOfRangeException(nameof(value));
            if (m_Gamepad == value) return;
            m_Gamepad = value;
            ResetState(true);
        }
    }

    public bool KeyboardMouseEnabled
    {
        get => m_KeyboardMouseEnabled;
        set
        {
            ThrowIfDisposed();
            if (m_KeyboardMouseEnabled == value) return;
            m_KeyboardMouseEnabled = value;
            ResetState(true);
        }
    }

    public InputAction AddButton(string name) => Add(name, InputActionType.Button);
    public InputAction AddAxis(string name) => Add(name, InputActionType.Axis);
    public InputAction AddVector2(string name) => Add(name, InputActionType.Vector2);

    private InputAction Add(string name, InputActionType type)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (m_ByName.ContainsKey(name)) throw new ArgumentException($"Action '{name}' already exists in '{Name}'.", nameof(name));
        var action = new InputAction(this, name, type);
        m_ByName.Add(name, action);
        m_Actions.Add(action);
        action.ResetState(Enabled);
        Invalidate();
        return action;
    }

    /// <summary>Held buttons must be released before they fire in a newly enabled context; axes remain responsive.</summary>
    public void Enable(bool suppressHeldButtons = true)
    {
        ThrowIfDisposed();
        if (Enabled) return;
        Enabled = true;
        ResetState(suppressHeldButtons);
        // Sample now so a fresh press later in this frame is not mistaken for a held activation key.
        Update();
    }

    public void Disable()
    {
        m_Rebind?.Cancel();
        m_Rebind = null;
        Enabled = false;
        ResetState(false);
    }

    public void Update()
    {
        ThrowIfDisposed();
        if (!Enabled && m_Rebind == null) return;
        long frame = m_Source.Frame;
        if (m_HasFrame && m_Frame == frame) return;
        m_Frame = frame;
        m_HasFrame = true;
        if (m_Rebind != null)
        {
            RebindOperation rebinding = m_Rebind;
            rebinding.Poll(m_Source);
            if (rebinding.Finished && ReferenceEquals(m_Rebind, rebinding)) m_Rebind = null;
            foreach (InputAction action in m_Actions) action.ResetState(true);
            // Captured controls cannot also activate gameplay in the completion frame.
            m_Frame = frame;
            m_HasFrame = true;
            return;
        }
        if (!Enabled) return;
        foreach (InputAction action in m_Actions) action.Evaluate(m_Source, m_Gamepad, m_KeyboardMouseEnabled);
    }

    public void ResetBindings()
    {
        ThrowIfDisposed();
        CancelRebinding();
        foreach (InputAction action in m_Actions) action.ResetBindings();
    }

    public void CancelRebinding() => m_Rebind?.Cancel();

    internal void RebindCanceled(RebindOperation operation)
    {
        if (!ReferenceEquals(m_Rebind, operation)) return;
        m_Rebind = null;
        ResetState(true);
    }

    internal void Invalidate() => m_HasFrame = false;
    internal IInputSource Source => m_Source;
    internal bool ShouldSampleHeldControls => Enabled || m_Rebind != null;
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(m_Disposed, this);

    private void ResetState(bool suppressHeldButtons)
    {
        foreach (InputAction action in m_Actions) action.ResetState(suppressHeldButtons);
        Invalidate();
    }

    internal RebindOperation BeginRebind(InputAction action, int index, InputBindingPart part, RebindOptions options)
    {
        ThrowIfDisposed();
        var operation = new RebindOperation(action, index, part, options, m_Source);
        m_Rebind?.Cancel();
        m_Rebind = operation;
        ResetState(true);
        return operation;
    }

    public void Dispose()
    {
        if (m_Disposed) return;
        Disable();
        m_Disposed = true;
        GC.SuppressFinalize(this);
    }
}

internal static class InputMapLifetime
{
    private static readonly List<WeakReference<InputActionMap>> s_Maps = new();

    static InputMapLifetime()
    {
        Application.OnPlayModeExited += Reset;
        AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(typeof(InputMapLifetime).Assembly);
        if (context != null && context.IsCollectible) context.Unloading += _ => Application.OnPlayModeExited -= Reset;
    }

    internal static void Register(InputActionMap map)
    {
        s_Maps.RemoveAll(reference => !reference.TryGetTarget(out _));
        s_Maps.Add(new WeakReference<InputActionMap>(map));
    }

    private static void Reset()
    {
        foreach (WeakReference<InputActionMap> reference in s_Maps)
            if (reference.TryGetTarget(out InputActionMap? map)) map.Disable();
        s_Maps.RemoveAll(reference => !reference.TryGetTarget(out _));
    }
}

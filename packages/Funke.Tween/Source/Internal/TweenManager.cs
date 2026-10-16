using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Threading;
using Funke.Coroutines;

namespace Funke.Tween;

internal static class TweenPool<T> where T : TweenCore, new()
{
    private static readonly Stack<T> s_Free = new();

    internal static T Get() => s_Free.Count > 0 ? s_Free.Pop() : new T();
    internal static void Return(T tween) => s_Free.Push(tween);

    internal static void Prewarm(int count)
    {
        while (s_Free.Count < count) s_Free.Push(new T());
    }
}

/// <summary>Owns the running root tweens, updates them once per frame and recycles finished ones.</summary>
internal static class TweenManager
{
    private static readonly List<TweenCore> s_Active = new(64);
    private static bool s_Updating;

    internal static float GlobalTimeScale = 1f;
    internal static bool AutoUpdate = true;

    // Test seam: the default asks the engine whether the entity still exists.
    internal static Func<Entity, bool> IsAlive = static entity => entity;

    internal static int ActiveCount
    {
        get
        {
            int count = 0;
            foreach (TweenCore tween in s_Active)
            {
                if (tween.Alive && !tween.Killed && tween.Parent == null) count++;
            }
            return count;
        }
    }

    static TweenManager()
    {
        Application.OnPlayModeExited += Reset;
        // The game's scripts (and this package) unload on hot reload; a handler left on Funke-ScriptCore would pin them.
        AssemblyLoadContext? context = AssemblyLoadContext.GetLoadContext(typeof(TweenManager).Assembly);
        if (context != null && context.IsCollectible) context.Unloading += _ => Application.OnPlayModeExited -= Reset;
    }

    internal static T Spawn<T>(float duration) where T : TweenCore, new()
    {
        T tween = TweenPool<T>.Get();
        tween.Activate(duration);
        tween.InActiveList = true;
        s_Active.Add(tween);
        Driver.Ensure();
        return tween;
    }

    /// <summary>Takes a tween out of the update list once it belongs to a sequence.</summary>
    internal static void Detach(TweenCore tween)
    {
        // Mid-update the list is being iterated; Compact drops the entry since it now has a parent.
        if (!tween.InActiveList || s_Updating) return;
        for (int i = s_Active.Count - 1; i >= 0; i--)
        {
            if (s_Active[i] != tween) continue;
            s_Active.RemoveAt(i);
            tween.InActiveList = false;
            return;
        }
    }

    internal static void Update(float deltaTime, float unscaledDeltaTime)
    {
        if (s_Updating) return;
        s_Updating = true;
        try
        {
            int count = s_Active.Count;
            for (int i = 0; i < count; i++)
            {
                TweenCore tween = s_Active[i];
                if (!tween.Alive || tween.Killed || tween.Parent != null || !tween.Playing) continue;

                if (tween.Link is { } link && !IsAlive(link))
                {
                    tween.TargetLost = true;
                    tween.Kill(complete: false);
                    continue;
                }

                float step = (tween.Unscaled ? unscaledDeltaTime : deltaTime) * tween.TimeScale * GlobalTimeScale;
                if (tween.Advance(step)) tween.Kill(complete: false);
            }
        }
        finally
        {
            s_Updating = false;
        }
        Compact();
    }

    // Recycling happens only here, outside user callbacks, so a pooled core is never in the list twice.
    private static void Compact()
    {
        int write = 0;
        for (int i = 0; i < s_Active.Count; i++)
        {
            TweenCore tween = s_Active[i];
            if (tween.Alive && tween.Killed && tween.Parent == null)
            {
                tween.InActiveList = false;
                tween.Recycle();
                continue;
            }
            if (!tween.Alive || tween.Parent != null)
            {
                tween.InActiveList = false;
                continue;
            }
            s_Active[write++] = tween;
        }
        if (write < s_Active.Count) s_Active.RemoveRange(write, s_Active.Count - write);
    }

    internal static int ForEachMatching(object key, Action<TweenCore> action)
    {
        int count = 0;
        int total = s_Active.Count;
        for (int i = 0; i < total && i < s_Active.Count; i++)
        {
            TweenCore tween = s_Active[i];
            if (!tween.Alive || tween.Killed || tween.Parent != null || !Matches(tween, key)) continue;
            action(tween);
            count++;
        }
        return count;
    }

    internal static void ForEachRoot(Action<TweenCore> action)
    {
        int total = s_Active.Count;
        for (int i = 0; i < total && i < s_Active.Count; i++)
        {
            TweenCore tween = s_Active[i];
            if (tween.Alive && !tween.Killed && tween.Parent == null) action(tween);
        }
    }

    private static bool Matches(TweenCore tween, object key)
    {
        if (tween.Id != null && tween.Id.Equals(key)) return true;
        switch (key)
        {
            case Entity entity:
                return entity.ID != 0 && tween.Link is { } link && link.ID == entity.ID;
            case Component component:
                return tween.Target is Component target
                    && target.GetType() == component.GetType()
                    && target.Entity.ID != 0
                    && target.Entity.ID == component.Entity.ID;
            default:
                return ReferenceEquals(tween.Target, key);
        }
    }

    /// <summary>Drops every tween without callbacks: Play mode stopped, nothing of that session may run on.</summary>
    internal static void Reset()
    {
        foreach (TweenCore tween in s_Active)
        {
            if (!tween.Alive || tween.Parent != null) continue;
            tween.Killed = true;
            tween.Recycle();
        }
        s_Active.Clear();
        s_Updating = false;
        GlobalTimeScale = 1f;
        Driver.Stop();
    }

    /// <summary>
    /// Ticks <see cref="Update"/> once per frame through the engine's coroutine scheduler while tweens exist, so the
    /// package needs no GlobalSystem entry in project.json (systems from package assemblies aren't discovered).
    /// </summary>
    private sealed class Driver
    {
        private static Driver? s_Current;
        private static CancellationTokenSource? s_Lifetime;
        private static int s_LastFrame = -1;
        private static int s_LastTickFrame = -1;

        private readonly Action m_Tick;
        private bool m_Armed;

        private Driver() => m_Tick = Tick;

        internal static void Ensure()
        {
            if (!AutoUpdate) return;
            Driver? driver = s_Current;
            // The scheduler drops pending waits on a script reload without firing them; re-arm a driver that went silent.
            if (driver != null && driver.m_Armed && s_LastTickFrame >= 0 && Time.FrameCount - s_LastTickFrame > 2)
            {
                driver = null;
            }
            if (driver == null)
            {
                driver = new Driver();
                s_Current = driver;
            }
            if (!driver.m_Armed) driver.Arm();
        }

        internal static void Stop()
        {
            s_Current = null;
            s_LastFrame = -1;
            s_LastTickFrame = -1;
        }

        private void Arm()
        {
            m_Armed = true;
            s_Lifetime ??= new CancellationTokenSource();
            // An explicit token keeps the wait from inheriting the calling script's DestroyToken.
            new WaitForNextFrame(s_Lifetime.Token).GetAwaiter().OnCompleted(m_Tick);
        }

        private void Tick()
        {
            m_Armed = false;
            if (s_Current != this || !AutoUpdate) return;

            int frame = Time.FrameCount;
            s_LastTickFrame = frame;
            // Each loaded scene pumps the scheduler, so with additive scenes this runs more than once per frame.
            if (frame != s_LastFrame)
            {
                s_LastFrame = frame;
                Update(Time.DeltaTime, Time.UnscaledDeltaTime);
            }

            if (s_Active.Count > 0) Arm();
        }
    }
}

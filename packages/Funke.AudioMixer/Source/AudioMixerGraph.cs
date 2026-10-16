using System;
using System.Collections.Generic;

namespace Funke.AudioMixer;

/// <summary>
/// The group tree behind <see cref="AudioMixer"/>: volumes, mutes, parents, snapshots and fades. Pure data, no engine
/// calls, so it can be tested and reused on its own.
/// </summary>
public sealed class AudioMixerGraph
{
    public const string Master = "Master";
    public const string Music = "Music";
    public const string Sfx = "SFX";

    private sealed class Group
    {
        public string Name = "";
        public string? Parent;
        public float Volume = 1f;
        public bool Muted;
        public Fade? Fade;
    }

    private sealed class Fade
    {
        public float From;
        public float To;
        public float Duration;
        public float Elapsed;
    }

    private readonly Dictionary<string, Group> m_Groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, float>> m_Snapshots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumped on every change that can alter an effective volume.</summary>
    public int Version { get; private set; }

    public AudioMixerGraph()
    {
        AddGroup(Master, null);
        AddGroup(Music, Master);
        AddGroup(Sfx, Master);
    }

    public IEnumerable<string> GroupNames => m_Groups.Keys;

    public bool HasGroup(string name) => m_Groups.ContainsKey(name);

    /// <summary>The group's parent; null for Master.</summary>
    public string? GetParent(string group) => Get(group).Parent;

    /// <summary>Every group with its depth below Master, parents before their children (children by name).</summary>
    public IReadOnlyList<(string Group, int Depth)> GetTree()
    {
        var tree = new List<(string, int)>();
        void Visit(string group, int depth)
        {
            tree.Add((m_Groups[group].Name, depth));
            var children = new List<string>();
            foreach (Group g in m_Groups.Values)
                if (g.Parent != null && string.Equals(g.Parent, group, StringComparison.OrdinalIgnoreCase)) children.Add(g.Name);
            children.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string child in children) Visit(child, depth + 1);
        }
        Visit(Master, 0);
        return tree;
    }

    /// <summary>Adds a group under <paramref name="parent"/> (Master when null); an existing group is moved.</summary>
    public void AddGroup(string name, string? parent = Master, float volume = 1f)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name must not be empty.", nameof(name));
        if (parent != null && !m_Groups.ContainsKey(parent))
            throw new ArgumentException($"Unknown parent group '{parent}'.", nameof(parent));
        for (string? p = parent; p != null; p = m_Groups[p].Parent)
        {
            if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Group '{name}' cannot be its own ancestor.", nameof(parent));
        }

        if (!m_Groups.TryGetValue(name, out Group? group))
        {
            group = new Group { Name = name, Volume = Clamp01(volume) };
            m_Groups.Add(name, group);
        }
        group.Parent = string.Equals(name, Master, StringComparison.OrdinalIgnoreCase) ? null : parent ?? Master;
        Version++;
    }

    public float GetVolume(string group) => Get(group).Volume;

    public void SetVolume(string group, float volume)
    {
        Group g = Get(group);
        g.Fade = null;
        g.Volume = Clamp01(volume);
        Version++;
    }

    public bool IsMuted(string group) => Get(group).Muted;

    public void SetMuted(string group, bool muted)
    {
        Get(group).Muted = muted;
        Version++;
    }

    /// <summary>Volume times every parent's volume; 0 when the group or a parent is muted.</summary>
    public float GetEffectiveVolume(string group)
    {
        float volume = 1f;
        for (Group? g = Get(group); g != null; g = g.Parent != null ? m_Groups[g.Parent] : null)
        {
            if (g.Muted) return 0f;
            volume *= g.Volume;
        }
        return volume;
    }

    /// <summary>Moves a group's volume to <paramref name="target"/> over <paramref name="seconds"/> (driven by <see cref="Advance"/>).</summary>
    public void FadeVolume(string group, float target, float seconds)
    {
        Group g = Get(group);
        if (seconds <= 0f)
        {
            SetVolume(group, target);
            return;
        }
        g.Fade = new Fade { From = g.Volume, To = Clamp01(target), Duration = seconds };
    }

    public bool IsFading
    {
        get
        {
            foreach (Group g in m_Groups.Values)
                if (g.Fade != null) return true;
            return false;
        }
    }

    /// <summary>Steps running fades; returns true while any is still running.</summary>
    public bool Advance(float deltaSeconds)
    {
        bool running = false;
        foreach (Group g in m_Groups.Values)
        {
            if (g.Fade is not { } fade) continue;
            fade.Elapsed += Math.Max(0f, deltaSeconds);
            float t = Math.Min(1f, fade.Elapsed / fade.Duration);
            // Summed float frame times land just short of the end (120 x 1/60 = 1.9999988), which cost a frame.
            if (t > 0.99999f) t = 1f;
            g.Volume = fade.From + (fade.To - fade.From) * t;
            if (t >= 1f) g.Fade = null;
            else running = true;
            Version++;
        }
        return running;
    }

    /// <summary>Stores target volumes for a set of groups under a name, e.g. "Paused" with Music at 0.3.</summary>
    public void DefineSnapshot(string name, IReadOnlyDictionary<string, float> volumes)
    {
        var copy = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, volume) in volumes)
        {
            Get(group);
            copy[group] = Clamp01(volume);
        }
        m_Snapshots[name] = copy;
    }

    /// <summary>Fades every group named in the snapshot to its stored volume.</summary>
    public void TransitionTo(string snapshot, float seconds)
    {
        if (!m_Snapshots.TryGetValue(snapshot, out var volumes))
            throw new ArgumentException($"Unknown snapshot '{snapshot}'.", nameof(snapshot));
        foreach (var (group, volume) in volumes)
            FadeVolume(group, volume, seconds);
    }

    public static float DecibelsToLinear(float decibels) => decibels <= -80f ? 0f : MathF.Pow(10f, decibels / 20f);
    public static float LinearToDecibels(float linear) => linear <= 0.0001f ? -80f : 20f * MathF.Log10(linear);

    private Group Get(string group)
    {
        if (!m_Groups.TryGetValue(group, out Group? g))
            throw new ArgumentException($"Unknown audio mixer group '{group}'. Add it with AudioMixer.AddGroup first.", nameof(group));
        return g;
    }

    private static float Clamp01(float value) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
}

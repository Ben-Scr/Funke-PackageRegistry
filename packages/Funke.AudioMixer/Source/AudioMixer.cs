using System;
using System.Collections.Generic;
using System.Threading;
using Funke.Coroutines;
using Funke.UI;

namespace Funke.AudioMixer;

/// <summary>
/// Volume groups for the whole game. Every sound goes through a group (Master, Music, SFX or your own); its loudness is
/// its own volume times the group's effective volume (the group and all its parents, 0 while any is muted).
/// <code>
/// AudioMixer.Attach(musicSource, AudioMixer.Music);          // AudioSource volume now follows the Music group
/// AudioMixer.PlayOneShot(jumpSound, AudioMixer.Sfx);         // one-shot through SFX
/// AudioMixer.SetVolume(AudioMixer.Music, 0.5f);               // saved to PlayerPrefs (AutoSave)
/// AudioMixer.BindSlider(musicSlider, AudioMixer.Music);       // settings screen in one line
/// </code>
/// </summary>
public static class AudioMixer
{
    public const string Master = AudioMixerGraph.Master;
    public const string Music = AudioMixerGraph.Music;
    public const string Sfx = AudioMixerGraph.Sfx;

    private const string k_PrefsPrefix = "Funke.AudioMixer.";

    private sealed class Output
    {
        public AudioSource? Source;
        public AudioStream? Stream;
        public string Group = Sfx;
        public float BaseVolume = 1f;
        public float Applied = -1f;
    }

    private static readonly AudioMixerGraph s_Graph = new();
    private static readonly List<Output> s_Outputs = new();
    private static bool s_Loaded;
    private static bool s_FadeLoopRunning;
    private static CancellationTokenSource? s_FadeLifetime;

    /// <summary>Raised after any volume or mute change (including every fade step).</summary>
    public static event Action? Changed;

    /// <summary>Write volume and mute changes to PlayerPrefs right away (PlayerPrefs saves on quit and when Play stops).</summary>
    public static bool AutoSave { get; set; } = true;

    /// <summary>The underlying group tree, for tools that need more than this facade.</summary>
    public static AudioMixerGraph Graph { get { EnsureLoaded(); return s_Graph; } }

    public static IEnumerable<string> Groups { get { EnsureLoaded(); return s_Graph.GroupNames; } }

    /// <summary>Adds a group (e.g. "UI" or "Voice") under <paramref name="parent"/>; its saved volume is restored.</summary>
    public static void AddGroup(string name, string parent = Master, float volume = 1f)
    {
        EnsureLoaded();
        s_Graph.AddGroup(name, parent, volume);
        LoadGroup(name);
        ApplyAll();
    }

    public static float GetVolume(string group) { EnsureLoaded(); return s_Graph.GetVolume(group); }
    public static float GetEffectiveVolume(string group) { EnsureLoaded(); return s_Graph.GetEffectiveVolume(group); }
    public static bool IsMuted(string group) { EnsureLoaded(); return s_Graph.IsMuted(group); }

    /// <summary>Linear volume 0..1.</summary>
    public static void SetVolume(string group, float volume)
    {
        EnsureLoaded();
        s_Graph.SetVolume(group, volume);
        if (AutoSave) SaveGroup(group);
        ApplyAll();
    }

    public static void SetVolumeDecibels(string group, float decibels) => SetVolume(group, AudioMixerGraph.DecibelsToLinear(decibels));
    public static float GetVolumeDecibels(string group) => AudioMixerGraph.LinearToDecibels(GetVolume(group));

    public static void SetMuted(string group, bool muted)
    {
        EnsureLoaded();
        s_Graph.SetMuted(group, muted);
        if (AutoSave) SaveGroup(group);
        ApplyAll();
    }

    /// <summary>Fades a group over time (unscaled, so it also runs while the game is paused). Not saved.</summary>
    public static void FadeVolume(string group, float target, float seconds)
    {
        EnsureLoaded();
        s_Graph.FadeVolume(group, target, seconds);
        ApplyAll();
        StartFadeLoop();
    }

    /// <summary>Named set of group volumes, e.g. DefineSnapshot("Paused", (Music, 0.3f), (Sfx, 0f)).</summary>
    public static void DefineSnapshot(string name, params (string Group, float Volume)[] volumes)
    {
        EnsureLoaded();
        var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, volume) in volumes) map[group] = volume;
        s_Graph.DefineSnapshot(name, map);
    }

    public static void TransitionTo(string snapshot, float seconds = 0.5f)
    {
        EnsureLoaded();
        s_Graph.TransitionTo(snapshot, seconds);
        ApplyAll();
        StartFadeLoop();
    }

    /// <summary>
    /// Routes an AudioSource through <paramref name="group"/>. Its current Volume becomes its own level; set it later
    /// with <see cref="SetSourceVolume"/>, since writing AudioSource.Volume directly is overwritten on the next change.
    /// </summary>
    public static void Attach(AudioSource source, string group)
    {
        EnsureLoaded();
        s_Graph.GetVolume(group);
        Output? output = Find(source);
        if (output == null)
        {
            output = new Output { Source = source, BaseVolume = source.Volume };
            s_Outputs.Add(output);
        }
        output.Group = group;
        Apply(output);
    }

    /// <summary>Routes a streamed file (AudioStream) through <paramref name="group"/>.</summary>
    public static void Attach(AudioStream stream, string group, float volume = 1f)
    {
        EnsureLoaded();
        s_Graph.GetVolume(group);
        Output? output = s_Outputs.Find(o => ReferenceEquals(o.Stream, stream));
        if (output == null)
        {
            output = new Output { Stream = stream };
            s_Outputs.Add(output);
        }
        output.Group = group;
        output.BaseVolume = Math.Clamp(volume, 0f, 1f);
        Apply(output);
    }

    public static void Detach(AudioSource source) => s_Outputs.RemoveAll(o => o.Source != null && o.Source.Entity.ID == source.Entity.ID);
    public static void Detach(AudioStream stream) => s_Outputs.RemoveAll(o => ReferenceEquals(o.Stream, stream));

    /// <summary>The attached source's own level (0..1), multiplied by its group.</summary>
    public static void SetSourceVolume(AudioSource source, float volume)
    {
        Output? output = Find(source);
        if (output == null)
        {
            source.Volume = volume;
            return;
        }
        output.BaseVolume = Math.Clamp(volume, 0f, 1f);
        output.Applied = -1f;
        Apply(output);
    }

    public static void PlayOneShot(Audio? clip, string group = Sfx, float volume = 1f)
    {
        if (clip == null) return;
        EnsureLoaded();
        float level = volume * s_Graph.GetEffectiveVolume(group);
        if (level > 0f) clip.PlayOneShot(level);
    }

    /// <summary>Drives a UI Slider (0..1) from a group and the group from the slider; returns an unbind action.</summary>
    public static Action BindSlider(Slider slider, string group)
    {
        EnsureLoaded();
        slider.MinValue = 0f;
        slider.MaxValue = 1f;
        slider.Value = s_Graph.GetVolume(group);
        void OnSlider(float value) => SetVolume(group, value);
        slider.OnValueChanged += OnSlider;
        return () => slider.OnValueChanged -= OnSlider;
    }

    /// <summary>Drives a UI Toggle (on = audible) from a group's mute state and back; returns an unbind action.</summary>
    public static Action BindMuteToggle(Toggle toggle, string group)
    {
        EnsureLoaded();
        toggle.IsOn = !s_Graph.IsMuted(group);
        void OnToggle(bool on) => SetMuted(group, !on);
        toggle.OnValueChanged += OnToggle;
        return () => toggle.OnValueChanged -= OnToggle;
    }

    /// <summary>Writes every group's volume and mute state to PlayerPrefs and saves them.</summary>
    public static void SaveVolumes()
    {
        EnsureLoaded();
        foreach (string group in s_Graph.GroupNames) SaveGroup(group);
        PlayerPrefs.Save();
    }

    /// <summary>Restores saved volumes; called automatically on first use.</summary>
    public static void LoadVolumes()
    {
        s_Loaded = true;
        foreach (string group in s_Graph.GroupNames) LoadGroup(group);
        ApplyAll();
    }

    /// <summary>Back to full volume, nothing muted, saved values removed.</summary>
    public static void ResetVolumes()
    {
        EnsureLoaded();
        foreach (string group in new List<string>(s_Graph.GroupNames))
        {
            s_Graph.SetVolume(group, 1f);
            s_Graph.SetMuted(group, false);
            PlayerPrefs.DeleteKey(k_PrefsPrefix + group + ".Volume");
            PlayerPrefs.DeleteKey(k_PrefsPrefix + group + ".Muted");
        }
        ApplyAll();
    }

    private static void EnsureLoaded()
    {
        if (!s_Loaded) LoadVolumes();
    }

    private static void SaveGroup(string group)
    {
        PlayerPrefs.SetFloat(k_PrefsPrefix + group + ".Volume", s_Graph.GetVolume(group));
        PlayerPrefs.SetInt(k_PrefsPrefix + group + ".Muted", s_Graph.IsMuted(group) ? 1 : 0);
    }

    private static void LoadGroup(string group)
    {
        string volumeKey = k_PrefsPrefix + group + ".Volume";
        if (PlayerPrefs.HasKey(volumeKey)) s_Graph.SetVolume(group, PlayerPrefs.GetFloat(volumeKey, 1f));
        s_Graph.SetMuted(group, PlayerPrefs.GetInt(k_PrefsPrefix + group + ".Muted", 0) != 0);
    }

    private static Output? Find(AudioSource source) => s_Outputs.Find(o => o.Source != null && o.Source.Entity.ID == source.Entity.ID);

    private static void ApplyAll()
    {
        for (int i = s_Outputs.Count - 1; i >= 0; i--)
        {
            if (!Apply(s_Outputs[i])) s_Outputs.RemoveAt(i);
        }
        Changed?.Invoke();
    }

    // False once the source's entity or the stream is gone.
    private static bool Apply(Output output)
    {
        float level = output.BaseVolume * s_Graph.GetEffectiveVolume(output.Group);
        if (MathF.Abs(level - output.Applied) < 0.0001f) return true;
        try
        {
            if (output.Source != null) output.Source.Volume = level;
            else if (output.Stream != null) output.Stream.Volume = level;
            output.Applied = level;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async void StartFadeLoop()
    {
        if (s_FadeLoopRunning) return;
        s_FadeLoopRunning = true;
        try
        {
            // An explicit token keeps the waits from inheriting the calling script's DestroyToken.
            s_FadeLifetime ??= new CancellationTokenSource();
            int lastFrame = Time.FrameCount;
            while (s_Graph.IsFading)
            {
                await new WaitForNextFrame(s_FadeLifetime.Token);
                // Every loaded scene pumps the scheduler, so with additive scenes this resumes more than once per frame.
                int frame = Time.FrameCount;
                if (frame == lastFrame) continue;
                lastFrame = frame;
                s_Graph.Advance(Time.UnscaledDeltaTime);
                ApplyAll();
            }
        }
        finally
        {
            s_FadeLoopRunning = false;
        }
    }
}

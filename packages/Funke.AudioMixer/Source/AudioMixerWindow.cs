using Funke.Editor;

namespace Funke.AudioMixer;

/// <summary>
/// Tools › Audio › Audio Mixer in the editor: a fader, mute switch and effective-volume bar per group. Live in Play mode;
/// in edit mode it changes the saved player settings the game starts with.
/// </summary>
[EditorWindow("Audio Mixer", Menu = "Audio/Audio Mixer", Width = 330, Height = 340)]
public sealed class AudioMixerWindow : EditorWindow
{
    private const float k_ColumnWidth = 72f;
    private const float k_FaderHeight = 150f;
    private const float k_MinDecibels = -60f;

    public override void OnGUI()
    {
        AudioMixerGraph graph = AudioMixer.Graph;
        EditorGUI.WrappedLabel(EditorGUI.IsPlaying
            ? "Live: changes apply to the running game."
            : "Edit mode: changes go to the saved player settings (PlayerPrefs) the game starts with.");
        EditorGUI.Space();

        bool first = true;
        foreach ((string group, int depth) in graph.GetTree())
        {
            if (!first) EditorGUI.SameLine(12f);
            first = false;
            DrawChannel(graph, group, depth);
        }

        EditorGUI.Space();
        EditorGUI.Separator();
        AudioMixer.AutoSave = EditorGUI.Toggle("Save changes", AudioMixer.AutoSave);
        EditorGUI.SameLine();
        if (EditorGUI.Button("Reset Volumes")) AudioMixer.ResetVolumes();
        if (graph.IsFading)
        {
            EditorGUI.SameLine();
            EditorGUI.HintLabel("fading...");
        }
    }

    private static void DrawChannel(AudioMixerGraph graph, string group, int depth)
    {
        EditorGUI.PushID(group);
        EditorGUI.BeginGroup();

        EditorGUI.Label(group);
        EditorGUI.HintLabel(depth == 0 ? "master" : "in " + graph.GetParent(group));

        float volume = graph.GetVolume(group);
        float decibels = System.Math.Max(AudioMixerGraph.LinearToDecibels(volume), k_MinDecibels);
        float edited = EditorGUI.VerticalSlider("##fader", decibels, k_MinDecibels, 0f, k_ColumnWidth, k_FaderHeight,
            volume <= 0f ? "off" : "%.0f dB");
        if (edited != decibels)
            AudioMixer.SetVolume(group, edited <= k_MinDecibels ? 0f : AudioMixerGraph.DecibelsToLinear(edited));

        // What the group's sounds actually get: its own volume times its parents', 0 while any of them is muted.
        EditorGUI.ProgressBar(graph.GetEffectiveVolume(group), null, k_ColumnWidth, 6f);

        bool muted = graph.IsMuted(group);
        bool editedMute = EditorGUI.Toggle("Mute", muted);
        if (editedMute != muted) AudioMixer.SetMuted(group, editedMute);

        EditorGUI.EndGroup();
        EditorGUI.PopID();
    }
}

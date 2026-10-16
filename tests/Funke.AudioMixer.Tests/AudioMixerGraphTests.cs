using System;
using System.Collections.Generic;
using System.Linq;
using Funke.AudioMixer;
using Xunit;

namespace Funke.AudioMixer.Tests;

public class AudioMixerGraphTests
{
    [Fact]
    public void Starts_with_master_music_and_sfx()
    {
        var graph = new AudioMixerGraph();
        Assert.Equal(new[] { "Master", "Music", "SFX" }, graph.GroupNames.OrderBy(n => n));
        Assert.Equal(1f, graph.GetEffectiveVolume(AudioMixerGraph.Music));
    }

    [Fact]
    public void Effective_volume_multiplies_the_parent_chain_and_mutes_silence_it()
    {
        var graph = new AudioMixerGraph();
        graph.AddGroup("UI", AudioMixerGraph.Sfx, 0.5f);
        graph.SetVolume(AudioMixerGraph.Master, 0.8f);
        graph.SetVolume(AudioMixerGraph.Sfx, 0.5f);
        Assert.Equal(0.2f, graph.GetEffectiveVolume("UI"), 4);

        graph.SetMuted(AudioMixerGraph.Sfx, true);
        Assert.Equal(0f, graph.GetEffectiveVolume("UI"));
        Assert.Equal(0.8f, graph.GetEffectiveVolume(AudioMixerGraph.Music), 4);
    }

    [Fact]
    public void Group_names_ignore_case_and_volumes_clamp()
    {
        var graph = new AudioMixerGraph();
        graph.SetVolume("music", 3f);
        Assert.Equal(1f, graph.GetVolume(AudioMixerGraph.Music));
        graph.SetVolume("MUSIC", float.NaN);
        Assert.Equal(0f, graph.GetVolume(AudioMixerGraph.Music));
    }

    [Fact]
    public void Adding_rejects_unknown_parents_and_cycles_and_moves_existing_groups()
    {
        var graph = new AudioMixerGraph();
        Assert.Throws<ArgumentException>(() => graph.AddGroup("Voice", "Nope"));
        graph.AddGroup("Voice", AudioMixerGraph.Music);
        Assert.Throws<ArgumentException>(() => graph.AddGroup(AudioMixerGraph.Music, "Voice"));
        Assert.Throws<ArgumentException>(() => graph.AddGroup(" "));

        graph.AddGroup("Voice", AudioMixerGraph.Sfx);
        graph.SetMuted(AudioMixerGraph.Music, true);
        Assert.Equal(1f, graph.GetEffectiveVolume("Voice"));
        Assert.Throws<ArgumentException>(() => graph.GetVolume("Missing"));
    }

    [Fact]
    public void Fades_advance_to_their_target_and_set_volume_cancels_them()
    {
        var graph = new AudioMixerGraph();
        graph.FadeVolume(AudioMixerGraph.Music, 0f, 2f);
        Assert.True(graph.IsFading);
        Assert.True(graph.Advance(1f));
        Assert.Equal(0.5f, graph.GetVolume(AudioMixerGraph.Music), 4);
        Assert.False(graph.Advance(5f));
        Assert.Equal(0f, graph.GetVolume(AudioMixerGraph.Music));
        Assert.False(graph.IsFading);

        graph.FadeVolume(AudioMixerGraph.Sfx, 0f, 1f);
        graph.SetVolume(AudioMixerGraph.Sfx, 0.7f);
        Assert.False(graph.IsFading);
        Assert.Equal(0.7f, graph.GetVolume(AudioMixerGraph.Sfx), 4);

        graph.FadeVolume(AudioMixerGraph.Sfx, 0.2f, 0f);
        Assert.Equal(0.2f, graph.GetVolume(AudioMixerGraph.Sfx), 4);
    }

    [Theory]
    [InlineData(2f, 60)]
    [InlineData(1f, 60)]
    [InlineData(0.5f, 60)]
    [InlineData(1f, 144)]
    public void Fades_end_on_the_frame_their_duration_is_reached(float seconds, int framesPerSecond)
    {
        var graph = new AudioMixerGraph();
        graph.FadeVolume(AudioMixerGraph.Music, 0f, seconds);
        int frames = (int)MathF.Round(seconds * framesPerSecond);
        float step = 1f / framesPerSecond;
        for (int i = 1; i < frames; i++)
        {
            Assert.True(graph.Advance(step), $"fade ended early at frame {i} of {frames}");
        }
        Assert.False(graph.Advance(step));
        Assert.Equal(0f, graph.GetVolume(AudioMixerGraph.Music));
    }

    [Fact]
    public void Snapshots_fade_only_their_groups()
    {
        var graph = new AudioMixerGraph();
        graph.DefineSnapshot("Paused", new Dictionary<string, float> { [AudioMixerGraph.Music] = 0.3f });
        graph.TransitionTo("Paused", 0.5f);
        graph.Advance(0.5f);
        Assert.Equal(0.3f, graph.GetVolume(AudioMixerGraph.Music), 4);
        Assert.Equal(1f, graph.GetVolume(AudioMixerGraph.Sfx));

        Assert.Throws<ArgumentException>(() => graph.TransitionTo("Missing", 1f));
        Assert.Throws<ArgumentException>(() => graph.DefineSnapshot("Bad", new Dictionary<string, float> { ["Nope"] = 1f }));
    }

    [Fact]
    public void Version_changes_with_every_audible_change()
    {
        var graph = new AudioMixerGraph();
        int version = graph.Version;
        graph.SetMuted(AudioMixerGraph.Sfx, true);
        Assert.NotEqual(version, graph.Version);
        version = graph.Version;
        graph.FadeVolume(AudioMixerGraph.Music, 0f, 1f);
        graph.Advance(0.25f);
        Assert.NotEqual(version, graph.Version);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-6f, 0.501f)]
    [InlineData(-80f, 0f)]
    public void Decibel_conversion_round_trips(float decibels, float linear)
    {
        Assert.Equal(linear, AudioMixerGraph.DecibelsToLinear(decibels), 2);
        if (linear > 0f) Assert.Equal(decibels, AudioMixerGraph.LinearToDecibels(linear), 1);
        else Assert.Equal(-80f, AudioMixerGraph.LinearToDecibels(linear));
    }

    [Fact]
    public void Tree_lists_parents_before_children_with_depth()
    {
        var graph = new AudioMixerGraph();
        graph.AddGroup("Voice", AudioMixerGraph.Sfx);
        graph.AddGroup("Ambience", AudioMixerGraph.Music);
        Assert.Equal(new[]
        {
            ("Master", 0), ("Music", 1), ("Ambience", 2), ("SFX", 1), ("Voice", 2),
        }, graph.GetTree());
        Assert.Equal(AudioMixerGraph.Sfx, graph.GetParent("Voice"));
        Assert.Null(graph.GetParent(AudioMixerGraph.Master));
    }
}

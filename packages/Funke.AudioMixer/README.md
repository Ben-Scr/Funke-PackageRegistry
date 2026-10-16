# Funke.AudioMixer

Volume groups for the whole game, so you don't build your own sound settings. Every sound plays through a group;
its loudness is its own volume times the group's effective volume (the group and all its parents, silent while any
of them is muted).

Built-in groups: `Master` → `Music`, `SFX`. Add your own with `AudioMixer.AddGroup("UI", AudioMixer.Sfx)`.

## Install

Package Manager → **Funke.AudioMixer** → Install, or `funke package add Funke.AudioMixer`. C# only, works with or
without the 2D/3D modules.

## Use

```csharp
using Funke;
using Funke.AudioMixer;

public class MusicPlayer : EntityScript
{
    public override void OnStart()
    {
        var source = GetComponent<AudioSource>()!;
        AudioMixer.Attach(source, AudioMixer.Music);      // follows the Music group from now on
        source.Play();
    }
}

AudioMixer.PlayOneShot(jumpClip, AudioMixer.Sfx);          // one-shots through a group
AudioMixer.Attach(stream, AudioMixer.Music);               // AudioStream (files outside the project) too

AudioMixer.SetVolume(AudioMixer.Music, 0.5f);              // linear 0..1, or SetVolumeDecibels
AudioMixer.SetMuted(AudioMixer.Sfx, true);
AudioMixer.FadeVolume(AudioMixer.Music, 0f, 1.5f);         // unscaled time, runs while paused

AudioMixer.DefineSnapshot("Paused", (AudioMixer.Music, 0.3f), (AudioMixer.Sfx, 0f));
AudioMixer.TransitionTo("Paused", 0.4f);
```

**Settings screen:** `AudioMixer.BindSlider(slider, AudioMixer.Music)` keeps a 0..1 Slider and the group in sync,
`AudioMixer.BindMuteToggle(toggle, AudioMixer.Sfx)` does the same for a Toggle (on = audible). Both return an action
that unbinds them (call it in `OnDestroy`).

**Saving:** volumes and mutes are written to `PlayerPrefs` on every change (`AutoSave`, default on) and restored on
first use; `SaveVolumes()`, `LoadVolumes()` and `ResetVolumes()` do it explicitly. Fades and snapshots are not saved.

**Source volume:** an attached AudioSource keeps its own level; change it with `AudioMixer.SetSourceVolume(source, v)`.
Writing `AudioSource.Volume` directly is overwritten on the next mixer change.

`AudioMixer.Graph` (`AudioMixerGraph`) is the engine-free group tree behind the facade, if you need it in tools or tests.

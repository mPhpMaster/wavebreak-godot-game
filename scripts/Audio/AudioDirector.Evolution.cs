using Godot;
using Wavebreak.Core;

namespace Wavebreak.Audio;

/// <summary>The evolution sting: one rising swell and a gold chord, louder than
/// a level-up because it happens a few times a run at most. Played through the
/// director's own 2D voices like the chest sounds.</summary>
public sealed partial class AudioDirector
{
    private static AudioStreamWav? _evolveStream;

    /// <summary>Evolution stings played this run.</summary>
    public int EvolutionsHeard { get; private set; }

    private void SubscribeEvolutionSound()
    {
        _evolveStream ??= SfxSynth.ToStream(SfxSynth.BuildEvolution());
        EventBus.Subscribe<WeaponEvolved>(OnWeaponEvolved);
    }

    private void UnsubscribeEvolutionSound() => EventBus.Unsubscribe<WeaponEvolved>(OnWeaponEvolved);

    private void OnWeaponEvolved(WeaponEvolved e)
    {
        if (_evolveStream is null) return;
        Voice? voice = null;
        foreach (Voice v in _voices2D)
        {
            if (!IsPlaying(v.Player)) { voice = v; break; }
            if (voice is null || v.Priority < voice.Priority) voice = v;
        }
        if (voice?.Player is not AudioStreamPlayer p) { _dropped++; return; }
        if (IsPlaying(p)) _stolen++;
        p.Stream = _evolveStream;
        p.PitchScale = 1f;
        p.VolumeDb = -6f;
        p.Play();
        voice.Id = (Sfx)(-1);
        voice.Priority = 7;
        voice.StartedAt = Now();
        EvolutionsHeard++;
    }
}

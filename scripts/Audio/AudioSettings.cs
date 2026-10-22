using System;
using Godot;

namespace Wavebreak.Audio;

/// <summary>Which volume a control acts on. Each maps to one audio bus.</summary>
public enum VolumeChannel { Master, Sfx, Music }

/// <summary>
/// The player's volume preferences: master, effects and music levels plus a
/// mute switch, saved to <c>user://settings.cfg</c> and applied to the buses.
///
/// Levels are stored as linear 0..1 in tenths, not decibels: "5 of 10" is what
/// the toast shows and what a player can reason about, and equal steps of a
/// linear slider map to roughly equal-sounding steps once converted to dB.
///
/// Static because it is process-wide state: the AudioServer's buses outlive a
/// scene reload, and so must the preference that drives them. Loaded once.
/// </summary>
public static class AudioSettings
{
    public const string MasterBus = "Master";
    public const string SfxBus = "Sfx";
    public const string MusicBus = "Music";

    private const string Path = "user://settings.cfg";
    private const string Section = "audio";

    /// <summary>One press of a volume key.</summary>
    public const float Step = 0.1f;

    // Defaults: comfortably low. A first launch should never be the loudest
    // thing on the desk; anyone who wants more presses a key.
    private const float DefaultMaster = 0.5f;
    private const float DefaultSfx = 0.8f;
    private const float DefaultMusic = 0.5f;

    private static readonly float[] Levels = { DefaultMaster, DefaultSfx, DefaultMusic };
    private static bool _loaded;

    public static bool Muted { get; private set; }

    public static float Get(VolumeChannel channel) => Levels[(int)channel];

    /// <summary>Reads the saved file once per process. A missing or unreadable
    /// file leaves the defaults; nothing about audio is worth failing a launch.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;

        Levels[(int)VolumeChannel.Master] = Read(cfg, "master", DefaultMaster);
        Levels[(int)VolumeChannel.Sfx] = Read(cfg, "sfx", DefaultSfx);
        Levels[(int)VolumeChannel.Music] = Read(cfg, "music", DefaultMusic);
        Muted = cfg.GetValue(Section, "muted", false).AsBool();
    }

    private static float Read(ConfigFile cfg, string key, float fallback)
        => Quantise(cfg.GetValue(Section, key, fallback).AsSingle());

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep any other sections a future settings screen adds
        cfg.SetValue(Section, "master", Get(VolumeChannel.Master));
        cfg.SetValue(Section, "sfx", Get(VolumeChannel.Sfx));
        cfg.SetValue(Section, "music", Get(VolumeChannel.Music));
        cfg.SetValue(Section, "muted", Muted);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[audio] could not save {Path}: {err}");
    }

    /// <summary>Raise or lower one channel by whole steps. Changing a level also
    /// unmutes: pressing "louder" while muted and hearing nothing reads as broken.</summary>
    public static void Nudge(VolumeChannel channel, int steps)
    {
        Levels[(int)channel] = Quantise(Get(channel) + steps * Step);
        Muted = false;
        Apply();
        Save();
    }

    public static void ToggleMute()
    {
        Muted = !Muted;
        Apply();
        Save();
    }

    /// <summary>Push the levels onto the buses. Zero is a bus mute rather than
    /// -inf dB, which the mixer would still be spending time on.</summary>
    public static void Apply()
    {
        SetBus(MasterBus, Get(VolumeChannel.Master), Muted);
        SetBus(SfxBus, Get(VolumeChannel.Sfx), false);
        SetBus(MusicBus, Get(VolumeChannel.Music), false);
    }

    private static void SetBus(string name, float level, bool mute)
    {
        int i = AudioServer.GetBusIndex(name);
        if (i < 0) return;
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Math.Max(level, 0.001f)));
        AudioServer.SetBusMute(i, mute || level <= 0.001f);
    }

    /// <summary>Snap to the nearest tenth so repeated presses never drift into
    /// 0.30000004 and a saved value round-trips exactly.</summary>
    private static float Quantise(float v) => Math.Clamp(MathF.Round(v / Step) * Step, 0f, 1f);

    /// <summary>Short human text for the toast, e.g. "MASTER 5/10".</summary>
    public static string Describe(VolumeChannel channel)
    {
        if (channel == VolumeChannel.Master && Muted) return "SOUND MUTED  (M)";
        int tenths = (int)MathF.Round(Get(channel) * 10f);
        string name = channel switch
        {
            VolumeChannel.Sfx => "EFFECTS",
            VolumeChannel.Music => "MUSIC",
            _ => "VOLUME",
        };
        return $"{name}  {new string('|', tenths)}{new string('.', 10 - tenths)}  {tenths}/10";
    }
}

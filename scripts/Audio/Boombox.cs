using Godot;
using Wavebreak.Meta;

namespace Wavebreak.Audio;

/// <summary>How the Boombox picks music.</summary>
public enum BoomboxMode
{
    /// <summary>Each map its own track, bosses and the swarm theirs (the default).</summary>
    MapDefault,
    /// <summary>A random owned loop at every musical moment.</summary>
    Shuffle,
    /// <summary>One chosen loop everywhere.</summary>
    Pinned,
}

/// <summary>
/// The Boombox choice (mode and pinned track), saved in the <c>[boombox]</c>
/// section of <c>user://settings.cfg</c> beside the volume levels. A player
/// preference, not progress, so it lives with the settings and not the profile.
/// Automated launches read nothing and write nothing.
/// </summary>
public static class Boombox
{
    private const string Path = "user://settings.cfg";
    private const string Section = "boombox";
    private static bool _loaded;

    public static BoomboxMode Mode { get; private set; } = BoomboxMode.MapDefault;
    public static string Pinned { get; private set; } = "";

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Launch.Parse();
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        string mode = cfg.GetValue(Section, "mode", "map").AsString();
        Mode = mode switch { "shuffle" => BoomboxMode.Shuffle, "pinned" => BoomboxMode.Pinned, _ => BoomboxMode.MapDefault };
        Pinned = cfg.GetValue(Section, "track", "").AsString();
        if (Mode == BoomboxMode.Pinned && MusicTracks.ById(Pinned) is not { Loop: true }) Mode = BoomboxMode.MapDefault;
    }

    public static void SetMode(BoomboxMode mode)
    {
        Load();
        if (mode == BoomboxMode.Pinned && !MusicTracks.IsUnlocked(Pinned)) return;
        Mode = mode;
        Save();
    }

    /// <summary>Pin a loop the player owns; it plays everywhere.</summary>
    public static bool Pin(string trackId)
    {
        Load();
        if (MusicTracks.ById(trackId) is not { Loop: true } t || !MusicTracks.IsUnlocked(t)) return false;
        Pinned = trackId;
        Mode = BoomboxMode.Pinned;
        Save();
        return true;
    }

    /// <summary>Cycle Map default → Shuffle → Pinned (when something is pinned).</summary>
    public static void CycleMode()
    {
        Load();
        BoomboxMode next = Mode switch
        {
            BoomboxMode.MapDefault => BoomboxMode.Shuffle,
            BoomboxMode.Shuffle => MusicTracks.IsUnlocked(Pinned) ? BoomboxMode.Pinned : BoomboxMode.MapDefault,
            _ => BoomboxMode.MapDefault,
        };
        Mode = next;
        Save();
    }

    public static string Describe() => Mode switch
    {
        BoomboxMode.Shuffle => Loc.T("Shuffle"),
        BoomboxMode.Pinned => Loc.F("Pinned: {0}", MusicTracks.ById(Pinned)?.Title ?? Pinned),
        _ => Loc.T("Map default"),
    };

    private static void Save()
    {
        Launch.Parse();
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep the audio and other sections
        cfg.SetValue(Section, "mode", Mode switch { BoomboxMode.Shuffle => "shuffle", BoomboxMode.Pinned => "pinned", _ => "map" });
        cfg.SetValue(Section, "track", Pinned);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[boombox] could not save {Path}: {err}");
    }
}

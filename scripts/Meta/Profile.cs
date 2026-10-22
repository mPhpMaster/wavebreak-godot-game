using System;
using System.Collections.Generic;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// Everything that survives between runs: lifetime stats, earned unlocks,
/// discovered secrets and the last character picked. Saved to
/// <c>user://profile.cfg</c>.
///
/// Static and loaded once per process, like <see cref="Audio.AudioSettings"/>:
/// the run scene is reloaded on every restart and the profile must not be.
///
/// Robustness is the whole design brief. A profile is the one file a player
/// cannot recreate, so:
///   * a missing file is a new player, not an error;
///   * an unreadable file is moved aside to <c>.corrupt</c> (never overwritten)
///     and the game starts fresh, so a bad write cannot cost a launch;
///   * every value is type-checked on read — a hand-edited "kills = banana"
///     reads as 0 rather than throwing;
///   * saves go to a temp file first and are renamed over the real one, so a
///     crash mid-save leaves the previous profile intact.
///
/// Unlock ids are stored, not booleans per unlock: an id that a later version
/// removes is simply ignored, and a new unlock starts locked without migration.
/// </summary>
public static class Profile
{
    public const string DefaultPath = "user://profile.cfg";
    private const int Version = 1;

    private static readonly Dictionary<string, long> Stats = new();
    private static readonly HashSet<string> UnlockSet = new();
    private static readonly HashSet<string> SecretSet = new();

    private static bool _loaded;

    /// <summary>Where the profile lives, or null when this process must not
    /// persist anything (an automated run without an explicit --profile).</summary>
    public static string? Path { get; private set; }

    public static string SelectedCharacter { get; set; } = Characters.DefaultId;

    // ------------------------------------------------------------ stat keys
    // Strings rather than an enum so the file stays readable and a new stat is
    // one constant, not a migration.

    public const string Runs = "runs";
    public const string Wins = "wins";
    public const string TotalKills = "total_kills";
    public const string ElitesKilled = "elites_killed";
    public const string BossesKilled = "bosses_killed";
    public const string BestSeconds = "best_seconds";
    public const string BestKills = "best_kills";
    public const string BestLevel = "best_level";
    public const string ChestsOpened = "chests_opened";
    public const string SecretsFound = "secrets_found";
    public const string TotalSlams = "total_slams";

    // -------------------------------------------------------------- loading

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        Launch.Parse();
        Path = Launch.ProfilePath ?? (Launch.Automated ? null : DefaultPath);
        if (Path is null)
        {
            GD.Print("[profile] in-memory (automated run, nothing is saved)");
            return;
        }

        if (!FileAccess.FileExists(Path))
        {
            GD.Print($"[profile] new profile at {Path}");
            return;
        }

        var cfg = new ConfigFile();
        Error err = cfg.Load(Path);
        if (err != Error.Ok)
        {
            Quarantine(err.ToString());
            return;
        }

        foreach (string key in SafeKeys(cfg, "stats"))
        {
            Variant v = cfg.GetValue("stats", key);
            if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                Stats[key] = Math.Max(0L, v.AsInt64());
        }

        ReadSet(cfg, "unlocks", UnlockSet);
        ReadSet(cfg, "secrets", SecretSet);

        Variant sel = cfg.GetValue("profile", "character", Characters.DefaultId);
        if (sel.VariantType == Variant.Type.String) SelectedCharacter = sel.AsString();

        GD.Print($"[profile] loaded {Path}: {UnlockSet.Count} unlocks, {SecretSet.Count} secrets, {Get(Runs)} runs");
    }

    private static IEnumerable<string> SafeKeys(ConfigFile cfg, string section)
        => cfg.HasSection(section) ? cfg.GetSectionKeys(section) : Array.Empty<string>();

    /// <summary>Sets are stored as key = true, one per line, so a diff of two
    /// profiles is readable and a hand-removed line removes exactly one entry.</summary>
    private static void ReadSet(ConfigFile cfg, string section, HashSet<string> into)
    {
        foreach (string key in SafeKeys(cfg, section))
            if (cfg.GetValue(section, key, false).AsBool()) into.Add(key);
    }

    private static void Quarantine(string why)
    {
        string aside = Path + ".corrupt";
        // Rename rather than delete: a corrupt profile is still the only copy of
        // whatever the player earned, and someone may want to recover it.
        Error moved = DirAccess.RenameAbsolute(Abs(Path!), Abs(aside));
        GD.PushWarning($"[profile] could not read {Path} ({why}); moved aside to {aside} ({moved}) and starting fresh");
    }

    // ---------------------------------------------------------------- save

    public static void Save()
    {
        if (Path is null) return;

        var cfg = new ConfigFile();
        cfg.SetValue("profile", "version", Version);
        cfg.SetValue("profile", "character", SelectedCharacter);
        foreach (var (key, value) in Stats) cfg.SetValue("stats", key, value);
        foreach (string id in UnlockSet) cfg.SetValue("unlocks", id, true);
        foreach (string id in SecretSet) cfg.SetValue("secrets", id, true);

        string temp = Path + ".tmp";
        Error err = cfg.Save(temp);
        if (err == Error.Ok)
        {
            // Godot's rename refuses to replace on some platforms; remove first.
            if (FileAccess.FileExists(Path)) DirAccess.RemoveAbsolute(Abs(Path));
            err = DirAccess.RenameAbsolute(Abs(temp), Abs(Path));
        }
        if (err != Error.Ok) GD.PushWarning($"[profile] could not save {Path}: {err}");
    }

    /// <summary>DirAccess's absolute calls want an OS path; user:// is resolved here.</summary>
    private static string Abs(string path) => ProjectSettings.GlobalizePath(path);

    // ---------------------------------------------------------------- stats

    public static long Get(string stat) => Stats.TryGetValue(stat, out long v) ? v : 0L;

    public static void Add(string stat, long amount)
    {
        if (amount <= 0) return;
        Stats[stat] = Get(stat) + amount;
    }

    /// <summary>Keep the larger of the stored and given value: personal bests.</summary>
    public static void Best(string stat, long value)
    {
        if (value > Get(stat)) Stats[stat] = value;
    }

    // ------------------------------------------------------ unlocks/secrets

    public static bool IsUnlocked(string id) => UnlockSet.Contains(id);
    public static IReadOnlyCollection<string> UnlockedIds => UnlockSet;

    /// <summary>Returns true only the first time, so the caller can announce it.</summary>
    public static bool Unlock(string id) => UnlockSet.Add(id);

    public static bool HasFoundSecret(string kind) => SecretSet.Contains(kind);
    public static IReadOnlyCollection<string> FoundSecrets => SecretSet;
    public static bool FindSecret(string kind) => SecretSet.Add(kind);
}

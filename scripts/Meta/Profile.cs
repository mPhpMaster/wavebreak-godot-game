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
///     and the game recovers from a complete <c>.tmp</c> or the <c>.bak</c> of
///     the last good file, starting fresh only when neither reads, so a bad
///     write cannot cost a launch;
///   * every value is type-checked on read — a hand-edited "kills = banana"
///     reads as 0 rather than throwing;
///   * saves go to a temp file first, verified, and replace the real one in a
///     single atomic step, so a crash mid-save leaves a whole profile behind
///     (<see cref="WriteSafely"/>);
///   * <c>[profile] version</c> is read on load and old files are stepped up
///     by <see cref="Migrate"/>.
///
/// Unlock ids are stored, not booleans per unlock: an id that a later version
/// removes is simply ignored, and a new unlock starts locked without migration.
/// </summary>
public static partial class Profile
{
    public const string DefaultPath = "user://profile.cfg";
    private const int Version = 2;

    // Written as the file's last section, so a truncated file lacks it.
    private const string EndSection = "end", EndKey = "complete";

    private static readonly Dictionary<string, long> Stats = new();
    private static readonly HashSet<string> UnlockSet = new();

    // character id -> chosen skin id. Absent means the character's base skin.
    private static readonly Dictionary<string, string> SkinChoice = new();

    public static string? SelectedSkin(string characterId)
        => SkinChoice.TryGetValue(characterId, out string? id) ? id : null;

    public static void SelectSkin(string characterId, string skinId) => SkinChoice[characterId] = skinId;
    private static readonly HashSet<string> SecretSet = new();

    // Spendable and settable meta values (currency, shop levels, difficulty).
    // Kept apart from Stats, which only ever grow; these can go down.
    private static readonly Dictionary<string, long> MetaValues = new();

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
        // A release's test flags can cheat, so its automated runs never persist.
        Path = Launch.Automated && !Launch.DevAllowed ? null : Launch.ProfilePath ?? (Launch.Automated ? null : DefaultPath);
        if (Path is null)
        {
            GD.Print("[profile] in-memory (automated run, nothing is saved)");
            return;
        }

        ConfigFile? cfg = LoadBest(Path, out bool recovered);
        if (cfg is null)
        {
            GD.Print($"[profile] new profile at {Path}");
            return;
        }

        int version = ReadVersion(cfg);
        if (version > Version) GD.PushWarning($"[profile] {Path} is version {version}, newer than this game's {Version}; reading what is understood");
        Migrate(cfg, version);

        foreach (string key in SafeKeys(cfg, "stats"))
        {
            Variant v = cfg.GetValue("stats", key);
            if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                Stats[key] = Math.Max(0L, v.AsInt64());
        }

        foreach (string key in SafeKeys(cfg, "meta"))
        {
            Variant v = cfg.GetValue("meta", key);
            if (v.VariantType is Variant.Type.Int or Variant.Type.Float)
                MetaValues[key] = Math.Max(0L, v.AsInt64());
        }

        ReadSet(cfg, "unlocks", UnlockSet);
        ReadSet(cfg, "secrets", SecretSet);

        foreach (string key in SafeKeys(cfg, "skins"))
        {
            Variant v = cfg.GetValue("skins", key, "");
            if (v.VariantType == Variant.Type.String && v.AsString().Length > 0) SkinChoice[key] = v.AsString();
        }

        Variant sel = cfg.GetValue("profile", "character", Characters.DefaultId);
        if (sel.VariantType == Variant.Type.String) SelectedCharacter = sel.AsString();

        GD.Print($"[profile] loaded {Path}: {UnlockSet.Count} unlocks, {SecretSet.Count} secrets, {Get(Runs)} runs");
        if (recovered) Save();   // profile.cfg itself comes back now, not at the next run end
    }

    /// <summary>The newest readable copy of the profile, or null when there is
    /// none (a new player). A bad <paramref name="path"/> is quarantined first;
    /// then a complete .tmp (a save that died just before its rename, so the
    /// newest data) is preferred over the .bak.</summary>
    internal static ConfigFile? LoadBest(string path, out bool recovered)
    {
        recovered = false;
        if (FileAccess.FileExists(path))
        {
            if (ReadComplete(path, out ConfigFile main, out string why)) return main;
            Quarantine(path, why);
        }
        foreach (string spare in new[] { path + ".tmp", path + ".bak" })
        {
            if (!FileAccess.FileExists(spare)) continue;
            if (!ReadComplete(spare, out ConfigFile copy, out string why))
            {
                GD.PushWarning($"[profile] ignoring {spare} ({why})");
                continue;
            }
            GD.PushWarning($"[profile] recovered {path} from {spare}");
            recovered = true;
            return copy;
        }
        return null;
    }

    /// <summary>Loads and tells a whole file from a damaged one. A file of
    /// version 2 or later must reach the end marker Save writes last; older and
    /// hand-written files have none and only need to parse and hold something.</summary>
    private static bool ReadComplete(string path, out ConfigFile cfg, out string why)
    {
        cfg = new ConfigFile();
        Error err = cfg.Load(path);
        why = err != Error.Ok ? err.ToString()
            : cfg.GetSections().Length == 0 ? "empty"
            : ReadVersion(cfg) >= 2 && !cfg.HasSectionKey(EndSection, EndKey) ? "truncated"
            : "";
        return why.Length == 0;
    }

    /// <summary>The file's [profile] version; 1 when absent or not a number
    /// (version 1 files are the oldest there are).</summary>
    private static int ReadVersion(ConfigFile cfg)
    {
        Variant v = cfg.GetValue("profile", "version", 1);
        return v.VariantType is Variant.Type.Int or Variant.Type.Float ? Math.Max(1, v.AsInt32()) : 1;
    }

    /// <summary>Bring a file of version <paramref name="from"/> up to
    /// <see cref="Version"/>, one step at a time, before anything reads it. A
    /// change of format adds a case here and bumps <see cref="Version"/>. A newer
    /// file runs no step: unknown ids are kept and saved back as they came.</summary>
    private static void Migrate(ConfigFile cfg, int from)
    {
        for (int v = from; v < Version; v++)
        {
            switch (v)
            {
                case 1: break;   // 1 -> 2 only added the end marker; Save writes it
            }
        }
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

    private static void Quarantine(string path, string why)
    {
        string aside = path + ".corrupt";
        // Never over an earlier quarantine: each one may be the only copy.
        for (int n = 1; FileAccess.FileExists(aside); n++) aside = $"{path}.corrupt.{n}";
        // Rename rather than delete: a corrupt profile is still the only copy of
        // whatever the player earned, and someone may want to recover it.
        Error moved = DirAccess.RenameAbsolute(Abs(path), Abs(aside));
        GD.PushWarning($"[profile] could not read {path} ({why}); moved aside to {aside} ({moved})");
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
        foreach (var (character, skin) in SkinChoice) cfg.SetValue("skins", character, skin);
        foreach (var (key, value) in MetaValues) cfg.SetValue("meta", key, value);
        cfg.SetValue(EndSection, EndKey, true);   // last: a file that has it was written whole

        Error err = WriteSafely(cfg, Path);
        if (err != Error.Ok) GD.PushWarning($"[profile] could not save {Path}: {err}");
    }

    /// <summary>Temp file, checked and flushed; the old file copied to .bak; then
    /// one atomic replace. Every step leaves a whole profile.cfg in place, and no
    /// moment passes with none, so a crash or power cut costs at most this save.</summary>
    internal static Error WriteSafely(ConfigFile cfg, string path)
    {
        string temp = path + ".tmp";
        Error err = cfg.Save(temp);
        if (err != Error.Ok) return err;
        if (!ReadComplete(temp, out _, out string why))
        {
            GD.PushWarning($"[profile] {temp} did not read back ({why})");
            return Error.FileCorrupt;
        }
        try
        {
            string absolute = Abs(path), absoluteTemp = Abs(temp);
            using (var f = new System.IO.FileStream(absoluteTemp, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
                f.Flush(flushToDisk: true);
            // Only a good file may replace the last good backup, and the backup
            // gets there by rename too, so it is never half a copy.
            if (FileAccess.FileExists(path) && ReadComplete(path, out _, out _))
            {
                System.IO.File.Copy(absolute, absolute + ".bak.tmp", overwrite: true);
                MoveOver(absolute + ".bak.tmp", absolute + ".bak");
            }
            MoveOver(absoluteTemp, absolute);
            return Error.Ok;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"[profile] replace failed: {e.Message}");
            return Error.Failed;
        }
    }

    /// <summary>Atomic replace. Windows refuses it while anything else holds the
    /// target for a moment (an antivirus scan, the indexer; measured with a plain
    /// File.Exists loop), so it gets a few short retries.</summary>
    private static void MoveOver(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                System.IO.File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < 8 && e is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(attempt * 10);
            }
        }
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

    // ----------------------------------------------------------------- meta
    // Values that can go down as well as up: Pearls, shop levels, the chosen
    // difficulty. Never negative; zero is not stored.

    public static long GetMeta(string key) => MetaValues.TryGetValue(key, out long v) ? v : 0L;

    public static void SetMeta(string key, long value)
    {
        if (value <= 0) MetaValues.Remove(key);
        else MetaValues[key] = value;
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

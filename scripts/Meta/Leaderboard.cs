using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Core;
using Wavebreak.Game;

namespace Wavebreak.Meta;

/// <summary>One finished run on the local leaderboard.</summary>
public sealed class LeaderboardEntry
{
    /// <summary>Unix time the run ended. Doubles as the entry's id: two runs
    /// cannot end in the same second on one machine.</summary>
    public long When;
    public bool Won;
    public float Seconds;
    public int Kills;
    public int Level;
    public int Phase;
    public string Character = "";
    public string Difficulty = "";

    public string Clock => RunState.Format(Seconds);

    public string CharacterName => Characters.ById(Character)?.DisplayName ?? Character;

    /// <summary>"2026-10-23 18:04", local time.</summary>
    public string DateText
    {
        get
        {
            long bias = Time.GetTimeZoneFromSystem()["bias"].AsInt64(); // minutes from UTC
            string s = Time.GetDatetimeStringFromUnixTime(When + bias * 60, true);
            return s.Length > 16 ? s[..16] : s;
        }
    }
}

/// <summary>The results screen hears this once a finished run has been placed
/// on the leaderboard. <see cref="Rank"/> is 1-based; 0 means it did not make
/// the top <see cref="Leaderboard.Size"/>.</summary>
public readonly struct LeaderboardPlaced
{
    public readonly int Rank;
    public readonly long When;
    public LeaderboardPlaced(int rank, long when) { Rank = rank; When = when; }
}

/// <summary>
/// The top ten runs on this machine, in <c>user://leaderboard.cfg</c>.
///
/// Its own file rather than a section of the profile: the profile is the one
/// file a player cannot recreate and is written with great care; a scoreboard
/// is disposable, and a bad write here must never be able to touch unlocks.
///
/// Ordering is "how far did the run get": wins first; then the furthest phase;
/// then, among wins, the faster clock, and among losses, the longer survival;
/// kills break any tie that is left.
///
/// Automated runs never read or write it (<see cref="Launch.Automated"/>), for
/// the same reason they never write the profile.
/// </summary>
public static class Leaderboard
{
    public const string FilePath = "user://leaderboard.cfg";
    public const int Size = 10;

    /// <summary>Optional: the run's difficulty label. Set by a difficulty
    /// system if the game has one; left null, entries show no difficulty.</summary>
    public static Func<string?>? DifficultyProvider { get; set; }

    private static readonly List<LeaderboardEntry> Entries = new();
    private static bool _loaded;

    /// <summary>The entry recorded by the most recent run in this process, so
    /// the menu page can highlight it.</summary>
    public static long LastRecorded { get; private set; }

    public static bool Enabled
    {
        get { Launch.Parse(); return !Launch.Automated; }
    }

    public static IReadOnlyList<LeaderboardEntry> Top
    {
        get { EnsureLoaded(); return Entries; }
    }

    /// <summary>Called by the unlock tracker when a run ends (death or win;
    /// a quit is not a finished run). Publishes <see cref="LeaderboardPlaced"/>.</summary>
    public static void RecordRun(Main main, bool won, int phase)
    {
        if (!Enabled || main is null || !GodotObject.IsInstanceValid(main) || main.Run is null) return;
        EnsureLoaded();

        var entry = new LeaderboardEntry
        {
            When = (long)Time.GetUnixTimeFromSystem(),
            Won = won,
            Seconds = main.Run.Seconds,
            Kills = main.Run.Kills,
            Level = main.Inventory.Level,
            Phase = Math.Max(phase, main.Run.Phase),
            Character = RunMeta.Character.Id,
            Difficulty = SafeDifficulty(),
        };
        // Same second as an existing entry (a restart spam): nudge so ids stay unique.
        while (Entries.Exists(x => x.When == entry.When)) entry.When++;

        Entries.Add(entry);
        Entries.Sort(Compare);
        int rank = Entries.IndexOf(entry) + 1;
        if (Entries.Count > Size) Entries.RemoveRange(Size, Entries.Count - Size);
        if (rank > Size) rank = 0;
        else Save();

        LastRecorded = rank > 0 ? entry.When : 0;
        GD.Print($"META leaderboard rank={rank} won={won} t={entry.Clock} kills={entry.Kills} level={entry.Level} phase={entry.Phase} char={entry.Character}");
        EventBus.Publish(new LeaderboardPlaced(rank, entry.When));
    }

    private static string SafeDifficulty()
    {
        try { return DifficultyProvider?.Invoke() ?? ""; }
        catch (Exception e) { GD.PushWarning($"[leaderboard] difficulty provider failed: {e.Message}"); return ""; }
    }

    public static int Compare(LeaderboardEntry a, LeaderboardEntry b)
    {
        if (a.Won != b.Won) return a.Won ? -1 : 1;
        if (a.Phase != b.Phase) return b.Phase.CompareTo(a.Phase);
        int time = a.Won ? a.Seconds.CompareTo(b.Seconds) : b.Seconds.CompareTo(a.Seconds);
        if (time != 0) return time;
        if (a.Kills != b.Kills) return b.Kills.CompareTo(a.Kills);
        return a.When.CompareTo(b.When);
    }

    // ------------------------------------------------------------ storage

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!Enabled || !FileAccess.FileExists(FilePath)) return;

        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok)
        {
            GD.PushWarning($"[leaderboard] could not read {FilePath}; starting empty");
            return;
        }
        foreach (string section in cfg.GetSections())
        {
            if (!section.StartsWith("run")) continue;
            Entries.Add(new LeaderboardEntry
            {
                When = Num(cfg, section, "when"),
                Won = Num(cfg, section, "won") != 0,
                Seconds = Num(cfg, section, "seconds"),
                Kills = (int)Num(cfg, section, "kills"),
                Level = (int)Num(cfg, section, "level"),
                Phase = (int)Num(cfg, section, "phase"),
                Character = Str(cfg, section, "character"),
                Difficulty = Str(cfg, section, "difficulty"),
            });
        }
        Entries.Sort(Compare);
        if (Entries.Count > Size) Entries.RemoveRange(Size, Entries.Count - Size);
    }

    private static long Num(ConfigFile cfg, string section, string key)
    {
        Variant v = cfg.GetValue(section, key, 0);
        return v.VariantType is Variant.Type.Int or Variant.Type.Float ? Math.Max(0L, (long)v.AsDouble()) : 0L;
    }

    private static string Str(ConfigFile cfg, string section, string key)
    {
        Variant v = cfg.GetValue(section, key, "");
        return v.VariantType == Variant.Type.String ? v.AsString() : "";
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        for (int i = 0; i < Entries.Count; i++)
        {
            LeaderboardEntry e = Entries[i];
            string s = $"run{i}";
            cfg.SetValue(s, "when", e.When);
            cfg.SetValue(s, "won", e.Won ? 1 : 0);
            cfg.SetValue(s, "seconds", Math.Round(e.Seconds, 1));
            cfg.SetValue(s, "kills", e.Kills);
            cfg.SetValue(s, "level", e.Level);
            cfg.SetValue(s, "phase", e.Phase);
            cfg.SetValue(s, "character", e.Character);
            cfg.SetValue(s, "difficulty", e.Difficulty);
        }
        Error err = cfg.Save(FilePath);
        if (err != Error.Ok) GD.PushWarning($"[leaderboard] could not save {FilePath}: {err}");
    }
}

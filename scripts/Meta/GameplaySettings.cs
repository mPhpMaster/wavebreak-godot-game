using System;
using Godot;

namespace Wavebreak.Meta;

/// <summary>How an auto-picked level-up chooses its card.</summary>
public enum AutoPickRule
{
    BestRarity,
    PreferWeapons,
    PreferItems,
    PreferTomes,
    UpgradeOwned,
    Random,
}

/// <summary>
/// Settings > Gameplay: the level-up hand's layout and the auto-pick.
///
///   Compact level-up   single-player uses the small docked hand that does
///                      not pause (co-op always does)
///   Auto-pick          level-ups resolve at once by a rule, with a toast
///
/// Stored in <c>user://settings.cfg [gameplay]</c>, loaded-then-written like
/// <see cref="GameSettings"/> so the other sections survive. Automated runs
/// never read or write it; their switches are <c>--compact-levelup</c> and
/// <c>--autopick-rule=&lt;id&gt;</c> (which also turns auto-pick on), in memory only.
/// </summary>
public static class GameplaySettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "gameplay";

    /// <summary>Stable ids, in <see cref="AutoPickRule"/> order: the file and the
    /// command line use these, never the enum's numbers.</summary>
    public static readonly string[] RuleIds = { "best_rarity", "prefer_weapons", "prefer_items", "prefer_tomes", "upgrade_owned", "random" };

    /// <summary>What Settings shows for each rule (English keys, see ArabicText.UiCoopUx).</summary>
    public static readonly string[] RuleNames = { "Best rarity first", "Prefer weapons", "Prefer items", "Prefer tomes", "Upgrade what I have", "Random" };

    private static bool _loaded;

    public static bool CompactLevelUp { get; private set; }
    public static bool AutoPick { get; private set; }
    public static AutoPickRule Rule { get; private set; } = AutoPickRule.BestRarity;

    /// <summary>Raised after any change, so an open panel can follow it.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        if (Launch.Automated)
        {
            // Test switches only; the player's file is never touched.
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--compact-levelup") CompactLevelUp = true;
                else if (arg.StartsWith("--autopick-rule="))
                {
                    AutoPick = true;
                    Rule = ParseRule(arg["--autopick-rule=".Length..]);
                }
            }
            return;
        }

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        CompactLevelUp = cfg.GetValue(Section, "compact_levelup", false).AsBool();
        AutoPick = cfg.GetValue(Section, "auto_pick", false).AsBool();
        Rule = ParseRule(cfg.GetValue(Section, "auto_pick_rule", RuleIds[0]).AsString());
    }

    public static AutoPickRule ParseRule(string id)
    {
        int i = Array.IndexOf(RuleIds, id.Trim().ToLowerInvariant());
        return i < 0 ? AutoPickRule.BestRarity : (AutoPickRule)i;
    }

    public static string IdOf(AutoPickRule rule) => RuleIds[Math.Clamp((int)rule, 0, RuleIds.Length - 1)];

    public static void SetCompact(bool on) { Load(); CompactLevelUp = on; Save(); }

    /// <summary>--check-input-guards: switch the layout in memory only.</summary>
    internal static void ForceCompactForTest(bool on) { Load(); CompactLevelUp = on; }
    public static void SetAutoPick(bool on) { Load(); AutoPick = on; Save(); }
    public static void SetRule(AutoPickRule rule) { Load(); Rule = rule; Save(); }

    private static void Save()
    {
        Changed?.Invoke();
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path);   // keep the other sections
        cfg.SetValue(Section, "compact_levelup", CompactLevelUp);
        cfg.SetValue(Section, "auto_pick", AutoPick);
        cfg.SetValue(Section, "auto_pick_rule", IdOf(Rule));
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[settings] could not save {Path}: {err}");
    }
}

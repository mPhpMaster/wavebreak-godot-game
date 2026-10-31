using System;
using Godot;
using Wavebreak.Data;

namespace Wavebreak.Meta;

/// <summary>Which palette the game's meaning-carrying colours use.</summary>
public enum ColorVision
{
    Normal,
    Deuteranopia,
    Protanopia,
    Tritanopia,
}

/// <summary>
/// Settings > Accessibility: a colour-blind palette and reduced flashes.
///
/// The palettes cover the colours that carry meaning on their own: rarity, enemy
/// telegraphs and shots, and team slots (which pings and map dots take). They
/// were chosen against the Machado 2009 simulation at full severity, for the
/// widest gap between the two closest colours in each set. Decals also gain
/// stripes in any of these modes, so a telegraph reads by pattern as well.
///
/// <see cref="Flash"/> is the one dial for anything that flashes: hit flashes,
/// the health bar, explosions' light, boss phase changes.
///
/// Stored in <c>user://settings.cfg [access]</c>, loaded-then-written like
/// <see cref="GameSettings"/>. Automated runs never touch the file; their
/// switches are <c>--colorblind=&lt;id&gt;</c> and <c>--reduce-flashes</c>.
/// </summary>
public static class Accessibility
{
    private const string Path = "user://settings.cfg";
    private const string Section = "access";

    /// <summary>What reduced flashes leaves of a flash.</summary>
    private const float FlashShare = 0.2f;

    /// <summary>Stable ids, in <see cref="ColorVision"/> order: the file and the
    /// command line use these.</summary>
    public static readonly string[] VisionIds = { "off", "deuteranopia", "protanopia", "tritanopia" };

    /// <summary>What Settings shows for each mode.</summary>
    public static readonly string[] VisionNames = { "Off", "Deuteranopia (red-green)", "Protanopia (red-green)", "Tritanopia (blue-yellow)" };

    private static bool _loaded;
    private static ColorVision _vision;
    private static bool _reduceFlashes;

    public static ColorVision Vision { get { Load(); return _vision; } }

    public static bool ReduceFlashes { get { Load(); return _reduceFlashes; } }

    /// <summary>Raised after any change, so an open panel can follow it.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Launch.Parse();

        if (Launch.Automated)
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--reduce-flashes") _reduceFlashes = true;
                else if (arg.StartsWith("--colorblind=")) _vision = ParseVision(arg["--colorblind=".Length..]);
            }
            return;
        }

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        _vision = ParseVision(cfg.GetValue(Section, "color_vision", VisionIds[0]).AsString());
        _reduceFlashes = cfg.GetValue(Section, "reduce_flashes", false).AsBool();
    }

    public static ColorVision ParseVision(string id)
    {
        int i = Array.IndexOf(VisionIds, id.Trim().ToLowerInvariant());
        return i < 0 ? ColorVision.Normal : (ColorVision)i;
    }

    public static void SetVision(ColorVision vision) { Load(); _vision = vision; Save(); }

    public static void SetReduceFlashes(bool on) { Load(); _reduceFlashes = on; Save(); }

    private static void Save()
    {
        Changed?.Invoke();
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path);   // keep the other sections
        cfg.SetValue(Section, "color_vision", VisionIds[(int)_vision]);
        cfg.SetValue(Section, "reduce_flashes", _reduceFlashes);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[settings] could not save {Path}: {err}");
    }

    // ---------------------------------------------------------------- flashes

    /// <summary>How much of a flash to show, 0..1 in and out.</summary>
    public static float Flash(float amount) => ReduceFlashes ? amount * FlashShare : amount;

    // ---------------------------------------------------------------- palettes

    private static bool RedGreen => Vision is ColorVision.Deuteranopia or ColorVision.Protanopia;

    /// <summary>True while telegraphs carry a pattern as well as a colour.</summary>
    public static bool Patterned => Vision != ColorVision.Normal;

    /// <summary>The rarity's colour in this palette, or null for the normal one.</summary>
    public static Color? Rarity(Rarity rarity)
    {
        if (Vision == ColorVision.Normal) return null;
        return (RedGreen, rarity) switch
        {
            (true, Data.Rarity.Common) => new Color(0.82f, 0.84f, 0.88f),
            (true, Data.Rarity.Uncommon) => new Color(0.98f, 0.95f, 0.35f),
            (true, Data.Rarity.Rare) => new Color(0.35f, 0.78f, 1.00f),
            (true, Data.Rarity.Epic) => new Color(0.55f, 0.32f, 0.90f),
            (true, Data.Rarity.Legendary) => new Color(0.92f, 0.48f, 0.02f),
            (false, Data.Rarity.Common) => new Color(0.80f, 0.82f, 0.86f),
            (false, Data.Rarity.Uncommon) => new Color(0.10f, 0.75f, 0.65f),
            (false, Data.Rarity.Rare) => new Color(0.20f, 0.45f, 0.95f),
            (false, Data.Rarity.Epic) => new Color(0.95f, 0.30f, 0.60f),
            (false, Data.Rarity.Legendary) => new Color(0.95f, 0.25f, 0.10f),
            _ => null,
        };
    }

    /// <summary>The team slot's colour in this palette, or null for the normal one.</summary>
    public static Color? Slot(int slot)
    {
        if (Vision == ColorVision.Normal) return null;
        return (RedGreen, slot) switch
        {
            (true, 0) => new Color(0.25f, 0.65f, 1.00f),
            (true, 1) => new Color(0.98f, 0.90f, 0.30f),
            (true, 2) => new Color(0.95f, 0.95f, 0.97f),
            (true, _) => new Color(0.70f, 0.25f, 0.65f),
            (false, 0) => new Color(0.20f, 0.80f, 0.85f),
            (false, 1) => new Color(0.95f, 0.25f, 0.15f),
            (false, 2) => new Color(0.95f, 0.95f, 0.97f),
            (false, _) => new Color(0.85f, 0.30f, 0.75f),
        };
    }

    /// <summary>Where a blast or a charge lands. White on red-green, the
    /// brightest thing either vision can tell from the world.</summary>
    public static Color Danger(Color normal) => Vision == ColorVision.Normal ? normal
        : (RedGreen ? new Color(0.95f, 0.95f, 0.97f) : new Color(1.00f, 0.20f, 0.30f)) with { A = normal.A };

    /// <summary>A rolling shockwave: kept apart from <see cref="Danger"/>,
    /// because the answer differs (jump a wave, leave a disc).</summary>
    public static Color Wave(Color normal) => Vision == ColorVision.Normal ? normal
        : (RedGreen ? new Color(0.20f, 0.55f, 1.00f) : new Color(0.00f, 0.85f, 0.85f)) with { A = normal.A };

    /// <summary>A hostile shot.</summary>
    public static Color Shot(Color normal) => Vision == ColorVision.Normal ? normal
        : RedGreen ? new Color(1.00f, 0.85f, 0.10f) : new Color(0.98f, 0.98f, 1.00f);
}

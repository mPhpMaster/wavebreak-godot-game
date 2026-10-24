using System;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// HUD preferences: whether the minimap shows and how big it is. Stored in
/// <c>user://settings.cfg</c> under <c>[hud]</c>, load-then-write like the other
/// settings so no section erases another. Automated launches never read or
/// write it (minimap on, medium), so captures look the same everywhere.
/// The rows live on the graphics page under CAMERA &amp; HUD.
/// </summary>
public static class HudSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "hud";

    public static readonly string[] SizeNames = { "Small", "Medium", "Large" };

    /// <summary>Minimap diameter in pixels (at 1280x720) per size.</summary>
    public static readonly float[] SizePixels = { 128f, 160f, 200f };

    private static bool _loaded;

    public static bool Minimap { get; private set; } = true;
    public static int SizeIndex { get; private set; } = 1;
    public static float MinimapPixels => SizePixels[Math.Clamp(SizeIndex, 0, SizePixels.Length - 1)];

    /// <summary>Bumped on every change, so the minimap re-lays itself out.</summary>
    public static int Version { get; private set; }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        Variant m = cfg.GetValue(Section, "minimap", true);
        if (m.VariantType == Variant.Type.Bool) Minimap = m.AsBool();
        Variant s = cfg.GetValue(Section, "minimap_size", 1);
        if (s.VariantType is Variant.Type.Int or Variant.Type.Float) SizeIndex = Math.Clamp(s.AsInt32(), 0, SizeNames.Length - 1);
    }

    public static void SetMinimap(bool on)
    {
        Load();
        Minimap = on;
        Version++;
        Save();
    }

    public static void SetSize(int index)
    {
        Load();
        SizeIndex = Math.Clamp(index, 0, SizeNames.Length - 1);
        Version++;
        Save();
    }

    private static void Save()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue(Section, "minimap", Minimap);
        cfg.SetValue(Section, "minimap_size", SizeIndex);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[settings] could not save {Path}: {err}");
    }
}

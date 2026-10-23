using System;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// The non-audio settings the menu exposes: mouse sensitivity and fullscreen.
///
/// Stored in the same <c>user://settings.cfg</c> as
/// <see cref="Audio.AudioSettings"/>, in their own sections. AudioSettings
/// already loads-then-writes so it keeps sections it does not own; this does the
/// same, so the two never erase each other.
///
/// Sensitivity is a multiplier on the camera's own default rather than a raw
/// radians-per-pixel value: the camera keeps the authority over what "normal"
/// feels like, and "1.0x" is something a player can reason about.
/// </summary>
public static class GameSettings
{
    private const string Path = "user://settings.cfg";

    public const float MinSensitivity = 0.25f;
    public const float MaxSensitivity = 3f;
    public const float SensitivityStep = 0.05f;

    private static bool _loaded;

    public static float Sensitivity { get; private set; } = 1f;
    public static bool Fullscreen { get; private set; }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;

        Variant s = cfg.GetValue("input", "mouse_sensitivity", 1f);
        if (s.VariantType is Variant.Type.Float or Variant.Type.Int)
            Sensitivity = Math.Clamp(s.AsSingle(), MinSensitivity, MaxSensitivity);
        Fullscreen = cfg.GetValue("video", "fullscreen", false).AsBool();
    }

    public static void SetSensitivity(float value)
    {
        Sensitivity = Math.Clamp(MathF.Round(value / SensitivityStep) * SensitivityStep, MinSensitivity, MaxSensitivity);
        Save();
    }

    public static void SetFullscreen(bool on)
    {
        Fullscreen = on;
        ApplyWindow();
        Save();
    }

    /// <summary>Push the window mode. Skipped for automated launches: a capture
    /// must render at the resolution it was asked for, and a smoke run that
    /// went fullscreen on a developer's desk would be rude.</summary>
    public static void ApplyWindow()
    {
        if (Launch.Automated || DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
    }

    private static void Save()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep the audio section
        cfg.SetValue("input", "mouse_sensitivity", Sensitivity);
        cfg.SetValue("video", "fullscreen", Fullscreen);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[settings] could not save {Path}: {err}");
    }
}

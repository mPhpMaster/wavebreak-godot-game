using System;
using Godot;

namespace Wavebreak.Meta;

/// <summary>
/// The gamepad's own settings: how fast the right stick turns the camera, the
/// two dead zones and inverted look. Bindings live in <see cref="Keybinds"/>.
///
/// Stored in <c>user://settings.cfg [pad]</c>, loaded-then-written like
/// <see cref="GameSettings"/> so the other sections survive. Automated runs
/// never read or write it; a check changes these in memory only.
/// </summary>
public static class PadSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "pad";

    public const float MinSensitivity = 0.25f;
    public const float MaxSensitivity = 3f;
    public const float SensitivityStep = 0.05f;
    public const float MinDeadzone = 0.05f;
    public const float MaxDeadzone = 0.6f;
    public const float DeadzoneStep = 0.01f;

    private static bool _loaded;

    public static float LookSensitivity { get; private set; } = 1f;
    public static float LookDeadzone { get; private set; } = 0.15f;
    public static float MoveDeadzone { get; private set; } = 0.2f;
    public static bool InvertLook { get; private set; }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (!Launch.Automated)
        {
            var cfg = new ConfigFile();
            if (cfg.Load(Path) == Error.Ok)
            {
                LookSensitivity = Read(cfg, "look_sensitivity", LookSensitivity, MinSensitivity, MaxSensitivity);
                LookDeadzone = Read(cfg, "look_deadzone", LookDeadzone, MinDeadzone, MaxDeadzone);
                MoveDeadzone = Read(cfg, "move_deadzone", MoveDeadzone, MinDeadzone, MaxDeadzone);
                InvertLook = cfg.GetValue(Section, "invert_look", false).AsBool();
            }
        }
        ApplyMoveDeadzone();
    }

    private static float Read(ConfigFile cfg, string key, float fallback, float min, float max)
    {
        Variant v = cfg.GetValue(Section, key, fallback);
        return v.VariantType is Variant.Type.Float or Variant.Type.Int ? Math.Clamp(v.AsSingle(), min, max) : fallback;
    }

    public static void SetLookSensitivity(float value)
    {
        Load();
        LookSensitivity = Math.Clamp(MathF.Round(value / SensitivityStep) * SensitivityStep, MinSensitivity, MaxSensitivity);
        Save();
    }

    public static void SetLookDeadzone(float value)
    {
        Load();
        LookDeadzone = Math.Clamp(value, MinDeadzone, MaxDeadzone);
        Save();
    }

    public static void SetMoveDeadzone(float value)
    {
        Load();
        MoveDeadzone = Math.Clamp(value, MinDeadzone, MaxDeadzone);
        ApplyMoveDeadzone();
        Save();
    }

    public static void SetInvertLook(bool on)
    {
        Load();
        InvertLook = on;
        Save();
    }

    /// <summary>The move actions take their dead zone from the InputMap, which
    /// is what Input.GetVector reads for the left stick.</summary>
    private static void ApplyMoveDeadzone()
    {
        foreach (string action in new[] { "move_left", "move_right", "move_forward", "move_back" })
            if (InputMap.HasAction(action)) InputMap.ActionSetDeadzone(action, MoveDeadzone);
    }

    private static void Save()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path);   // keep the other sections
        cfg.SetValue(Section, "look_sensitivity", LookSensitivity);
        cfg.SetValue(Section, "look_deadzone", LookDeadzone);
        cfg.SetValue(Section, "move_deadzone", MoveDeadzone);
        cfg.SetValue(Section, "invert_look", InvertLook);
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[settings] could not save {Path}: {err}");
    }
}

using System;
using Godot;

namespace Wavebreak.Meta;

/// <summary>Every graphics option, as plain values. Presets and the panel edit
/// one of these; <see cref="GraphicsSettings"/> pushes it into the engine.</summary>
public sealed class GraphicsConfig
{
    public int Preset = GraphicsSettings.High;

    // display (window mode, size and monitor only change through APPLY)
    public int WindowMode;          // 0 windowed, 1 borderless fullscreen, 2 exclusive fullscreen
    public int Width = 1280, Height = 720;
    public int Monitor = -1;        // -1 = whichever screen the window is on
    public int VSync = 1;           // 0 off, 1 on, 2 adaptive
    public int FpsCap;              // 0 = unlimited

    // quality (what the presets set)
    public float RenderScale = 1f;
    public int Upscale;             // 0 bilinear, 1 FSR 1.0, 2 FSR 2.2
    public int AntiAlias;           // 0 off, 1 FXAA, 2/3/4 MSAA 2x/4x/8x, 5 TAA
    public int Shadows = 3;         // 0 off .. 3 high
    public bool Ssao;
    public bool Glow = true;
    public bool Fog = true;
    public bool Ssr;
    public bool Ssil;
    public float PropDensity = 1f;  // multiplier on the map's own scatter density
    public int MaxPopups = 128;     // damage numbers on screen at once

    // camera and HUD (preferences, never touched by a preset)
    public float Fov = 70f;
    public float Shake = 1f;
    public float ScreenFx = 1f;
    public float WeaponFx = 0.75f;  // opacity of weapon/projectile effects
    public float UiScale = 1f;
    public float Brightness = 1f;
    public bool ShowFps;

    public GraphicsConfig Clone() => (GraphicsConfig)MemberwiseClone();
}

/// <summary>
/// Graphics options: presets, display, render scale, anti-aliasing, shadows,
/// environment effects, camera and HUD, visual density.
///
/// Stored in the shared <c>user://settings.cfg</c> under <c>[graphics]</c>,
/// load-then-write like <see cref="GameSettings"/> so no section erases another.
/// Automated launches never save and normally use the defaults. The explicit
/// --perf-user-graphics flag permits a read-only load for same-quality profiling.
/// Default automated launches run on the defaults,
/// which are the "High" preset and are exactly what the game looked like before
/// this page existed (engine defaults for AA, scale and shadows, the theme's own
/// glow and fog). So tests and captures stay stable.
///
/// Where it lands:
///   viewport  — MSAA/FXAA/TAA, 3D scaling mode and scale (the root window)
///   server    — directional shadow atlas size and soft-shadow quality
///   scene     — every WorldEnvironment (glow, SSAO, SSR, SSIL, fog, brightness)
///               and DirectionalLight3D (shadow on/off), via <see cref="ApplyScene"/>;
///               maps call it when they build their sky, so a new map starts right
///   camera    — <see cref="Actors.PlayerCamera"/> reads Fov, Shake and ScreenFx
///   popups    — <see cref="UI.DamagePopup"/> reads MaxPopups
///   arena     — PropDensity scales the next map's scatter
/// Each environment and light remembers its authored values as metadata, so
/// turning an effect back on restores what the map asked for, not a guess.
/// </summary>
public static class GraphicsSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "graphics";

    public const int Low = 0, Medium = 1, High = 2, Ultra = 3, Custom = 4;
    public static readonly string[] PresetNames = { "Low", "Medium", "High", "Ultra", "Custom" };
    public static readonly string[] WindowModes = { "Windowed", "Borderless fullscreen", "Exclusive fullscreen" };
    public static readonly string[] VSyncModes = { "Off", "On", "Adaptive" };
    public static readonly int[] FpsCaps = { 30, 60, 120, 144, 165, 240, 0 };
    public static readonly string[] UpscaleModes = { "Bilinear", "FSR 1.0", "FSR 2.2" };
    public static readonly string[] AntiAliasModes = { "Off", "FXAA", "MSAA 2x", "MSAA 4x", "MSAA 8x", "TAA" };
    public static readonly string[] ShadowModes = { "Off", "Low", "Medium", "High" };
    public static readonly int[] PopupCaps = { 32, 64, 128 };
    public static readonly Vector2I[] CommonSizes =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 2160),
    };

    /// <summary>Metadata keys on Environment / DirectionalLight3D.</summary>
    public const string GameplayFogMeta = "wb_gameplay_fog";
    private const string BaseMeta = "wb_base";

    /// <summary>With fog off, gameplay fog (Sea Fret) is kept at this share of
    /// its density: it is a rule of the run, not decoration.</summary>
    public const float GameplayFogKept = 0.6f;

    private static bool _loaded;
    private static bool _windowApplied;

    public static GraphicsConfig Current { get; private set; } = new();

    public static float ShakeScale => Current.Shake;
    public static float ScreenFxScale => Current.ScreenFx;

    /// <summary>An effect's own fade (0 solid .. 1 gone) pushed toward clear by
    /// the "Weapon effects" opacity, so a full build cannot wall off the screen.</summary>
    public static float FxFade(float own) => 1f - (1f - Mathf.Clamp(own, 0f, 1f)) * Current.WeaponFx;
    public static int MaxDamageNumbers => Current.MaxPopups;
    public static float PropDensityScale => Current.PropDensity;

    public static event Action? Changed;

    // ================================================================ presets

    public static void ApplyPresetValues(GraphicsConfig c, int preset)
    {
        switch (preset)
        {
            case Low:
                c.RenderScale = 0.75f; c.Upscale = 1; c.AntiAlias = 0; c.Shadows = 0;
                c.Ssao = false; c.Glow = false; c.Fog = true; c.Ssr = false; c.Ssil = false;
                c.PropDensity = 0.5f; c.MaxPopups = 32;
                break;
            case Medium:
                c.RenderScale = 1f; c.Upscale = 0; c.AntiAlias = 1; c.Shadows = 2;
                c.Ssao = false; c.Glow = true; c.Fog = true; c.Ssr = false; c.Ssil = false;
                c.PropDensity = 0.75f; c.MaxPopups = 64;
                break;
            case High:
                c.RenderScale = 1f; c.Upscale = 0; c.AntiAlias = 0; c.Shadows = 3;
                c.Ssao = false; c.Glow = true; c.Fog = true; c.Ssr = false; c.Ssil = false;
                c.PropDensity = 1f; c.MaxPopups = 128;
                break;
            case Ultra:
                c.RenderScale = 1f; c.Upscale = 0; c.AntiAlias = 3; c.Shadows = 3;
                c.Ssao = true; c.Glow = true; c.Fog = true; c.Ssr = false; c.Ssil = false;
                c.PropDensity = 1.25f; c.MaxPopups = 128;
                break;
            default:
                return;
        }
        c.Preset = preset;
    }

    /// <summary>FSR 2.2 needs the Forward+ renderer.</summary>
    public static bool Fsr2Available => RenderingServer.GetCurrentRenderingMethod() == "forward_plus";

    // ================================================================ load/save

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Launch.Parse();
        if (Launch.Automated && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--perf-user-graphics") < 0) return; // benchmark may read, never save

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        GraphicsConfig c = Current;
        if (!cfg.HasSection(Section))
        {
            // The old single "Fullscreen" switch.
            if (cfg.GetValue("video", "fullscreen", false).AsBool()) c.WindowMode = 1;
            return;
        }

        int I(string key, int def, int min, int max)
        {
            Variant v = cfg.GetValue(Section, key, def);
            return v.VariantType is Variant.Type.Int or Variant.Type.Float ? Math.Clamp(v.AsInt32(), min, max) : def;
        }
        float F(string key, float def, float min, float max)
        {
            Variant v = cfg.GetValue(Section, key, def);
            return v.VariantType is Variant.Type.Int or Variant.Type.Float ? Math.Clamp(v.AsSingle(), min, max) : def;
        }
        bool B(string key, bool def)
        {
            Variant v = cfg.GetValue(Section, key, def);
            return v.VariantType == Variant.Type.Bool ? v.AsBool() : def;
        }

        c.Preset = I("preset", c.Preset, 0, Custom);
        c.WindowMode = I("window_mode", c.WindowMode, 0, 2);
        c.Width = I("width", c.Width, 640, 7680);
        c.Height = I("height", c.Height, 360, 4320);
        c.Monitor = I("monitor", c.Monitor, -1, 16);
        c.VSync = I("vsync", c.VSync, 0, 2);
        int cap = I("fps_cap", c.FpsCap, 0, 1000);
        c.FpsCap = Array.IndexOf(FpsCaps, cap) >= 0 ? cap : 0;
        c.RenderScale = F("render_scale", c.RenderScale, 0.5f, 1f);
        c.Upscale = I("upscale", c.Upscale, 0, 2);
        c.AntiAlias = I("anti_alias", c.AntiAlias, 0, 5);
        c.Shadows = I("shadows", c.Shadows, 0, 3);
        c.Ssao = B("ssao", c.Ssao);
        c.Glow = B("glow", c.Glow);
        c.Fog = B("fog", c.Fog);
        c.Ssr = B("ssr", c.Ssr);
        c.Ssil = B("ssil", c.Ssil);
        c.PropDensity = F("prop_density", c.PropDensity, 0.25f, 1.5f);
        int pop = I("max_popups", c.MaxPopups, 1, 1000);
        c.MaxPopups = Array.IndexOf(PopupCaps, pop) >= 0 ? pop : 128;
        c.Fov = F("fov", c.Fov, 50f, 100f);
        c.Shake = F("shake", c.Shake, 0f, 1f);
        c.ScreenFx = F("screen_fx", c.ScreenFx, 0f, 1f);
        c.WeaponFx = F("weapon_fx", c.WeaponFx, 0.1f, 1f);
        c.UiScale = F("ui_scale", c.UiScale, 0.8f, 1.5f);
        c.Brightness = F("brightness", c.Brightness, 0.5f, 1.5f);
        c.ShowFps = B("show_fps", c.ShowFps);
    }

    public static void Save()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep audio, input and key sections
        GraphicsConfig c = Current;
        cfg.SetValue(Section, "preset", c.Preset);
        cfg.SetValue(Section, "window_mode", c.WindowMode);
        cfg.SetValue(Section, "width", c.Width);
        cfg.SetValue(Section, "height", c.Height);
        cfg.SetValue(Section, "monitor", c.Monitor);
        cfg.SetValue(Section, "vsync", c.VSync);
        cfg.SetValue(Section, "fps_cap", c.FpsCap);
        cfg.SetValue(Section, "render_scale", c.RenderScale);
        cfg.SetValue(Section, "upscale", c.Upscale);
        cfg.SetValue(Section, "anti_alias", c.AntiAlias);
        cfg.SetValue(Section, "shadows", c.Shadows);
        cfg.SetValue(Section, "ssao", c.Ssao);
        cfg.SetValue(Section, "glow", c.Glow);
        cfg.SetValue(Section, "fog", c.Fog);
        cfg.SetValue(Section, "ssr", c.Ssr);
        cfg.SetValue(Section, "ssil", c.Ssil);
        cfg.SetValue(Section, "prop_density", c.PropDensity);
        cfg.SetValue(Section, "max_popups", c.MaxPopups);
        cfg.SetValue(Section, "fov", c.Fov);
        cfg.SetValue(Section, "shake", c.Shake);
        cfg.SetValue(Section, "screen_fx", c.ScreenFx);
        cfg.SetValue(Section, "weapon_fx", c.WeaponFx);
        cfg.SetValue(Section, "ui_scale", c.UiScale);
        cfg.SetValue(Section, "brightness", c.Brightness);
        cfg.SetValue(Section, "show_fps", c.ShowFps);
        if (cfg.HasSectionKey("video", "fullscreen")) cfg.EraseSectionKey("video", "fullscreen");
        Error err = cfg.Save(Path);
        if (err != Error.Ok) GD.PushWarning($"[graphics] could not save {Path}: {err}");
    }

    // ================================================================ edits

    /// <summary>Edit one option, apply it live and save. A quality option makes
    /// the preset "Custom".</summary>
    public static void Change(Action<GraphicsConfig> edit, bool quality)
    {
        edit(Current);
        if (quality) Current.Preset = Custom;
        ApplyLive();
        Save();
    }

    public static void SetPreset(int preset)
    {
        if (preset == Custom) Current.Preset = Custom;
        else ApplyPresetValues(Current, preset);
        ApplyLive();
        Save();
    }

    /// <summary>Everything back to defaults except the window (mode, size,
    /// monitor), which only ever changes through APPLY and its confirmation.</summary>
    public static void ResetDefaults()
    {
        GraphicsConfig old = Current;
        Current = new GraphicsConfig
        {
            WindowMode = old.WindowMode, Width = old.Width, Height = old.Height, Monitor = old.Monitor,
        };
        ApplyLive();
        Save();
    }

    /// <summary>Set and apply window mode, size and monitor (the APPLY button
    /// and its revert).</summary>
    public static void CommitDisplay(int mode, int width, int height, int monitor)
    {
        GraphicsConfig c = Current;
        c.WindowMode = Math.Clamp(mode, 0, 2);
        c.Width = width; c.Height = height; c.Monitor = monitor;
        ApplyWindow();
        Save();
        Changed?.Invoke();
    }

    // ================================================================ apply

    private static bool DisplayAllowed => !Launch.Automated && DisplayServer.GetName() != "headless";

    /// <summary>Window mode, size and monitor once per process, at boot.</summary>
    public static void ApplyWindowOnce()
    {
        if (_windowApplied) return;
        _windowApplied = true;
        ApplyWindow();
    }

    /// <summary>Skipped for automated launches: a capture must render at the
    /// size it was asked for, and a test going fullscreen on a desk is rude.</summary>
    public static void ApplyWindow()
    {
        if (!DisplayAllowed) return;
        GraphicsConfig c = Current;
        int screens = DisplayServer.GetScreenCount();
        if (c.Monitor >= 0 && c.Monitor < screens && DisplayServer.WindowGetCurrentScreen() != c.Monitor)
        {
            if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetCurrentScreen(c.Monitor);
        }
        switch (c.WindowMode)
        {
            case 1: DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); break;
            case 2: DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen); break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                int screen = DisplayServer.WindowGetCurrentScreen();
                Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
                var size = new Vector2I(Math.Min(c.Width, usable.Size.X), Math.Min(c.Height, usable.Size.Y));
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
                break;
        }
    }

    /// <summary>Everything that applies live, against the running scene.</summary>
    public static void ApplyLive()
    {
        if (Engine.GetMainLoop() is SceneTree tree) ApplyAll(tree.CurrentScene ?? tree.Root);
        Changed?.Invoke();
    }

    /// <summary>All live settings: viewport, shadow quality, frame rate, UI
    /// scale, every environment and sun under <paramref name="scene"/>, the
    /// player camera and the FPS counter.</summary>
    public static void ApplyAll(Node scene)
    {
        SceneTree tree = scene.GetTree() ?? (SceneTree)Engine.GetMainLoop();
        ApplyViewport(tree.Root);
        ApplyShadowQuality();
        ApplyFrameRate();
        tree.Root.ContentScaleFactor = Current.UiScale;
        ApplyScene(scene);
        ApplyCamera();
        UI.FpsOverlay.Sync(tree);
    }

    public static void ApplyViewport(Viewport vp)
    {
        GraphicsConfig c = Current;
        vp.Scaling3DMode = c.Upscale switch
        {
            1 => Viewport.Scaling3DModeEnum.Fsr,
            2 when Fsr2Available => Viewport.Scaling3DModeEnum.Fsr2,
            _ => Viewport.Scaling3DModeEnum.Bilinear,
        };
        vp.Scaling3DScale = c.RenderScale;
        vp.Msaa3D = c.AntiAlias switch
        {
            2 => Viewport.Msaa.Msaa2X,
            3 => Viewport.Msaa.Msaa4X,
            4 => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled,
        };
        vp.ScreenSpaceAA = c.AntiAlias == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        vp.UseTaa = c.AntiAlias == 5;
    }

    /// <summary>Directional shadow atlas and soft-shadow filter. "High" is the
    /// engine default (4096, soft low).</summary>
    public static void ApplyShadowQuality()
    {
        (int size, RenderingServer.ShadowQuality q) = Current.Shadows switch
        {
            1 => (1024, RenderingServer.ShadowQuality.Hard),
            2 => (2048, RenderingServer.ShadowQuality.SoftVeryLow),
            _ => (4096, RenderingServer.ShadowQuality.SoftLow),
        };
        RenderingServer.DirectionalShadowAtlasSetSize(size, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(q);
    }

    public static void ApplyFrameRate()
    {
        Engine.MaxFps = Current.FpsCap;
        if (!DisplayAllowed) return;
        DisplayServer.WindowSetVsyncMode(Current.VSync switch
        {
            0 => DisplayServer.VSyncMode.Disabled,
            2 => DisplayServer.VSyncMode.Adaptive,
            _ => DisplayServer.VSyncMode.Enabled,
        });
    }

    public static void ApplyCamera()
    {
        if (Actors.PlayerCamera.Instance is { } cam && GodotObject.IsInstanceValid(cam))
            cam.FieldOfView = Current.Fov;
    }

    /// <summary>Every WorldEnvironment and DirectionalLight3D at or under
    /// <paramref name="root"/>. Maps call this after building their sky.</summary>
    public static void ApplyScene(Node root)
    {
        if (root is WorldEnvironment { Environment: { } e0 }) ApplyEnvironment(e0);
        if (root is DirectionalLight3D l0) ApplyLight(l0);
        foreach (Node n in root.FindChildren("*", "WorldEnvironment", true, false))
            if (n is WorldEnvironment { Environment: { } env }) ApplyEnvironment(env);
        foreach (Node n in root.FindChildren("*", "DirectionalLight3D", true, false))
            if (n is DirectionalLight3D light) ApplyLight(light);
    }

    public static void ApplyEnvironment(Godot.Environment env)
    {
        if (!env.HasMeta(BaseMeta))
            env.SetMeta(BaseMeta, new Godot.Collections.Array
            {
                env.GlowEnabled, env.FogEnabled, env.FogDensity, env.AdjustmentEnabled, env.AdjustmentBrightness,
            });
        var b = env.GetMeta(BaseMeta).AsGodotArray();
        bool baseGlow = b[0].AsBool(), baseFog = b[1].AsBool(), baseAdjust = b[3].AsBool();
        float baseDensity = b[2].AsSingle(), baseBrightness = b[4].AsSingle();
        float gameplay = env.HasMeta(GameplayFogMeta) ? env.GetMeta(GameplayFogMeta).AsSingle() : 0f;

        GraphicsConfig c = Current;
        env.GlowEnabled = baseGlow && c.Glow;
        env.SsaoEnabled = c.Ssao;
        env.SsrEnabled = c.Ssr;
        env.SsilEnabled = c.Ssil;
        if (c.Fog)
        {
            env.FogEnabled = baseFog || gameplay > 0f;
            env.FogDensity = Mathf.Max(baseFog ? baseDensity : 0f, gameplay);
        }
        else
        {
            // Decorative fog goes; gameplay fog stays, thinner.
            env.FogEnabled = gameplay > 0f;
            env.FogDensity = gameplay > 0f ? gameplay * GameplayFogKept : baseDensity;
        }
        env.AdjustmentEnabled = baseAdjust || !Mathf.IsEqualApprox(c.Brightness, 1f);
        env.AdjustmentBrightness = baseBrightness * c.Brightness;
    }

    public static void ApplyLight(DirectionalLight3D light)
    {
        if (!light.HasMeta(BaseMeta)) light.SetMeta(BaseMeta, light.ShadowEnabled);
        light.ShadowEnabled = light.GetMeta(BaseMeta).AsBool() && Current.Shadows > 0;
    }
}

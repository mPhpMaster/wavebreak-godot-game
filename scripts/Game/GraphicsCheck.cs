using System;
using System.Threading.Tasks;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Meta;
using Wavebreak.UI;

namespace Wavebreak.Game;

/// <summary>
/// --check-graphics: in a live run, applies every preset and toggles every
/// option once through the same calls the graphics page makes, and checks the
/// engine state moved (viewport AA and scaling, environment SSAO/glow/fog/
/// brightness, sun shadows, Engine.MaxFps, the camera's field of view, UI
/// scale, the FPS counter, Sea Fret fog surviving "fog off"). Then it builds
/// the page itself, drives a preset through its dropdown, runs the display
/// APPLY/revert cycle, resets, and checks the defaults are back.
/// Prints GRAPHICS PASS/FAIL. Automated, so nothing is saved.
/// </summary>
public static class GraphicsCheck
{
    public static async Task Run(Main main)
    {
        SceneTree tree = main.GetTree();
        bool ok = true;
        void Expect(string what, bool cond) { GD.Print($"GRAPHICS {(cond ? "ok  " : "FAIL")} {what}"); ok &= cond; }
        async Task Wait(double s)
            => await tree.ToSignal(tree.CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

        await Wait(1.5);
        Viewport vp = tree.Root;
        WorldEnvironment? we = null;
        DirectionalLight3D? sun = null;
        foreach (Node n in main.FindChildren("*", "WorldEnvironment", true, false)) { we = n as WorldEnvironment; break; }
        foreach (Node n in main.FindChildren("*", "DirectionalLight3D", true, false)) { sun = n as DirectionalLight3D; break; }
        Expect("run has an environment and a sun", we?.Environment is not null && sun is not null);
        if (we?.Environment is null || sun is null) { Finish(tree, false); return; }
        Godot.Environment env = we.Environment;
        PlayerCamera? cam = PlayerCamera.Instance;
        Expect("player camera exists", cam is not null);
        bool baseFog = env.FogEnabled;
        float baseDensity = env.FogDensity;

        void Defaults(string when)
        {
            Expect($"{when}: preset High", GraphicsSettings.Current.Preset == GraphicsSettings.High);
            Expect($"{when}: viewport native, no AA",
                   Mathf.IsEqualApprox(vp.Scaling3DScale, 1f) && vp.Scaling3DMode == Viewport.Scaling3DModeEnum.Bilinear
                   && vp.Msaa3D == Viewport.Msaa.Disabled && vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Disabled && !vp.UseTaa);
            Expect($"{when}: glow on, SSAO/SSR/SSIL off", env.GlowEnabled && !env.SsaoEnabled && !env.SsrEnabled && !env.SsilEnabled);
            Expect($"{when}: theme fog unchanged", env.FogEnabled == baseFog && Mathf.IsEqualApprox(env.FogDensity, baseDensity));
            Expect($"{when}: sun casts shadows", sun.ShadowEnabled);
            Expect($"{when}: no fps cap, FOV 70, UI 100%", Engine.MaxFps == 0 && cam is { FieldOfView: 70f }
                   && Mathf.IsEqualApprox(tree.Root.ContentScaleFactor, 1f));
            Expect($"{when}: brightness 1", Mathf.IsEqualApprox(env.AdjustmentBrightness, 1f));
        }
        Defaults("defaults");

        // ------------------------------------------------------------ presets
        GraphicsSettings.SetPreset(GraphicsSettings.Low);
        Expect("Low: 75% FSR, no shadows, no glow",
               Mathf.IsEqualApprox(vp.Scaling3DScale, 0.75f) && vp.Scaling3DMode == Viewport.Scaling3DModeEnum.Fsr
               && !sun.ShadowEnabled && !env.GlowEnabled && GraphicsSettings.MaxDamageNumbers == 32);
        GraphicsSettings.SetPreset(GraphicsSettings.Medium);
        Expect("Medium: FXAA, shadows on, glow on",
               vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa && sun.ShadowEnabled && env.GlowEnabled
               && Mathf.IsEqualApprox(vp.Scaling3DScale, 1f));
        GraphicsSettings.SetPreset(GraphicsSettings.Ultra);
        Expect("Ultra: MSAA 4x, SSAO", vp.Msaa3D == Viewport.Msaa.Msaa4X && env.SsaoEnabled && vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Disabled);
        GraphicsSettings.SetPreset(GraphicsSettings.High);
        Defaults("High again");
        await Wait(0.3);

        // ------------------------------------------------------------ every option
        void Set(Action<GraphicsConfig> e, bool q = true) => GraphicsSettings.Change(e, q);

        Set(c => c.RenderScale = 0.5f);
        Expect("render scale 50%", Mathf.IsEqualApprox(vp.Scaling3DScale, 0.5f) && GraphicsSettings.Current.Preset == GraphicsSettings.Custom);
        Set(c => c.Upscale = 1);
        Expect("upscaling FSR 1.0", vp.Scaling3DMode == Viewport.Scaling3DModeEnum.Fsr);
        Set(c => c.Upscale = 2);
        Expect($"upscaling FSR 2.2 (available={GraphicsSettings.Fsr2Available})",
               vp.Scaling3DMode == (GraphicsSettings.Fsr2Available ? Viewport.Scaling3DModeEnum.Fsr2 : Viewport.Scaling3DModeEnum.Bilinear));
        await Wait(0.2);
        Set(c => { c.Upscale = 0; c.RenderScale = 1f; });

        (int aa, Func<bool> test)[] aas =
        {
            (1, () => vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa && vp.Msaa3D == Viewport.Msaa.Disabled),
            (2, () => vp.Msaa3D == Viewport.Msaa.Msaa2X),
            (3, () => vp.Msaa3D == Viewport.Msaa.Msaa4X),
            (4, () => vp.Msaa3D == Viewport.Msaa.Msaa8X),
            (5, () => vp.UseTaa && vp.Msaa3D == Viewport.Msaa.Disabled),
            (0, () => !vp.UseTaa && vp.Msaa3D == Viewport.Msaa.Disabled && vp.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Disabled),
        };
        foreach (var (aa, test) in aas)
        {
            Set(c => c.AntiAlias = aa);
            await Wait(0.1);
            Expect($"anti-aliasing {GraphicsSettings.AntiAliasModes[aa]}", test());
        }

        for (int sh = 0; sh <= 3; sh++)
        {
            int level = sh;
            Set(c => c.Shadows = level);
            Expect($"shadows {GraphicsSettings.ShadowModes[level]}", sun.ShadowEnabled == level > 0);
        }

        Set(c => c.Ssao = true); Expect("SSAO on", env.SsaoEnabled);
        Set(c => c.Ssao = false); Expect("SSAO off", !env.SsaoEnabled);
        Set(c => c.Glow = false); Expect("glow off", !env.GlowEnabled);
        Set(c => c.Glow = true); Expect("glow on", env.GlowEnabled);
        Set(c => c.Ssr = true); Expect("SSR on", env.SsrEnabled);
        Set(c => c.Ssil = true); Expect("SSIL on", env.SsilEnabled);
        Set(c => { c.Ssr = false; c.Ssil = false; });
        Set(c => c.Fog = false); Expect("fog off", !env.FogEnabled);

        // Sea Fret: gameplay fog survives "fog off", thinner.
        env.SetMeta(GraphicsSettings.GameplayFogMeta, 0.045f);
        GraphicsSettings.ApplyEnvironment(env);
        Expect($"Sea Fret fog kept with fog off (density {env.FogDensity:0.000})",
               env.FogEnabled && Mathf.IsEqualApprox(env.FogDensity, 0.045f * GraphicsSettings.GameplayFogKept));
        Set(c => c.Fog = true);
        Expect("Sea Fret fog full with fog on", env.FogEnabled && Mathf.IsEqualApprox(env.FogDensity, Mathf.Max(baseFog ? baseDensity : 0f, 0.045f)));
        env.RemoveMeta(GraphicsSettings.GameplayFogMeta);
        GraphicsSettings.ApplyEnvironment(env);
        Expect("fog back to the theme's", env.FogEnabled == baseFog && Mathf.IsEqualApprox(env.FogDensity, baseDensity));

        Set(c => c.Fov = 90f, false);
        await Wait(0.6);
        Expect($"FOV 90 reaches the camera (fov {cam?.Camera.Fov:0.0})", cam is not null && cam.FieldOfView == 90f && cam.Camera.Fov > 84f);
        Set(c => c.Shake = 0f, false); Expect("camera shake 0%", GraphicsSettings.ShakeScale == 0f);
        Set(c => c.ScreenFx = 0.5f, false); Expect("speed & dash zoom 50%", GraphicsSettings.ScreenFxScale == 0.5f);
        Set(c => c.Brightness = 1.3f, false);
        Expect("brightness 130%", env.AdjustmentEnabled && Mathf.IsEqualApprox(env.AdjustmentBrightness, 1.3f));
        Set(c => c.UiScale = 1.25f, false);
        await Wait(0.1);
        Expect($"UI scale 125% (visible {tree.Root.GetVisibleRect().Size})", Mathf.IsEqualApprox(tree.Root.ContentScaleFactor, 1.25f));
        Set(c => c.UiScale = 1f, false);
        Set(c => c.ShowFps = true, false);
        await Wait(0.2);
        Expect("FPS counter shows", FpsOverlay.Showing);
        Set(c => c.ShowFps = false, false);
        Expect("FPS counter hides", !FpsOverlay.Showing);
        Set(c => c.FpsCap = 30, false); Expect("FPS cap 30", Engine.MaxFps == 30);
        Set(c => c.FpsCap = 144, false); Expect("FPS cap 144", Engine.MaxFps == 144);
        Set(c => c.FpsCap = 0, false); Expect("FPS cap unlimited", Engine.MaxFps == 0);
        Set(c => c.VSync = 2, false); Expect("VSync adaptive stored", GraphicsSettings.Current.VSync == 2);
        Set(c => c.PropDensity = 0.5f); Expect("prop density 50% (next map)", GraphicsSettings.PropDensityScale == 0.5f);
        Set(c => c.MaxPopups = 32); Expect("max damage numbers 32", GraphicsSettings.MaxDamageNumbers == 32);

        // ------------------------------------------------------------ the page itself
        var panel = new GraphicsPanel(() => { }, 300f);
        main.AddChild(panel);
        await Wait(0.2);
        Expect($"page builds with its dropdowns ({panel.Options.Count})", panel.Options.Count >= 8);
        OptionButton preset = panel.Options[0];
        preset.Select(GraphicsSettings.Low);
        preset.EmitSignal(OptionButton.SignalName.ItemSelected, (long)GraphicsSettings.Low);
        Expect("page: picking Low applies it", GraphicsSettings.Current.Preset == GraphicsSettings.Low && !sun.ShadowEnabled);
        // Display: pick borderless, APPLY, then let it revert.
        OptionButton mode = panel.Options[1];
        int before = GraphicsSettings.Current.WindowMode;
        mode.Select(1);
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        foreach (Node n in panel.FindChildren("*", "Button", true, false))
            if (n is Button { Text: "APPLY" } apply) apply.EmitSignal(BaseButton.SignalName.Pressed);
        Expect("APPLY commits the window mode and asks to keep it", GraphicsSettings.Current.WindowMode == 1 && panel.Confirming && GraphicsPanel.Blocking);
        panel.Revert();
        Expect("revert restores the window mode", GraphicsSettings.Current.WindowMode == before && !panel.Confirming);
        panel.QueueFree();

        // ------------------------------------------------------------ reset
        GraphicsSettings.ResetDefaults();
        await Wait(0.3);
        Defaults("after reset");
        Finish(tree, ok);
    }

    private static void Finish(SceneTree tree, bool ok)
    {
        GD.Print(ok ? "GRAPHICS PASS" : "GRAPHICS FAIL");
        tree.Quit();
    }
}

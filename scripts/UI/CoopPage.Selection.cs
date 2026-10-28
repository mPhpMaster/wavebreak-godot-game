using System;
using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;
using Wavebreak.Net;

namespace Wavebreak.UI;

public sealed partial class CoopPage
{
    // ------------------------------------------------------------ character, skin, hat, difficulty

    private Label Picker(VBoxContainer column, string name, Action prev, Action next, List<Button> arrows)
    {
        var value = MainMenu.MakeLabel("", 17, Accent);
        value.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        value.ClipText = true;
        // Right to left, "previous" sits on the right and points that way.
        Button a = Arrow(Loc.IsRtl ? ">" : "<", prev, arrows);
        Button b = Arrow(Loc.IsRtl ? "<" : ">", next, arrows);
        column.AddChild(Row(name, a, value, b));
        return value;
    }

    private static Button Arrow(string text, Action pressed, List<Button> arrows)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(40f, 34f) };
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += pressed;
        arrows.Add(b);
        return b;
    }

    private static CharacterDef Current()
        => Characters.ById(Profile.SelectedCharacter) is { IsAvailable: true } c ? c : Characters.Default;

    private void CycleCharacter(int step)
    {
        var open = new List<CharacterDef>();
        foreach (CharacterDef c in Characters.All) if (c.IsAvailable) open.Add(c);
        if (open.Count == 0) return;
        int i = Math.Max(0, open.IndexOf(Current()));
        Profile.SelectedCharacter = open[((i + step) % open.Count + open.Count) % open.Count].Id;
        LookChanged();
    }

    private void CycleSkin(int step)
    {
        CharacterDef c = Current();
        var open = new List<SkinDef>();
        foreach (SkinDef s in c.Skins) if (s.IsAvailable) open.Add(s);
        if (open.Count == 0) return;
        int i = Math.Max(0, open.IndexOf(c.SelectedSkin));
        Profile.SelectSkin(c.Id, open[((i + step) % open.Count + open.Count) % open.Count].Id);
        LookChanged();
    }

    /// <summary>Owned hats only (the PLAY page previews and sells the rest).</summary>
    private void CycleHat(int step)
    {
        CharacterDef c = Current();
        var owned = new List<HatDef>();
        foreach (HatDef h in Hats.All) if (h.IsOwned) owned.Add(h);
        if (owned.Count == 0) return;
        int i = Math.Max(0, owned.IndexOf(Hats.SelectedFor(c.Id)));
        Hats.Select(c.Id, owned[((i + step) % owned.Count + owned.Count) % owned.Count]);
        LookChanged();
    }

    /// <summary>Host (or no session yet): the next unlocked tier that way.
    /// The choice is the profile's, so the PLAY page shows it too.</summary>
    private void StepDifficulty(int step)
    {
        if (Coop.IsClient || Coop.InRun) return;
        int n = DifficultyTiers.All.Count;
        int start = DifficultyTiers.IndexOf(DifficultyTiers.Selected);
        for (int k = 1; k <= n; k++)
        {
            int i = ((start + step * k) % n + n) % n;
            if (!DifficultyTiers.IsUnlocked(i)) continue;
            DifficultyTiers.Select(i);
            break;
        }
        Profile.Save();
        if (Coop.IsHost) NetSession.Instance?.SetLobbyDifficulty(DifficultyTiers.Selected.Id);
        Refresh();
    }

    /// <summary>Host (or no session yet): the next route; saved to the profile.</summary>
    private void StepRoute(int step)
    {
        if (Coop.IsClient || Coop.InRun) return;
        Routes.Cycle(step);
        Profile.Save();
        if (Coop.IsHost) NetSession.Instance?.SetLobbyRoute();
        Refresh();
    }

    private void LookChanged()
    {
        Profile.Save();
        NetSession.Instance?.SendLook();
        Refresh();
    }

    private static string SkinName(CharacterDef c, string skinId)
    {
        foreach (SkinDef s in c.Skins) if (s.Id == skinId) return Loc.T(s.Name);
        return Loc.T(c.Skins[0].Name);
    }

    private static LineEdit Field(string text, int maxLength)
    {
        var f = new LineEdit { Text = text, MaxLength = maxLength, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(150f, 40f) };
        f.AddThemeFontSizeOverride("font_size", 18);
        f.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return f;
    }

    private static Control Row(string name, params Control[] controls)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        Label l = MainMenu.MakeLabel(name, 16, Ink);
        l.CustomMinimumSize = new Vector2(100f, 0f);
        l.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(l);
        foreach (Control c in controls)
        {
            if (c is Button b && b.CustomMinimumSize.X < 1f) b.CustomMinimumSize = new Vector2(110f, 44f);
            row.AddChild(c);
        }
        return row;
    }

    // ------------------------------------------------------------ settings

    private string _savedPort = Coop.DefaultPort.ToString();
    private string _savedAddress = "127.0.0.1:" + Coop.DefaultPort;

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;
        string name = cfg.GetValue("coop", "name", "").AsString();
        if (name.Length > 0) NetSession.PlayerName = NetSession.CleanName(name);
        _savedPort = cfg.GetValue("coop", "port", _savedPort).AsString();
        _savedAddress = cfg.GetValue("coop", "address", _savedAddress).AsString();
    }

    private void SaveSettings()
    {
        if (Launch.Automated) return;
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);   // keep the other sections
        cfg.SetValue("coop", "name", NetSession.PlayerName);
        cfg.SetValue("coop", "port", _port?.Text.Trim() ?? _savedPort);
        cfg.SetValue("coop", "address", _address?.Text.Trim() ?? _savedAddress);
        cfg.Save(SettingsPath);
    }
}

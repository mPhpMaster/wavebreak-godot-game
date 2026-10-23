using System.Collections.Generic;
using Godot;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Profile tab: unlocks, Pearls, the Pearl Shop, difficulty tiers,
/// found shrines and a full reset. Works on the title screen too. Changes are
/// saved to the profile at once (never for an automated run) and the title
/// menu is rebuilt so they show immediately.</summary>
public sealed partial class DevPanel
{
    private readonly List<(UnlockDef Def, Button Row)> _unlockRows = new();
    private int _unlockFilter;   // 0 all, 1 locked, 2 unlocked
    private ulong _resetArmedAt;

    private Control BuildProfileTab()
    {
        _unlockRows.Clear();
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };

        Label pearls = DevUi.Lbl("", 15, DevUi.Gold);
        Label summary = DevUi.Lbl("", 14, DevUi.Dim);
        OnRefresh(() =>
        {
            pearls.Text = $"{Loc.T(Pearls.Name)}: {Loc.Ltr($"{Pearls.Balance:N0}")}";
            int tiers = 0;
            for (int i = 0; i < DifficultyTiers.All.Count; i++) if (DifficultyTiers.IsUnlocked(i)) tiers++;
            summary.Text = Loc.F("unlocks {0}/{1}  ·  shrines {2}/{3}  ·  tiers {4}/{5}  ·  shop {6}",
                                 Profile.UnlockedIds.Count, Unlocks.All.Count, Profile.FoundSecrets.Count, SecretSpawner.Kinds.Count,
                                 tiers, DifficultyTiers.All.Count, MetaShop.Describe());
        });

        page.AddChild(DevUi.Row(DevUi.Heading("UNLOCKS"), summary));
        page.AddChild(DevUi.Flow(
            Reg("unlock_all", DevUi.Btn("UNLOCK EVERYTHING", () => ProfileAct("Unlock everything", () =>
            {
                int n = DevCheats.UnlockEverything();
                return $"Unlocked everything: {n} new rows, all characters, skins, hats, tiers and shrines.";
            }), DevUi.Gold)),
            Reg("unlock_tiers", DevUi.Btn("Unlock all difficulty tiers", () => ProfileAct("Unlock tiers", () =>
            {
                DevCheats.UnlockAllTiers();
                Profile.Save();
                return "Every difficulty tier is open.";
            }))),
            Reg("shrines_found", DevUi.Btn("Mark all hidden shrines found", () => ProfileAct("Shrines found", () =>
            {
                DevCheats.MarkAllShrinesFound();
                Profile.Save();
                return "Every hidden shrine kind is marked found.";
            })))));

        page.AddChild(DevUi.Row(DevUi.Heading("PEARLS & SHOP"), pearls));
        var money = DevUi.Flow();
        foreach (long amount in new long[] { 100, 1000, 10000 })
        {
            long a = amount;
            money.AddChild(Reg($"pearls_{a}", DevUi.Btn(Loc.IsArabic ? $"{Loc.Ltr($"+{a:N0}")} {Loc.T(Pearls.Name)}" : $"+{a:N0} {Pearls.Name}", () => ProfileAct("Pearls", () =>
            {
                DevCheats.GivePearls(a);
                return $"+{a:N0} {Pearls.Name} (balance {Pearls.Balance:N0}).";
            }), DevUi.Gold)));
        }
        money.AddChild(Reg("shop_max", DevUi.Btn("Max the Pearl Shop", () => ProfileAct("Max shop", () =>
        {
            DevCheats.MaxShop();
            return $"Pearl Shop maxed: {MetaShop.Describe()} (applies from the next run).";
        }))));
        money.AddChild(Reg("shop_reset", DevUi.Btn("Reset the Pearl Shop", () => ProfileAct("Reset shop", () =>
        {
            DevCheats.ResetShop();
            return "Pearl Shop levels back to zero (no refund).";
        }))));
        Button reset = null!;
        reset = Reg("profile_reset", DevUi.Btn("Reset whole profile...", () =>
        {
            ulong now = Time.GetTicksMsec();
            if (_resetArmedAt == 0 || now - _resetArmedAt > 5000)
            {
                _resetArmedAt = now;
                reset.Text = Loc.T("CLICK AGAIN TO WIPE THE PROFILE");
                Say("Reset armed: click the red button again within 5 seconds to wipe everything.", false);
                return;
            }
            _resetArmedAt = 0;
            reset.Text = Loc.T("Reset whole profile...");
            ProfileAct("Reset profile", () =>
            {
                DevCheats.ResetProfile();
                return "Profile wiped: a brand-new save.";
            });
        }, DevUi.Bad));
        money.AddChild(reset);
        page.AddChild(money);

        page.AddChild(DevUi.Heading("UNLOCK LIST  ·  click a row to unlock or lock it"));
        var list = new DevFilterList("filter unlocks: name, reward, description...");
        foreach (var (label, mode) in new[] { ("All", 0), ("Locked", 1), ("Unlocked", 2) })
        {
            int m = mode;
            list.Bar.AddChild(DevUi.Btn(label, () => { _unlockFilter = m; list.Apply(); }, minWidth: 80f));
        }
        list.Extra = tag => tag is UnlockDef u && (_unlockFilter == 0 || Profile.IsUnlocked(u.Id) == (_unlockFilter == 2));
        foreach (UnlockDef u in Unlocks.All)
        {
            UnlockDef def = u;
            var row = new Button { FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left,
                                   ClipText = true, CustomMinimumSize = new Vector2(0f, 28f) };
            row.AddThemeFontSizeOverride("font_size", 14);
            row.Pressed += () => ProfileAct(def.Name, () =>
            {
                bool to = !Profile.IsUnlocked(def.Id);
                DevCheats.SetUnlocked(def, to);
                return $"{(to ? "Unlocked" : "Locked")}: {def.Name} ({Unlocks.DescribeReward(def)}).";
            });
            list.Add($"{def.Id} {def.Name} {Unlocks.DescribeReward(def)} {def.Description}", row, def);
            _unlockRows.Add((def, row));
            _buttons[$"unlock:{def.Id}"] = row;
        }
        list.Apply();
        page.AddChild(list.Root);
        RefreshUnlockRows();
        return page;
    }

    private void RefreshUnlockRows()
    {
        foreach (var (u, row) in _unlockRows)
        {
            if (!IsInstanceValid(row)) continue;
            bool on = Profile.IsUnlocked(u.Id);
            string secret = u.Secret ? Loc.T("  [secret]") : "";
            row.Text = $"{(on ? "[x]" : "[  ]")}  {Loc.T(u.Name)}{secret}   ·   {Unlocks.DescribeReward(u)}   ·   {Loc.T(u.Description)}";
            row.AddThemeColorOverride("font_color", on ? DevUi.Good : u.Secret ? DevUi.Dim : DevUi.Ink);
        }
    }

    /// <summary>A profile action: applied and saved, rows refreshed, and the
    /// title menu rebuilt so the change shows there.</summary>
    private void ProfileAct(string what, System.Func<string?> action)
    {
        if (Act(what, action, runSide: false))
        {
            RefreshUnlockRows();
            RequestTitleRefresh();
        }
    }
}

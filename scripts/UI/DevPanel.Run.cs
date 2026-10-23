using System.Collections.Generic;
using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;
using Wavebreak.Progression;
using Wavebreak.Stats;

namespace Wavebreak.UI;

/// <summary>Player and Build tabs (run only).</summary>
public sealed partial class DevPanel
{
    private static readonly object SpeedSource = new();
    private static readonly object ChargeSource = new();
    private const int FlyJumps = 999;

    private static Main M => Main.Instance!;

    // ================================================================ player

    private Control BuildPlayerTab()
    {
        var (scroll, page) = DevUi.Scroll();

        page.AddChild(DevUi.Heading("CHEATS"));
        page.AddChild(DevUi.Flow(
            Reg("god", DevUi.Toggle("God mode (no damage)", _god || Screenshot.Invulnerable, on => Act("God mode", () =>
            {
                _god = on;
                Screenshot.Invulnerable = on;
                return on ? "God mode on: nothing can hurt you." : "God mode off.";
            }))),
            Reg("fly", DevUi.Toggle("Fly (endless air jumps, low gravity)", _fly, on => Act("Fly", () =>
            {
                _fly = on;
                ApplyFly(M.Player);
                return on ? "Fly on: jump again in the air as often as you like." : "Fly off.";
            }))),
            Reg("autopick", DevUi.Toggle("Auto-pick level-up cards", _autoPick, on => Act("Auto-pick", () =>
            {
                _autoPick = on;
                M.LevelUp.AutoPickSeconds = on ? 0.05f : 0f;
                return on ? "Level-up cards are picked automatically." : "Level-up cards wait for you again.";
            })))));

        Label hp = DevUi.Lbl("", 15, DevUi.Good);
        page.AddChild(DevUi.Row(DevUi.Heading("HEALTH"), hp));
        page.AddChild(DevUi.Flow(
            Reg("heal", DevUi.Btn("Full heal", () => Act("Full heal", () =>
            {
                M.Player.Health.HealFraction(1f);
                return $"Healed to {M.Player.Health.Current:0}/{M.Player.Health.Max:0}.";
            }), DevUi.Good)),
            Reg("maxhp", DevUi.Btn("+100 max health", () => Act("Max health", () =>
            {
                M.Player.Stats.Add(Stat.MaxHealth, ModifierOp.Flat, 100f, Source);
                M.Player.Health.HealFraction(1f);
                return $"Max health now {M.Player.Health.Max:0}.";
            })))));

        Label gold = DevUi.Lbl("", 15, DevUi.Gold);
        page.AddChild(DevUi.Row(DevUi.Heading("GOLD"), gold));
        var goldRow = DevUi.Flow();
        foreach (int amount in new[] { 100, 1000, 10000 })
        {
            int a = amount;
            goldRow.AddChild(Reg($"gold_{a}", DevUi.Btn(Loc.F("+{0:N0} gold", a), () => Act("Gold", () =>
            {
                M.Inventory.DevSetGold(M.Inventory.Gold + a);
                return $"+{a:N0} gold (now {M.Inventory.Gold:N0}).";
            }), DevUi.Gold)));
        }
        SpinBox goldSet = DevUi.Spin(0, 1_000_000, 500, 50);
        goldRow.AddChild(goldSet);
        goldRow.AddChild(Reg("gold_set", DevUi.Btn("Set gold", () => Act("Set gold", () =>
        {
            M.Inventory.DevSetGold((int)goldSet.Value);
            return $"Gold set to {M.Inventory.Gold:N0}.";
        }))));
        page.AddChild(goldRow);

        Label level = DevUi.Lbl("", 15, DevUi.Accent);
        page.AddChild(DevUi.Row(DevUi.Heading("EXPERIENCE"), level));
        var xpRow = DevUi.Flow();
        xpRow.AddChild(Reg("xp_100", DevUi.Btn("+100 XP", () => Act("XP", () =>
        {
            M.Inventory.DevAddXp(100);
            return $"+100 XP (level {M.Inventory.Level}).";
        }))));
        foreach (int n in new[] { 1, 5, 10 })
        {
            int k = n;
            xpRow.AddChild(Reg($"level_{k}", DevUi.Btn(Loc.F("Level up x{0}", k), () => LevelUp(k))));
        }
        SpinBox levels = DevUi.Spin(1, 200, 25);
        xpRow.AddChild(levels);
        xpRow.AddChild(Reg("level_n", DevUi.Btn("Level up N times", () => LevelUp((int)levels.Value))));
        page.AddChild(xpRow);

        Label charges = DevUi.Lbl("", 15, DevUi.Accent);
        page.AddChild(DevUi.Row(DevUi.Heading("LEVEL-UP CHARGES"), charges));
        page.AddChild(DevUi.Flow(
            Reg("rerolls", DevUi.Btn("+5 rerolls", () => GiveCharges(Stat.Rerolls, "rerolls"))),
            Reg("banishes", DevUi.Btn("+5 banishes", () => GiveCharges(Stat.Banishes, "banishes"))),
            Reg("skips", DevUi.Btn("+5 skips", () => GiveCharges(Stat.Skips, "skips")))));

        Label speed = DevUi.Lbl("", 15, DevUi.Accent);
        page.AddChild(DevUi.Row(DevUi.Heading("MOVE SPEED"), speed));
        var speedRow = DevUi.Flow();
        foreach (var (id, label, bonus) in new[] { ("speed_0", "x1 (normal)", 0f), ("speed_05", "+50%", 0.5f), ("speed_1", "x2", 1f),
                                                   ("speed_2", "x3", 2f), ("speed_4", "x5", 4f) })
        {
            float b = bonus;
            speedRow.AddChild(Reg(id, DevUi.Btn(label, () => Act("Move speed", () =>
            {
                _speedBonus = b;
                ApplySpeed(M.Player);
                return $"Move speed {M.Player.Stats.Get(Stat.MoveSpeed):0.0} m/s.";
            }))));
        }
        page.AddChild(speedRow);

        page.AddChild(DevUi.Heading("TELEPORT"));
        page.AddChild(DevUi.Flow(
            Reg("tp_centre", DevUi.Btn("Map centre", () => Teleport("map centre", Arena.Centre))),
            Reg("tp_portal", DevUi.Btn("Portal", () => Teleport("portal", M.Stages.PortalPosition))),
            Reg("tp_boss", DevUi.Btn("Boss", () => Teleport("boss", M.Stages.BossPosition is { } b ? b + Vector3.Back * 6f : null))),
            Reg("tp_chest", DevUi.Btn("Nearest chest", () => Teleport("nearest chest",
                ChestSpawner.Instance?.Nearest(M.Player.GlobalPosition, 9999f, c => !c.IsOpen)?.GlobalPosition))),
            Reg("tp_shrine", DevUi.Btn("Nearest shrine", () => Teleport("nearest shrine",
                ShrineSpawner.Instance?.Nearest(M.Player.GlobalPosition, 9999f)?.GlobalPosition))),
            Reg("tp_secret", DevUi.Btn("Nearest hidden shrine", () => Teleport("nearest hidden shrine",
                Meta.SecretSpawner.Placed.Where(s => !s.Spent).OrderBy(s => s.GlobalPosition.DistanceTo(M.Player.GlobalPosition))
                    .Select(s => (Vector3?)s.GlobalPosition).FirstOrDefault()))),
            Reg("tp_merchant", DevUi.Btn("Nearest merchant", () => Teleport("nearest merchant", NearestStation<Merchant>()))),
            Reg("tp_microwave", DevUi.Btn("Nearest microwave", () => Teleport("nearest microwave", NearestStation<Microwave>())))));

        OnRefresh(() =>
        {
            if (Main.Instance is not { } m || !IsInstanceValid(m)) return;
            PlayerHealth h = m.Player.Health;
            hp.Text = Loc.Ltr($"{h.Current:0} / {h.Max:0}") + (Screenshot.Invulnerable ? Loc.T("   (god mode)") : "");
            gold.Text = Loc.F("{0:N0} gold", m.Inventory.Gold);
            level.Text = Loc.F("level {0}  ·  {1}/{2} XP", m.Inventory.Level, m.Inventory.XpIntoLevel, m.Inventory.XpForNextLevel);
            if (LevelUpChoices.Current is { } c)
                charges.Text = Loc.F("rerolls {0}  ·  banishes {1}  ·  skips {2}", c.RerollsLeft, c.BanishesLeft, c.SkipsLeft);
            speed.Text = Loc.F("{0:0.0} m/s", m.Player.Stats.Get(Stat.MoveSpeed));
        });
        return scroll;
    }

    private bool LevelUp(int n)
    {
        return Act($"Level up x{n}", () =>
        {
            int before = M.Inventory.Level;
            M.Inventory.DevLevelUp(n);
            int gained = M.Inventory.Level - before;
            if (gained <= 0) return null;
            return _autoPick
                ? $"+{gained} levels (now {M.Inventory.Level}); auto-pick is taking the cards."
                : $"+{gained} levels (now {M.Inventory.Level}); {gained} choices are queued behind this panel.";
        });
    }

    private void GiveCharges(Stat stat, string name) => Act($"+5 {name}", () =>
    {
        M.Player.Stats.Add(stat, ModifierOp.Flat, 5f, ChargeSource);
        return $"+5 {name}.";
    });

    private Vector3? NearestStation<T>() where T : Station
    {
        if (Interactables.Instance is not { } hub) return null;
        Vector3 me = M.Player.GlobalPosition;
        return hub.Stations.OfType<T>().Where(s => IsInstanceValid(s))
                  .OrderBy(s => s.GlobalPosition.DistanceSquaredTo(me))
                  .Select(s => (Vector3?)s.GlobalPosition).FirstOrDefault();
    }

    private bool Teleport(string what, Vector3? target) => Act($"Teleport to {what}", () =>
    {
        if (target is not { } t) return null;
        // A little to the side, so the player does not land inside the thing.
        Vector3 to = t + new Vector3(0f, 0.6f, 0f);
        if (what != "map centre")
        {
            Vector3 away = M.Player.GlobalPosition - t;
            away.Y = 0f;
            to += (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Back) * 2.5f;
        }
        M.Player.Teleport(to);
        PlayerCamera.Instance?.SnapToTarget();
        return $"Teleported to the {what}.";
    });

    private static void ApplyFly(Player player)
    {
        bool has = player.HasMeta("dev_fly");
        if (_fly == has) return;
        if (_fly)
        {
            player.ExtraAirJumps += FlyJumps;
            player.SetGravityScale(0.35f);
            player.SetMeta("dev_fly", true);
        }
        else
        {
            player.ExtraAirJumps -= FlyJumps;
            player.SetGravityScale(Meta.Challenges.IsActive("lowgrav") ? 0.5f : 1f);
            player.RemoveMeta("dev_fly");
        }
    }

    private static void ApplySpeed(Player player)
    {
        player.Stats.RemoveAllFrom(SpeedSource);
        if (_speedBonus != 0f) player.Stats.Add(Stat.MoveSpeed, ModifierOp.Additive, _speedBonus, SpeedSource);
    }

    // ================================================================= build

    private VBoxContainer _buildSide = null!;
    private DevFilterList? _buildList;
    private string _buildSignature = "";
    private int _contentKind;   // 0 all, 1 weapons, 2 items, 3 tomes

    public static IEnumerable<WeaponDef> AllWeapons => Catalog.Weapons.Concat(Meta.MetaContent.Weapons).GroupBy(w => w.Id).Select(g => g.First());
    public static IEnumerable<ItemDef> AllItems => Catalog.Items.Concat(Meta.MetaContent.Items).GroupBy(i => i.Id).Select(g => g.First());

    private Control BuildBuildTab()
    {
        var split = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 16);

        // ---- left: everything that exists
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.45f };
        split.AddChild(left);
        var weapons = AllWeapons.ToList();
        var items = AllItems.ToList();
        var tomes = Tomes.All.ToList();
        left.AddChild(DevUi.Heading(Loc.F("ADD ANYTHING  ·  {0} weapons, {1} items, {2} tomes", weapons.Count, items.Count, tomes.Count)));
        var list = new DevFilterList("search weapons, items, tomes...");
        _buildList = list;
        foreach (var (label, kind) in new[] { ("All", 0), ("Weapons", 1), ("Items", 2), ("Tomes", 3) })
        {
            int k = kind;
            list.Bar.AddChild(Reg($"kind_{k}", DevUi.Btn(label, () => { _contentKind = k; list.Apply(); }, minWidth: 72f)));
        }
        list.Extra = tag => _contentKind == 0 || (tag is int k && k == _contentKind);

        foreach (WeaponDef w in weapons)
        {
            WeaponDef def = w;
            list.Add($"weapon {def.Id} {def.DisplayName} {def.Rarity}", ContentRow(Icons.For(def), def.DisplayName, Catalog.RarityColor(def.Rarity),
                Kind("weapon", def.Rarity),
                Reg($"add_weapon:{def.Id}", DevUi.Btn("Add", () => AddWeapon(def, max: false), minWidth: 56f)),
                Reg($"max_weapon:{def.Id}", DevUi.Btn("Max", () => AddWeapon(def, max: true), minWidth: 56f))), 1);
        }
        foreach (ItemDef i in items)
        {
            ItemDef def = i;
            list.Add($"item {def.Id} {def.DisplayName} {def.Rarity}", ContentRow(Icons.For(def), def.DisplayName, Catalog.RarityColor(def.Rarity),
                Kind("item", def.Rarity),
                Reg($"add_item:{def.Id}", DevUi.Btn("Add", () => AddItem(def, 1), minWidth: 56f)),
                Reg($"add5_item:{def.Id}", DevUi.Btn("+5", () => AddItem(def, 5), minWidth: 56f))), 2);
        }
        foreach (TomeDef t in tomes)
        {
            TomeDef def = t;
            list.Add($"tome {def.Id} {def.DisplayName} {def.Rarity}", ContentRow(Icons.For(def), def.DisplayName, Catalog.RarityColor(def.Rarity),
                Kind("tome", def.Rarity),
                Reg($"add_tome:{def.Id}", DevUi.Btn("Add", () => AddTome(def, max: false), minWidth: 56f)),
                Reg($"max_tome:{def.Id}", DevUi.Btn("Max", () => AddTome(def, max: true), minWidth: 56f))), 3);
        }
        list.Apply();
        left.AddChild(list.Root);

        // ---- right: what the player holds
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.AddChild(right);
        right.AddChild(DevUi.Heading("CURRENT BUILD"));
        right.AddChild(DevUi.Flow(
            Reg("max_weapons", DevUi.Btn("Max all weapons", () => Act("Max all weapons", () =>
            {
                foreach (WeaponRuntime w in M.Weapons.Weapons.ToList()) M.Weapons.DevMax(w.Def);
                return $"Every held weapon is at its top level.";
            }))),
            Reg("max_tomes", DevUi.Btn("Max all tomes", () => Act("Max all tomes", () =>
            {
                if (TomeBook.Current is not { } book) return null;
                foreach (TomeDef t in book.Held.ToList()) while (book.Add(t)) { }
                return "Every held tome is at its top level.";
            }))),
            Reg("clear_build", DevUi.Btn("Clear build", () => Act("Clear build", () =>
            {
                M.Weapons.Clear();
                M.Inventory.DevForgetAllWeapons();
                M.Inventory.DevClearItems();
                TomeBook.Current?.Clear();
                M.Hud.DevResync(M.Weapons, TomeBook.Current);
                return "Build cleared: no weapons, items or tomes.";
            }), DevUi.Bad))));
        var (scroll, body) = DevUi.Scroll();
        _buildSide = body;
        right.AddChild(scroll);

        OnRefresh(() =>
        {
            if (Main.Instance is null) return;
            string sig = BuildSignature();
            if (sig != _buildSignature) RebuildBuildSide();
        });
        RebuildBuildSide();
        return split;
    }

    /// <summary>"weapon · Rare", in the player's language.</summary>
    private static string Kind(string kind, Rarity rarity)
        => Loc.IsArabic ? $"{Loc.T(kind)} · {Loc.T(Catalog.RarityName(rarity))}" : $"{kind} · {rarity}";

    private static HBoxContainer ContentRow(IconSpec icon, string name, Color color, string kind, params Control[] buttons)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0f, 32f) };
        IconView view = IconView.Make(28f);
        view.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        view.Set(icon, color);
        row.AddChild(view);
        Label n = DevUi.Lbl(name, 15, color);
        n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        n.ClipText = true;
        row.AddChild(n);
        Label k = DevUi.Lbl(kind, 13, DevUi.Dim);
        // No kind (the build side): the name gets the room.
        k.CustomMinimumSize = new Vector2(kind.Length > 0 ? 130f : 0f, 0f);
        row.AddChild(k);
        foreach (Control b in buttons) row.AddChild(b);
        return row;
    }

    private static string BuildSignature()
    {
        var m = M;
        string w = string.Join(",", m.Weapons.Weapons.Select(x => $"{x.Def.Id}:{x.Level}"));
        string t = TomeBook.Current is { } b ? string.Join(",", b.Held.Select(x => $"{x.Id}:{b.LevelOf(x)}")) : "";
        return $"{w}|{m.Inventory.TotalStacks}:{m.Inventory.DistinctItems}|{t}";
    }

    private void RebuildBuildSide()
    {
        if (_buildSide is null || !IsInstanceValid(_buildSide) || Main.Instance is null) return;
        _buildSignature = BuildSignature();
        foreach (Node n in _buildSide.GetChildren()) n.QueueFree();
        Main m = M;

        _buildSide.AddChild(DevUi.Lbl(Loc.F("WEAPONS  {0}/{1}", m.Weapons.Weapons.Count, WeaponSystem.MaxWeapons), 14, DevUi.Dim));
        foreach (WeaponRuntime w in m.Weapons.Weapons.ToList())
        {
            WeaponDef def = w.Def;
            _buildSide.AddChild(ContentRow(Icons.For(def), Loc.F("{0}  Lv {1}/{2}", def.DisplayName, w.Level, def.MaxLevel), Catalog.RarityColor(def.Rarity), "",
                DevUi.Btn("Max", () => AddWeapon(def, max: true), minWidth: 50f),
                Reg($"remove_weapon:{def.Id}", DevUi.Btn("Remove", () => Act($"Remove {def.DisplayName}", () =>
                {
                    if (!M.Weapons.DevRemove(def.Id)) return null;
                    M.Inventory.DevForgetWeapon(def.Id);
                    M.Hud.DevResync(M.Weapons, TomeBook.Current);
                    return $"Removed {def.DisplayName}.";
                }), DevUi.Bad, 70f))));
        }

        TomeBook? book = TomeBook.Current;
        _buildSide.AddChild(DevUi.Lbl(Loc.F("TOMES  {0}/{1}", book?.Count ?? 0, book?.Slots ?? Tomes.MaxSlots), 14, DevUi.Dim));
        if (book is not null)
            foreach (TomeDef t in book.Held.ToList())
            {
                TomeDef def = t;
                _buildSide.AddChild(ContentRow(Icons.For(def), Loc.F("{0}  Lv {1}/{2}", def.DisplayName, book.LevelOf(def), def.MaxLevel),
                    Catalog.RarityColor(def.Rarity), "", DevUi.Btn("Max", () => AddTome(def, max: true), minWidth: 50f)));
            }

        _buildSide.AddChild(DevUi.Lbl(Loc.F("ITEMS  {0}", m.Inventory.TotalStacks), 14, DevUi.Dim));
        foreach (ItemStack s in m.Inventory.Items.ToList())
        {
            ItemDef def = s.Def;
            _buildSide.AddChild(ContentRow(Icons.For(def), s.Count > 1 ? $"{Loc.T(def.DisplayName)}  {Loc.Ltr($"x{s.Count}")}" : def.DisplayName,
                Catalog.RarityColor(def.Rarity), "",
                DevUi.Btn("+1", () => AddItem(def, 1), minWidth: 40f),
                Reg($"remove_item:{def.Id}", DevUi.Btn("-1", () => Act($"Remove {def.DisplayName}", () =>
                    M.Inventory.DevRemove(def) ? $"Removed one {def.DisplayName}." : null), DevUi.Bad, 40f))));
        }
    }

    private bool AddWeapon(WeaponDef def, bool max) => Act($"Add {def.DisplayName}", () =>
    {
        WeaponSystem ws = M.Weapons;
        if (!ws.CanAdd(def))
            return ws.Has(def.Id) ? null : throw new System.InvalidOperationException($"all {WeaponSystem.MaxWeapons} weapon slots are full");
        if (max) ws.DevMax(def);
        else ws.Add(def);
        return $"{def.DisplayName} is now level {ws.LevelOf(def.Id)}.";
    });

    private bool AddItem(ItemDef def, int copies) => Act($"Add {def.DisplayName}", () =>
    {
        int added = 0;
        for (int i = 0; i < copies; i++) if (M.Inventory.Add(def)) added++;
        return added > 0 ? $"+{added} {def.DisplayName} (holding {M.Inventory.StacksOf(def)})." : null;
    });

    private bool AddTome(TomeDef def, bool max) => Act($"Add {def.DisplayName}", () =>
    {
        if (TomeBook.Current is not { } book) return null;
        if (!book.CanTake(def))
            return book.LevelOf(def) > 0 ? null : throw new System.InvalidOperationException("every tome slot is full");
        if (max) while (book.Add(def)) { }
        else book.Add(def);
        return $"{def.DisplayName} is now level {book.LevelOf(def)}.";
    });
}

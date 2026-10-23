using System.Linq;
using Godot;
using Wavebreak.Actors;
using Wavebreak.Combat;
using Wavebreak.Core;
using Wavebreak.Data;
using Wavebreak.Game;
using Wavebreak.Meta;

namespace Wavebreak.UI;

/// <summary>Spawn and Stage tabs (run only).</summary>
public sealed partial class DevPanel
{
    private static float _spawnDistance = 7f;
    private int _enemyFilter;   // 0 all, 1 normal, 2 elite, 3 boss

    // ================================================================= spawn

    /// <summary>Where spawned things go: a few metres in front of the camera,
    /// on the ground, kept inside the map.</summary>
    private static int _spawnSerial;
    private static ulong _lastSpawnMsec;

    /// <summary>The spawn point, nudged sideways for things placed in quick
    /// succession so a burst of clicks lines them up instead of stacking them.</summary>
    private static Vector3 NextSpot()
    {
        ulong now = Time.GetTicksMsec();
        _spawnSerial = now - _lastSpawnMsec < 4000 ? _spawnSerial + 1 : 0;
        _lastSpawnMsec = now;
        int k = _spawnSerial % 9;
        float side = (k + 1) / 2 * 3f * (k % 2 == 1 ? 1f : -1f);
        return SpawnPoint(side);
    }

    private static Vector3 SpawnPoint(float sideways = 0f)
    {
        Player p = M.Player;
        Vector3 fwd = PlayerCamera.Instance is { } cam ? cam.FlatForward : new Vector3(p.AimDirection.X, 0f, p.AimDirection.Z);
        if (fwd.LengthSquared() < 0.01f) fwd = Vector3.Forward;
        fwd = fwd.Normalized();
        Vector3 right = new(-fwd.Z, 0f, fwd.X);
        Vector3 at = p.GlobalPosition + fwd * _spawnDistance + right * sideways;

        var flat = new Vector2(at.X - Arena.Centre.X, at.Z - Arena.Centre.Z);
        float max = M.Stages.Stage.InRadius - 4f;
        if (flat.Length() > max) { flat = flat.Normalized() * max; at.X = Arena.Centre.X + flat.X; at.Z = Arena.Centre.Z + flat.Y; }

        PhysicsDirectSpaceState3D? space = p.GetWorld3D()?.DirectSpaceState;
        if (space is not null)
        {
            var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 30f, at + Vector3.Down * 30f, 1u,
                                                           new Godot.Collections.Array<Rid> { p.GetRid() });
            var hit = space.IntersectRay(query);
            at.Y = hit.Count > 0 ? hit["position"].AsVector3().Y : p.GlobalPosition.Y;
        }
        return at;
    }

    private Control BuildSpawnTab()
    {
        var split = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 16);

        // ---- left: enemies
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        split.AddChild(left);
        var enemies = Catalog.Enemies.ToList();
        SpinBox count = DevUi.Spin(1, 60, 1);
        left.AddChild(DevUi.Row(DevUi.Heading(Loc.F("ENEMIES  ·  {0}", enemies.Count)), DevUi.Lbl("   count", 14, DevUi.Dim), count));
        var list = new DevFilterList("search enemies...");
        foreach (var (label, mode) in new[] { ("All", 0), ("Normal", 1), ("Elite", 2), ("Boss", 3) })
        {
            int m = mode;
            list.Bar.AddChild(DevUi.Btn(label, () => { _enemyFilter = m; list.Apply(); }, minWidth: 64f));
        }
        list.Extra = tag => tag is EnemyDef d && _enemyFilter switch
        {
            1 => !d.IsElite && !d.IsBoss,
            2 => d.IsElite && !d.IsBoss,
            3 => d.IsBoss,
            _ => true,
        };
        foreach (EnemyDef e in enemies)
        {
            EnemyDef def = e;
            string tag = def.IsBoss ? "BOSS" : def.IsElite ? "ELITE" : "";
            Color c = def.IsBoss ? DevUi.Bad : def.IsElite ? DevUi.Gold : DevUi.Ink;
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0f, 30f) };
            Label n = DevUi.Lbl(def.DisplayName, 15, c);
            n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            n.ClipText = true;
            row.AddChild(n);
            Label info = DevUi.Lbl(Loc.F("{0}  {1:0} hp  ·  from {2}", tag, def.Health, RunState.Format(def.UnlockAtSeconds)), 13, DevUi.Dim);
            info.CustomMinimumSize = new Vector2(190f, 0f);
            row.AddChild(info);
            row.AddChild(Reg($"spawn_enemy:{def.Id}", DevUi.Btn("Spawn", () => SpawnEnemies(def, (int)count.Value), minWidth: 70f)));
            list.Add($"{def.Id} {def.DisplayName} {tag}", row, def);
        }
        list.Apply();
        left.AddChild(list.Root);

        // ---- right: everything else
        var (scroll, right) = DevUi.Scroll();
        split.AddChild(scroll);

        SpinBox dist = DevUi.Spin(2, 40, _spawnDistance);
        dist.ValueChanged += v => _spawnDistance = (float)v;
        right.AddChild(DevUi.Row(DevUi.Lbl("Spawn point: metres in front of the camera", 14, DevUi.Dim), dist));

        right.AddChild(DevUi.Heading("CHESTS"));
        right.AddChild(DevUi.Flow(
            Reg("chest_standard", DevUi.Btn("Chest", () => SpawnChest(ChestTier.Standard, false))),
            Reg("chest_gilded", DevUi.Btn("Gilded chest", () => SpawnChest(ChestTier.Gilded, false), DevUi.Gold)),
            Reg("chest_free", DevUi.Btn("Free chest", () => SpawnChest(ChestTier.Standard, true))),
            Reg("chest_free_gilded", DevUi.Btn("Free gilded chest", () => SpawnChest(ChestTier.Gilded, true), DevUi.Gold))));

        right.AddChild(DevUi.Heading("SHRINES"));
        var shrines = DevUi.Flow();
        foreach (MapShrineKind kind in System.Enum.GetValues<MapShrineKind>())
        {
            MapShrineKind k = kind;
            shrines.AddChild(Reg($"shrine_{k}", DevUi.Btn(Loc.IsArabic ? Loc.T(MapShrines.Name(k)) : $"{k} shrine", () => SpawnShrine(k))));
        }
        foreach (SecretKindDef secret in SecretSpawner.Kinds)
        {
            SecretKindDef k = secret;
            shrines.AddChild(Reg($"secret_{k.Id}", DevUi.Btn(Loc.F("Hidden: {0}", k.DisplayName), () => SpawnSecret(k), DevUi.Accent)));
        }
        right.AddChild(shrines);

        right.AddChild(DevUi.Heading("STATIONS & PICKUPS"));
        right.AddChild(DevUi.Flow(
            Reg("merchant", DevUi.Btn("Merchant", () => SpawnStation(new Merchant(), "merchant"))),
            Reg("microwave", DevUi.Btn("Microwave", () => SpawnStation(new Microwave(), "microwave"))),
            Reg("pots", DevUi.Btn("6 pots", () => Act("Pots", () =>
                Interactables.Instance?.SpawnPotsAt(NextSpot(), 6) is > 0 ? "6 breakable pots placed." : null))),
            Reg("pickup_xp", DevUi.Btn("10 XP gems", () => SpawnPickups(PickupKind.Xp, 10, 10f, "XP gems"))),
            Reg("pickup_gold", DevUi.Btn("10 gold coins", () => SpawnPickups(PickupKind.Gold, 10, 10f, "gold coins"), DevUi.Gold)),
            Reg("pickup_heal", DevUi.Btn("Heal pack", () => SpawnPickups(PickupKind.Health, 1, 0.5f, "heal pack"), DevUi.Good))));

        right.AddChild(DevUi.Heading("FIELD"));
        right.AddChild(DevUi.Flow(
            Reg("kill_all", DevUi.Btn("Kill all enemies", KillAll, DevUi.Bad)),
            Reg("clear_shots", DevUi.Btn("Clear enemy projectiles", () => Act("Clear projectiles", () =>
            {
                int n = EnemyAttacks.ActiveShots + EnemyAttacks.ActiveHazards;
                EnemyAttacks.Clear();
                return $"Cleared {n} enemy shots and hazards.";
            }))),
            Reg("freeze", DevUi.Toggle("Freeze AI", EnemyManager.DevFrozen, on => Act("Freeze AI", () =>
            {
                EnemyManager.DevFrozen = on;
                return on ? "Enemies are frozen and nothing new spawns." : "Enemies move again.";
            })))));
        return split;
    }

    private bool SpawnEnemies(EnemyDef def, int count) => Act($"Spawn {def.DisplayName}", () =>
    {
        EnemyManager em = M.Enemies;
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            float side = count == 1 ? 0f : (i - (count - 1) * 0.5f) * 1.6f;
            if (em.SpawnAt(def, SpawnPoint(side) + Vector3.Up * 0.2f) is not null) made++;
        }
        if (made == 0) throw new System.InvalidOperationException($"no room ({em.AliveCount}/{em.MaxAlive} alive)");
        return made < count ? $"Spawned {made} x {def.DisplayName} ({count - made} refused: live cap)." : $"Spawned {made} x {def.DisplayName}.";
    });

    private bool SpawnChest(ChestTier tier, bool free) => Act("Chest", () =>
    {
        Chest? c = ChestSpawner.Instance?.SpawnAt(tier, free, NextSpot());
        return c is null ? null : free ? $"Free {tier} chest placed." : $"{tier} chest placed.";
    });

    private bool SpawnShrine(MapShrineKind kind) => Act($"{kind} shrine", () =>
    {
        if (ShrineSpawner.Instance is not { } s || M.Stages.DevArena is not { } arena) return null;
        s.SpawnAt(kind, arena, NextSpot());
        return $"{kind} shrine placed.";
    });

    private bool SpawnSecret(SecretKindDef kind) => Act($"Hidden shrine {kind.DisplayName}", () =>
    {
        if (M.Stages.DevArena is not { } arena) return null;
        var shrine = new Shrine(kind);
        arena.AddChild(shrine);
        shrine.GlobalPosition = NextSpot();
        return $"Hidden shrine ({kind.DisplayName}) placed.";
    });

    private bool SpawnStation(Station station, string what) => Act(what, () =>
        Interactables.Instance?.SpawnStationAt(station, NextSpot()) is not null ? $"A {what} is placed." : null);

    private bool SpawnPickups(PickupKind kind, int n, float value, string what) => Act(what, () =>
    {
        Vector3 at = NextSpot() + Vector3.Up * 0.6f;
        int made = 0;
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            if (Pickup.Spawn(kind, at, value, new Vector3(Mathf.Cos(a), 2.5f, Mathf.Sin(a)) * 1.5f) is not null) made++;
        }
        return made > 0 ? $"Spawned {made} {what}." : null;
    });

    private void KillAll() => Act("Kill all", () =>
    {
        int killed = 0;
        foreach (Enemy e in M.Enemies.AliveList.ToList())
        {
            if (!IsInstanceValid(e) || !e.IsAlive) continue;
            e.TakeDamage(new DamageInfo { Amount = e.Health + 1_000_000f, CanCrit = false, ProcCoefficient = 0f, Source = "dev" });
            if (e.IsAlive) e.Despawn();
            else killed++;
        }
        return $"Killed {killed} enemies.";
    });

    // ================================================================= stage

    private Control BuildStageTab()
    {
        var (scroll, page) = DevUi.Scroll();
        StageDirector sd = M.Stages;

        Label phase = DevUi.Lbl("", 15, DevUi.Accent);
        page.AddChild(DevUi.Row(DevUi.Heading("PHASE"), phase));
        var phases = DevUi.Flow();
        for (int i = 1; i <= sd.PhaseCount; i++)
        {
            int n = i;
            phases.AddChild(Reg($"phase_{n}", DevUi.Btn(Loc.F("Phase {0}: {1}", n, sd.Route[n - 1].DisplayName), () => Act($"Phase {n}", () =>
            {
                M.Stages.DevJumpToPhase(n);
                return $"Jumped to phase {n} ({M.Run.StageName}).";
            }))));
        }
        page.AddChild(phases);
        page.AddChild(DevUi.Flow(
            Reg("boss_now", DevUi.Btn("Spawn the phase boss now", () => Act("Boss now", () =>
                M.Stages.DevBossNow() ? $"The boss is coming: {M.Stages.BossName}." : null), DevUi.Bad)),
            Reg("portal_now", DevUi.Btn("Open the portal now", () => Act("Portal", () =>
                M.Stages.DevOpenPortal() ? (M.Stages.Won ? "Last boss counted as beaten: the run is won." : "Portal open at the map centre.") : null))),
            Reg("final_swarm", DevUi.Btn("Start the Final Swarm", () => Act("Final Swarm", () =>
                M.Stages.DevStartFinalSwarm() ? "The Final Swarm begins." : null), DevUi.Bad))));

        page.AddChild(DevUi.Heading("MAP & ROUTE"));
        var maps = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(260f, 30f) };
        foreach (StageDef s in Stages.All) maps.AddItem(Loc.T(s.DisplayName));
        var routes = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(260f, 30f) };
        foreach (RouteDef r in Routes.All) routes.AddItem(Loc.T(r.Name));
        page.AddChild(DevUi.Flow(
            Reg("map_pick", maps),
            Reg("set_map", DevUi.Btn("Play this map now", () => Act("Map", () =>
            {
                StageDef map = Stages.All[Mathf.Clamp(maps.Selected, 0, Stages.All.Count - 1)];
                M.Stages.DevSetMap(map);
                return $"This phase is now on {map.DisplayName}.";
            }))),
            Reg("route_pick", routes),
            Reg("set_route", DevUi.Btn("Switch to this route", () => Act("Route", () =>
            {
                RouteDef r = Routes.All[Mathf.Clamp(routes.Selected, 0, Routes.All.Count - 1)];
                var stages = Routes.Build(r, M.Run.Seed);
                M.Stages.DevSetRoute(stages);
                return Loc.IsArabic
                    ? Loc.F("Route {0}: {1}.", r.Name, string.Join("  <  ", stages.Select(s => Loc.T(s.DisplayName))))
                    : $"Route {r.Name}: {string.Join(" > ", stages.Select(s => s.DisplayName))}.";
            })))));

        page.AddChild(DevUi.Heading("CHALLENGES (enemy effects apply now, player effects from the next run)"));
        var chal = DevUi.Flow();
        foreach (ChallengeDef c in Challenges.All)
        {
            ChallengeDef def = c;
            CheckButton t = Reg($"challenge_{def.Id}", DevUi.Toggle(def.Name, Challenges.IsActive(def.Id), on => Act($"Challenge {def.Name}", () =>
            {
                if (Challenges.IsActive(def.Id) != on) Challenges.DevToggleLive(def);
                Profile.Save();
                return $"{def.Name} {(on ? "on" : "off")}: {def.Blurb}";
            })));
            t.TooltipText = def.Blurb;
            chal.AddChild(t);
        }
        page.AddChild(chal);

        Label tier = DevUi.Lbl("", 15, DevUi.Accent);
        page.AddChild(DevUi.Row(DevUi.Heading("DIFFICULTY"), tier));
        var tiers = DevUi.Flow();
        foreach (DifficultyTier t in DifficultyTiers.All)
        {
            DifficultyTier def = t;
            tiers.AddChild(Reg($"tier_{def.Id}", DevUi.Btn(def.Name, () => Act($"Tier {def.Name}", () =>
            {
                DifficultyTiers.DevSetActive(def);
                return $"This run is now on {def.Name} ({def.Summary}).";
            }))));
        }
        page.AddChild(tiers);

        Label speed = DevUi.Lbl("", 15, DevUi.Accent);
        page.AddChild(DevUi.Row(DevUi.Heading("TIME SCALE"), speed));
        var scales = DevUi.Flow();
        foreach (var (id, label, s) in new[] { ("timescale_025", "x0.25", 0.25), ("timescale_05", "x0.5", 0.5), ("timescale_1", "x1", 1.0),
                                               ("timescale_2", "x2", 2.0), ("timescale_4", "x4", 4.0) })
        {
            double v = s;
            scales.AddChild(Reg(id, DevUi.Btn(label, () => Act("Time scale", () =>
            {
                Engine.TimeScale = v;
                return $"Time runs at {label}.";
            }), minWidth: 60f)));
        }
        page.AddChild(scales);

        page.AddChild(DevUi.Heading("END THE RUN"));
        page.AddChild(DevUi.Flow(
            Reg("win", DevUi.Btn("WIN THE RUN NOW", () =>
            {
                if (Act("Win", () => { M.Stages.DevWin(); return "Run won: results next."; })) Close();
            }, DevUi.Good)),
            Reg("die", DevUi.Btn("DIE NOW", () =>
            {
                if (Act("Die", () => { M.Player.Health.Kill(); return "You died."; })) Close();
            }, DevUi.Bad))));

        OnRefresh(() =>
        {
            if (Main.Instance is not { } m || !IsInstanceValid(m)) return;
            StageDirector d = m.Stages;
            string boss = d.State == StageDirector.PhaseState.Waves ? Loc.F("boss in {0}", RunState.Format(d.SecondsToBoss)) : Loc.T(d.State.ToString());
            phase.Text = Loc.F("{0}/{1}  {2}  ·  {3}  ·  route {4}", m.Run.Phase, m.Run.PhaseCount, m.Run.StageName, boss, Routes.Active?.Name ?? "");
            tier.Text = Loc.F("{0}  ·  challenges: {1}", DifficultyTiers.Active.Name, ChallengeNames());
            speed.Text = $"x{Engine.TimeScale:0.##}";
        });
        return scroll;
    }
}

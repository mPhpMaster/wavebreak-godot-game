# Wavebreak — architecture

A 3D survivors-like in Godot 4.7 (.NET / C#). Original code and content.

The point of this document is the *why*. Anyone can read the files to see what
the code does; what is worth writing down is which decisions are load-bearing, so
that later changes do not quietly undo them.

## The four decisions everything else rests on

### 1. Stats go through one pipeline, never a field

`Stats/StatSheet.cs`

```
final = Shape( (base + flat) * (1 + Σ additive) * Π multiplicative )
```

Nothing writes `player.damage += 5`. Everything registers a `StatModifier`
tagged with **who supplied it**:

```csharp
stats.Add(Stat.Damage, ModifierOp.Additive, 0.15f, source: itemDef);
...
stats.RemoveAllFrom(itemDef);   // exact, and cannot drift
```

Why the source tag matters: the alternative is remembering the value you added
and subtracting it later. That is correct exactly once — the moment anything
recalculates in between, the subtraction removes the wrong amount and leaves
permanent residue on the stat. Tagging makes removal a filter instead of
arithmetic, so it is always exact.

Why additive modifiers **sum before applying**: ten +10% items give +100%, not
1.1¹⁰ = ×2.59. Players can add percentages in their head; they cannot compound
them. Reserve `Multiplicative` for rare effects that are *meant* to be explosive.

Adding a new stat costs one enum member and one line in `BaseValue`. Nothing
switches on the enum, because modifiers live in a table keyed by it.

### 2. Damage is an object, not a float

`Combat/DamageInfo.cs`

One mutable record travels the whole pipeline: the weapon sets the base, the
attacker's stats roll the crit, the victim's armour reduces it, on-hit effects
add to it, and the UI reads the final value *and the reasons*.

Passing a `float` down the call chain forces every stage into a fixed order at
the call site. Then "shots that cannot crit" or "double damage below half health"
becomes a new parameter on every method between the weapon and the victim.
`DamageInfo` makes those additions local.

`Nullified` is part of this: a dodge is not damage 0, it is "this hit did not
happen", and downstream effects (lifesteal, thorns, on-hit procs) need to tell
the difference.

### 3. Systems talk through a bus that can be reset

`Core/EventBus.cs`, `Core/GameEvents.cs`

Typed publish/subscribe over structs, so publishing does not allocate in the
frame loop.

The reason it is a bus and not a set of `static Action` fields: on run restart,
handlers belonging to freed nodes stay subscribed and fire against dead objects,
and the crash surfaces somewhere unrelated to the cause. `EventBus.Reset()` wipes
every subscription in one call at the end of a run, which turns that whole class
of bug from "rare and confusing" into "impossible".

### 4. Content is data, not classes

`Data/Definitions.cs` — `WeaponDef`, `EnemyDef`, `ItemDef` are Godot
`Resource`s, the ScriptableObject equivalent: inspector-editable, saveable as
`.tres`, hot-reloadable.

Most items need **no code at all** — an item is a list of `ItemEffect`
(stat, op, per-stack value). Most weapons need no code either: `AttackShape`
selects a firing strategy the runtime already implements.

This is what lets the game carry dozens of weapons and items without dozens of
classes, and lets someone tune them without touching C#.

## Balance knobs worth knowing about

| Where | Knob | Effect |
|---|---|---|
| `StatSheet.ArmorHalfPoint` | 30 | armour points for 50% reduction; the whole curve moves with it |
| `StatSheet.MaxReduction` | 0.9 | there is always a way to die |
| `StatSheet.EvasionHalfPoint` | 40 | same shape, separate curve |
| `EnemyDef.Cost` | per enemy | how the director's budget converts into threat — swarm vs. squad |
| `EnemyDef.UnlockAtSeconds` | per enemy | paces which threats appear when |
| `RunState.TargetSeconds` | 900 | nominal run length; the stage table is tuned to land near it |
| `StageDef.PhaseSeconds` | 210 | waves per map before its boss arrives; 3 maps x (3:30 + ~2 min boss) ~ 15 min |
| `StageDef.Radius` | 130-165 m | maps are 2-3x the old 60 m disc; spawns stay on a ring around the player, so density near the player is unchanged |
| `StageDef.Enemy*Scale` | 1 / 1.25 / 1.55 hp | per-map step on top of the clock curve; phase 1 is exactly 1 so the opening is untouched |
| `StageDef.BossHealthScale` | 0.55 (0.45 last) | phase bosses are mandatory gates and weapons aim at the nearest enemy, so they carry less than the full time-curve health |
| `StageDef.BossGraceSeconds` | 120 | boss lifetime before overtime adds +12% enemy hp/damage per 30 s |
| `DifficultyTiers.All` | Normal 1 / Hard 1.45 / Nightmare 2.1 hp | tier multipliers on enemy hp, damage, spawn rate, crowd growth and Pearls; Normal is exactly 1 |
| `FinalSwarm` consts | per 30 s: hp +15% x1.10 compound, dmg +25% x1.09 compound, +0.35 spawn rate | the endless phase after the last boss; compounding so even an 86%-armour build meets a wall |
| `Enemy.Configure` speed cap | 5.2 m/s (5.6 elites/bosses) | nothing walks faster than the player's base 6 m/s once the clock and modifiers stack; dashes and leaps are exempt |
| `RunRewards` consts | 1/20 kills, 10/phase, 15/boss, 60 win | Pearls per run, times the tier's reward |
| `MetaShop.CostOf` | base x (n+1)^1.5 | shop prices; refund returns the full amount paid |

`HealthRegen` is stored **per minute** because that is the readable unit on a
tooltip; `StatSheet.RegenPerSecond()` is the only place that conversion happens.
Getting this wrong in either direction is a 60× balance error, which is exactly
the kind of mistake that hides for a long time.

## Running it

```bash
D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe --path .
```

Build the C# first (Godot will also do it on open):

```bash
dotnet build
```

Self-verifying capture — runs the real game for N seconds, saves a frame, exits:

```bash
Godot_v4.7.1-stable_mono_win64_console.exe --path . --resolution 1280x720 -- --shot=out.png --shot-after=6 --seed=42
```

Seeded runs are reproducible (`Core/Rng.cs`), which is what makes a balance bug
re-testable and lets captures be compared across changes.

Useful test flags (all after `--`):

| Flag | What it does |
|---|---|
| `--auto --invulnerable --seed=N` | autopilot run that cannot die |
| `--phase-seconds=N` / `--start-phase=N` | shorter phases / start on a later map |
| `--give=id,id` | start holding these weapons and tomes at max; `all_items` adds one of every item |
| `--perf` | every 5 s: FPS, CPU/GPU render ms, draw calls, nodes, live enemies, per-system ms (`Game/Prof`), and a node census every 30 s |
| `--levelup-shot=x.png`, `--menu-shot=x.png --menu-page=<page>`, `--panel-shot=<prefix>` | capture screens |
| `--validate-content`, `--check-pause=<dir>`, `--check-chests=<dir>`, `--check-boundary`, `--check-input-guards`, `--check-devpanel`, `--check-graphics`, `--check-walk` | self-checks printing PASS/FAIL |
| `--check-restroom`, `--restroom-shot=x.png`, `--check-phases`, `--check-portal-timer`, `--portal-seconds=N`, `--no-rest-room` | rest room, run length and portal countdown (see "Rest room between maps") |
| `--coop-host=PORT` / `--coop-join=IP:PORT` with `--auto` | automated co-op session over ENet (direct IP), printing `COOP PASS/FAIL` (see "Online co-op") |
| `--compact-levelup`, `--autopick-rule=<id>` | Settings > Gameplay for this run only (compact hand; auto-pick by rule) |
| `--steam-selftest` | Steam transport over a fake in-memory Steam: frames, handshake, relay, ordering, refusal, timeouts. Prints `STEAM SELFTEST PASS/FAIL` |
| `--steam-test` | let an automated launch (`--menu-shot`) start Steam, for live checks. Without it, automated runs never touch Steam |
| `--connect-lobby=<id>` | join this Steam lobby at start, like Steam's own `+connect_lobby <id>` |

`tests/smoke.ps1` runs three seeded 30-second runs; `tests/coop.ps1` runs the
two-process co-op test. Automated launches run
through `D:\tools\megabonk\run-offscreen.ps1`, which keeps the window on a hidden
desktop.

## Building the Windows release and installer

```bash
Godot_v4.7.1-stable_mono_win64_console.exe --headless --path . --export-release "Windows Desktop" build/windows/Wavebreak.exe
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/Wavebreak.iss
```

`export_presets.cfg` uses the custom .NET templates under
`D:/tools/godot/export_templates/4.7.1.stable.mono/` and writes the icon
(`icon.ico`, made by `installer/make_icon.py` from `icon.svg`) and version
resources into the exe. The installer (`installer/Wavebreak.iss`) installs per
user by default, adds Start-menu and optional desktop shortcuts, speaks English
or Arabic, and writes `build/installer/Wavebreak-Setup-<version>.exe`.
`build/` is git-ignored.

Steam files: the editor plugin `addons/wavebreak_steam` (enabled in
`project.godot`, C#, `#if TOOLS`) copies `thirdparty/steamworks/win64/steam_api64.dll`
next to the exported exe and writes `steam_appid.txt` from `SteamApp.AppId`
at the end of every Windows export; the export log shows
`[steam export] ... -> build/windows`. The installer lists both files by
name, so a build without them fails to compile instead of shipping a game
that cannot start Steam. The NuGet package (Steamworks.NET 2024.8.0, MIT)
carries only the managed wrapper; the DLL in `thirdparty/` is Valve's
redistributable for SDK 1.60, the version that wrapper is built against
(checked by matching its 952 imports to the DLL's exports).

## Physics and performance

The project runs on **Jolt Physics** (`3d/physics_engine` in `project.godot`).
Godot's own solver spent about 1 ms per enemy in `MoveAndSlide` against the
convex map hulls and held a mid-run crowd at 7 FPS; Jolt runs the same scene at
150+ FPS. On top of that, `Enemy.CoastThisTick` lets a grounded, self-driven,
non-boss enemy skip its collision move on alternate ticks past 16 m and two in
three past 30 m (staggered by instance), stepping straight along its velocity
instead; the next real slide resolves any wall it touched. That cut the enemy
step about 3x. Measure with `--perf`.

## Visuals: imported first, procedural always there

`Visuals/` owns how things look; gameplay code never builds a mesh.

- **Imported models are the primary look** (CC0 Kenney and Quaternius packs,
  `assets/CREDITS.md`), wrapped as a `ModelSpec` with a clip map
  (`ArtAssets`). **Procedural models are the fallback** (`ModelBuilder`): any
  enemy without a mapping, or whose file is missing, still gets a body built
  from its numbers. A new `EnemyDef` therefore never renders as nothing.
- **One `CharacterRig` per character** is both model and animator. It plays
  clips for imported models and poses parts for procedural ones, and layers the
  same procedural squash, lean, flip and death on top of both. Gameplay talks
  to it through four hooks: `PlayAttackWindup`, `PlayAttack`, `PlayHit`,
  `PlayDeath` (enemies: `EnemyVisuals.SpawnCorpse`, because the pooled node
  vanishes the instant it dies).
- **Cost is per kind, not per instance.** Procedural meshes are built once per
  spec and share one material (colour in vertices, flash/dissolve as instance
  uniforms); imported clips advance at a reduced rate far from the camera; far
  procedural enemies swap to a merged mesh via engine visibility ranges; props
  go through MultiMesh. Measure with `--art-stress=N` against `--art-off`.
- **Biomes are data** (`PropTheme`), consumed by `PropFactory` (floor shader,
  scatter, sky, sun). A theme can mix kit and procedural props per kind.

## Layout

```
scripts/
  Core/        EventBus, GameEvents, Rng, Pool, Shrine/Stage events
  Stats/       Stat, StatSheet          <- the pipeline
  Combat/      DamageInfo, WeaponSystem, WeaponRuntime, Projectile, StatusEffects,
               EnemyAttacks, Weapons/ (behaviours, item procs, effect meshes)
  Actors/      Player, PlayerHealth, PlayerCamera, Enemy, EnemyManager, Pickup,
               Chest, Merchant, Microwave, Campfire, MapShrine, CuteShrine, Station
  Audio/       AudioDirector, SfxSynth, MusicDirector, MusicTracks, Boombox, AudioSettings
  Progression/ XpCurve, Inventory, OfferPool, TomeBook, LevelUpChoices
  UI/          Hud, LevelUpPanel, PauseMenu, StatsPanel, ResultsScreen, BossBar, MainMenu
               pages (Codex, MetaShop, Boombox, Graphics, CoopPage), CoopHud, DevPanel, CuteStyle, Icons,
               AwarenessLayer (Minimap, TeamTags, PingMarkers, AwarenessAlerts, ScoreboardPanel)
  Data/        Definitions, Catalog*, Stages*, Tomes*, MapShrines, ContentAudit*  <- all content
  Game/        Main, Arena, StageDirector, FinalSwarm, Portal, RunState, spawners,
               Autopilot, Screenshot, PerfProbe/Prof
  Meta/        Profile, Unlocks, Quests, Characters, Hats, Pearls, MetaShop, Difficulty,
               Routes, Challenges, Leaderboard, Launch, Loc + ArabicText/, Graphics/Game settings
  Net/         Coop (session facts), NetSession (+ .Run, .World, .Test, .Awareness, .Pings),
               RemotePlayer, DownFlare, Pings
    Steam/     SteamApp (start-up, App ID), SteamLobby, SteamPeer (+ SteamFrame),
               SteamLink (ISteamLink, fake hub), SteamMessagesLink, SteamPump, SteamSelfTest
  Visuals/     ModelSpec, ModelBuilder, CharacterRig, PlayerLooks, EnemyVisuals,
               PropTheme, PropFactory, ToonMaterials, ArtAssets, showcase/stress
assets/        imported CC0 models, icons, music, fonts (see assets/CREDITS.md)
tools/         compose_music.py and other generators
installer/     Inno Setup script, icon generator
addons/        wavebreak_steam: export plugin for the Steam DLL and steam_appid.txt
thirdparty/    steamworks/win64/steam_api64.dll (Valve redistributable, .gdignore'd)
tests/         smoke.ps1, coop.ps1, run-offscreen.ps1
docs/          screenshots used by README.md
```

The project boots into `scenes/Title.tscn` (`UI/MainMenu`). Any game argument
other than the menu's own (`--profile`, `--menu-shot`, `--menu-page`) skips it
straight into the run, so every automated launch behaves as before. Automated
runs never touch the real profile unless `--profile=<path>` is given. Unlocks
only *add* content (`Meta/MetaContent`); the base catalogue is never locked.

Art checks: `-- --art-showcase=hero|enemies|closeup|props-<theme> --shot=x.png`
stages a scene; `--art-theme=<theme>` plays a run in a biome;
`--art-stress=150` (with `--art-off` / `--art-procedural` for A/B) prints a
`STRESS` line with wall-clock frame times.

## Meta currency, shop, difficulty, Final Swarm

Pearls (`Meta/Pearls`) are paid once per run by `Meta/RunRewards` (on RunEnded,
when the results screen asks, or when the run scene is left early) and spent in
the title screen's Pearl Shop (`UI/MetaShopPage`, `Meta/MetaShop`), whose levels
go onto the player's StatSheet tagged `MetaShop.Source`. Reroll/banish/skip
charges are exposed as `MetaShop.BonusRerolls/BonusBanishes/BonusSkips` for the
level-up code to read. Difficulty tiers (`Meta/DifficultyTiers`) are picked on
the character page; `Active` is read by `EnemyManager` every tick. All of it
lives in the profile's `[meta]` section (`Profile.GetMeta/SetMeta`).

The last boss's death is the win (`StageDirector.Won`, `FinalBossDefeated`):
it is recorded at once, and then `Game/FinalSwarm` escalates until the player
dies; that death still publishes `RunEnded(survived: true)`. The pause menu's
END RUN (`PauseMenu.EndRun`) finishes the run on the spot: a win after the last
boss, a forfeit before it.
`--no-final-swarm` restores the instant win; `--difficulty=<id>` forces a tier.

## Rest room between maps, run length, portal countdown

`Game/RestRoom` (+ `StageDirector.RestRoom.cs`, `Actors/Campfire`,
`UI/RestRoomHud`, `Autopilot.RestRoom.cs`, `Net/NetSession.RestRoom.cs`).

- **Flow:** boss down -> portal -> **rest room** -> CONTINUE portal -> next
  map. The last map has no portal (its boss is the win, then the Final
  Swarm), so there is a rest room between every pair of maps and none after
  the last. `--no-rest-room` restores the direct portal step.
- **The room** replaces the map node at the same origin: a sand island with a
  beach hut, palms, a boardwalk, the merchant's stall, a campfire and a
  microwave, built from primitives, kit props and cube pets. `Arena.UseBounds`
  points the player's polygon clamp at `RestRoom.Bounds` while it stands.
- **Merchant:** the maps place none any more (`StageDef.Merchants = 0`, the
  content audit checks it). The room's stall is the same `Merchant` station
  run by `Interactables` (`UseRestRoom`): same stock rules, `TryBuy`, panel
  and run totals, so the merchant quests (Good Custom, Shopping Spree, ...)
  count as before. Prices are those of the map just cleared.
- **Campfire:** interact to rest, health back to full. Never runs out.
- **Clock: paused.** While `StageDirector.InRestRoom`, `Main` skips
  `RunState.Tick` and the enemy director; the field is cleared on arrival.
  Shopping costs no difficulty-curve time; `RestClockIn/Out` record it.
- **Leaving.** Single-player: step into the CONTINUE portal. Co-op: the host
  moves on when every connected player stands in it ("In the portal 1/2" on
  every HUD); once anyone is in, a 30 s countdown (`RestExitTimeout`) starts,
  so one idle player cannot hold the team. A player who arrives downed is
  revived at full health.
- **Run length per tier:** `DifficultyTier.Phases` (Normal 3, Hard 3,
  Nightmare 4, Tempest 4, Abyss 5, Leviathan 5, Endless Tide 5). `Routes`
  builds that many maps without repeats: Classic uses `Stages.LongOrder`
  (meadow, shallows, plateau, graveyard, Cinder Reach last); a fixed route
  gets the unused maps slotted in before its last map; a draw draws more.
  `Stages.ForPhase(map, i, count)` keeps stepping enemy numbers past phase 3
  (+25% hp, +0.13 dmg, +0.03 speed per phase); only the last map's boss gets
  the final share (0.45). Normal is exactly the classic route objects.
- **Boss portal countdown:** `DifficultyTier.PortalSeconds` (90 / 75 / 60 /
  50 / 40 / 35 / 30 s), shown on the HUD status line and over the portal.
  **At zero the portal pulls everyone through** (into the rest room, or the
  next map with the room off), wherever they stand. The map is already
  cleared, so the countdown is a hurry-up, not a fail state: overtime
  pressure exists to end stalled boss fights, and a portal that shut would
  only strand the run. Co-op: the host decides; clients count down locally
  and are corrected twice a second. `--portal-seconds=N` overrides it.
- **Checks:** `--restroom-shot=x.png` (arrival view), `--check-restroom
  --auto --invulnerable` (map 1 -> room -> map 2: no merchant on the maps, no
  enemies and a held clock in the room, the bot buys, rests and walks out on
  its own; `RESTROOM PASS`), `--check-phases [--difficulty=abyss]` (phase
  count per tier, distinct rising maps for every tier and route; `PHASES
  PASS`), `--check-portal-timer --portal-seconds=6 [--portal-shot=x.png]`
  (countdown runs and pulls the player through; `PORTALTIMER PASS`).
  `tests/coop.ps1` checks that both players pass through the room together.

## Elements, statuses, megacrits, weapon upgrade rarities

- `Combat/StatusEffects` decides what a landed weapon hit applies (Fire burn,
  Ice chill then freeze, Lightning shock arc plus stun, Poison stacks); the
  state and tint live on the enemy (`Actors/Enemy.Status.cs`: `ApplyBurn`,
  `ApplyPoison`, `ApplyChill`, `ApplyStun`). Item procs are plain stats
  (`IgniteChance`/`ChillChance`/`ShockChance`/`PoisonChance`, `StatusChance`,
  `StatusDuration`, `ElementalDamage`, `FreezePower`), so element items need no
  code (`Data/Catalog.Elements.cs`). Ticks and arcs carry proc coefficient 0.
  Bosses take 30% freeze/stun time and get a short crowd-control immunity.
- A crit rolls `MegacritChance` to become a megacrit (x3 on top, magenta number).
- Weapon levels are rolled upgrades (`Combat/WeaponUpgrade.cs`): a Luck-weighted
  rarity and 1-3 stat lines, rolled when the card is built so the card shows
  exactly what it gives; `WeaponRuntime.Bonus` holds the sum. `DamagePerLevel`,
  `CooldownPerLevel` and `ProjectilesEveryNLevels` now only size the damage and
  speed lines and gate projectile lines (0 = never).

`Game/Main.cs` constructs the systems and ticks them **in one place** rather than
letting each run its own `_Process`. That ordering is deliberate: with
independent callbacks, whether weapons fire before or after enemies move depends
on scene-tree order, so hit registration changes when you reparent a node.

## Routes, extra maps and challenges

`Data/Stages.Shores.cs` adds Gravemoor (graveyard, pentagon, sunken graves that
slow and gnaw) and Saltglass Shallows (tidal, 12 sides, tide pools that slow).
The pools are `Game/MapHazards`, placed last so maps without hazards keep their
layout. `Meta/Routes` picks the run's 3 maps: Classic (= `Stages.Run`), fixed
routes, seeded draws, and "start on X". A non-classic map is copied with the
classic numbers for its phase (`Stages.ForPhase`), so a route changes the ground,
not the difficulty curve. `Meta/Challenges` and `ChallengeRuntime` are stackable
run modifiers; every multiplier is exactly 1 when none is active. Automated runs
use Classic and no challenges unless `--route=<id|a,b,c>` or
`--challenges=<ids|all>` is given. `--check-boundary` walks `Stages.All`.
`StageDef.MusicId` and `EnemyIds` are hooks for music and theme enemies
(empty = default).

## Quest book, skin recolouring, codex tabs

- `Meta/Quests.Mastery.cs` (`MasteryQuests`) is joined last onto `Unlocks.All`
  (~280 rows in all). Per-weapon (kills, max level) and per-tome rows are
  generated from the catalogue, so new content gets quests for free. New
  metrics are appended to `Metric`; their tracking is
  `Meta/UnlockTracker.Mastery.cs`. Status applications are counted in
  `Combat/StatusTally` (static ints, reset per run); merchant, microwave, pot
  and shrine numbers are copied from `Interactables`/`ShrineSpawner` on the
  0.2 s poll. `UnlockDef.StatKey` caches each row's profile key so the poll
  does not build strings. `RewardKind.Pearls` rows credit `Pearls.Earn` when
  earned and carry a unique `pearls.<id>` reward id, so `Unlocks.SourceOf`
  (now a dictionary) never mistakes them for content gates.
- Skins repaint the model (`Visuals/SkinRecolor`, `SkinDef.Paint`): HSV
  adjust plus a brightness-keeping dye, with skin tones protected. Procedural
  rigs read it from two instance uniforms on `ToonMaterials.Character`;
  imported Kenney rigs get a surface-override stand-in material
  (`ToonMaterials.Recolored`, one per source material, cached) with the same
  texture and the same instance uniforms (`CharacterRig.SetRecolor`). The
  menu preview and the run both apply it. Base skins are the identity.
- `UI/Codex` builds every card once and filters by tab (All / Quests /
  Secrets / Characters / Challenges / Shrines & Tomes) by toggling visibility;
  Q/E, Page Up/Down or the shoulder buttons switch tabs. `ContentAudit.Quests`
  checks ids, rewards, keys and the six-skins-per-character floor.

## Map challenges, Toggler, slots, cute shrines

- `Meta/MapChallenges` holds 55 per-map challenges (11 per map): a goal
  (reach the boss, beat it, or win and last 60 s of the Final Swarm), global
  modifiers, a map twist (night, whiteout, high tide, lava floor ...) applied
  to a reflection copy of that map, and a pressure tier. One is selected at a
  time; it forces the route `start:<map>`, merges into `Challenges.BeginRun`,
  and `MapChallengeRuntime` pays Pearls plus a hat or skin once (`mchal_<id>`).
  `--map-challenge=<id>` forces one in automated runs.
- `Meta/ShopExtras` appends three Pearl Shop lines (Toggler, Weapon Rack,
  Bookshelf) and is the one run hook (`RunMeta.Attach`). Weapons now have
  `WeaponSystem.Slots` (6 base); the HUD shows `WEAPONS n/max` (`Hud.Slots`).
- `Meta/Toggler` filters `MetaContent.OfferItems/OfferWeapons` and
  `OfferPool.DisabledTomes`, keeping 10 weapons / 20 items / 8 tomes on.
- `Game/CuteShrineSpawner` places the Shrine of Balance and Wishing Well
  (`StageDef.BalanceShrines/WishingWells`), separate from `ShrineSpawner`, so
  the autopilot never uses them. `--cute-shrine-shot=<png>` photographs them.
- `UI/CuteStyle` is the soft pastel look used by these pages.
## Online co-op (2-4 players)

`scripts/Net/` plus small `*.Coop.cs` partials next to the code they extend
(`Enemy.Coop`, `EnemyManager.Coop`, `Pickup.Coop`, `Player.Coop`,
`PlayerHealth.Coop`, `ChestSpawner.Coop`, `StageDirector.Coop`). It is built on
Godot's high-level multiplayer: one `MultiplayerPeer` and RPCs on a single
node, `/root/NetSession`, which has the same path on every peer and survives
scene changes. Players find each other through **Steam lobbies** and talk
through Valve's relay (`SteamPeer`, see "Steam lobbies and transport");
`ENetMultiplayerPeer` (direct IP) remains as the developer and test path.
Only the peer differs: every RPC, the roster and the run sync are the same
over both. The design follows what the Megabonk mod *Bonk With Friends* chose
to sync (lobby, players, enemies, damage, XP, pickups, chests, run start and
end) and how, including its Steam lobby flow. None of its code is used.

**Single-player has no network code path.** A `NetSession` exists only while a
session does. Every hook in the game is behind `Net.Coop.Active`, `IsHost`,
`IsClient` or `InRun`, and all of these are false without one. The one hook
that runs in single-player is `Coop.SetPaused`, which is then exactly
`tree.Paused = x`. The self-checks and the smoke test pass unchanged.

### Who owns what

The split is by ownership, chosen as the simplest approach that stays
responsive and cannot desync where it matters:

| Thing | Owner | Others see |
|---|---|---|
| A player's body, movement, health, weapons, level-ups, inventory, tomes | that player's machine | `RemotePlayer` puppets from 20 Hz unreliable state: position, facing, velocity, pose, health, down flag, pickup range |
| Enemies (director, spawns, AI, health, death) | host | `Enemy` puppets: spawn and death are reliable events; position, facing and health come in 15 Hz unreliable snapshots, with smoothing and a short look-ahead |
| Damage *to* enemies | host applies it | Clients' weapons fire locally at puppets. A hit is predicted (number, flash, health bar), batched and sent to the host, which applies it to the real enemy. Crowd control (stun, chill/freeze) is forwarded the same way. Burn and poison tick on the client's puppet and forward each tick |
| Damage *to* players | the victim | The host's contact swings at a remote player become a `Hurt` RPC. The owner applies it through their own i-frames, dodge, armour and shield. Enemy shots, blasts, shockwaves and flashes are mirrored, and each machine tests them against its own player |
| Drops (XP, gold, med-packs) | host | The host rolls drops and decides who collects each orb: the first player whose magnet range it enters, or the nearest living player if that one goes down or leaves. Clients animate the magnet and wait for the host's word |
| Chests | host places; first to ask gets it | `E` sends a request, and the host grants it to the first asker. The winner pays from their own gold and price ladder and rolls from their own pool. Everyone else sees it open with the winner's name |
| Phase flow (waves → boss → portal → next map, win, Final Swarm) | host | Reliable `Phase`, `Boss` and `BossDown` messages replay the steps. A client's `StageDirector.Tick` only guards falls. The run clock is synced twice a second |
| Rest room (entering, leaving, the CONTINUE count, the boss portal's countdown) | host | Reliable `RestEnter` when the host takes the boss portal; leaving is the usual `Phase` message. `RestStatus` (in the portal / total / countdown) and `PortalTimer` stream at 4 and 2 Hz, unreliable. Each machine builds its own room, revives and heals its own player, and shops with its own gold |
| Shrines, merchant, microwave, pots, secrets | each player | Each player has their own set, built from the shared seed, and their own rewards. Pot drops are personal (`Pickup.Personal`) |

Why clients fire their own weapons instead of sending input to the host:
the weapon code depends on the player's whole build (stat sheet, item procs,
character passive, tomes, upgrade rolls). Running a second copy of every
client's build on the host would double the state that has to agree. Moving
the *result* (a hit on enemy N for X) keeps the host authoritative over the
only thing that matters to everyone, the enemy's health, while each player's
shots feel instant. This is a co-op game, so a cheating client is out of
scope.

Messages are ordered by sending them all on the reliable channel: enemy and
pickup events are batched per frame into one `Ev` float array (typed
fixed-length records), and every other reliable RPC flushes that batch
first. So a boss's `Boss` message never arrives before the boss's spawn, and
`Phase` never arrives after the new map's chests.

### Decisions the brief left open

- **Level-ups and menus do not pause.** The world keeps running in co-op.
  `Coop.SetPaused` replaces `tree.Paused` in the pause menu and the shrine
  and modal panels. In co-op it sets `Coop.LocalHold`, which freezes only
  the local player's movement and interact keys, and shields them for at
  most 8 s per opening (`Coop.MenuGraceSeconds`). Pausing everyone for each
  player's level-up would stop the game every few seconds with four players;
  freezing the others' input would be worse.
- **Level-ups do not even hold you.** In a co-op run the level-up panel is
  the compact hand (`UI/LevelUpPanel.Compact.cs`): small cards docked at the
  bottom, no dimmer, no pause, no `LocalHold`, so the player keeps moving
  while choosing. Single-player keeps the full pausing panel unless Settings >
  Gameplay > Compact level-up is on. Input rules that keep picking out of the
  way of playing: 1-4 and the reroll/banish/skip keys are taken only when not
  bound to a movement action; no card ever takes focus (Space and the arrows
  stay the body's); the root ignores the mouse, a click that misses a card
  does not re-capture the mouse, and right-click stays dash; while another
  menu holds the game the hand waits. `LevelUpPanel.PausesGame` is what the
  pause menu and shrine panel ask now, instead of `IsOpen`.
- **Lobby.** The host's difficulty (any of `DifficultyTiers.All`, only tiers
  unlocked on the host's profile) is `Coop.DifficultyId`, sent as a `@|<id>`
  header line on every roster broadcast, shown read-only to clients, and
  used by `StartGame`, so `DifficultyTiers.BeginRun` starts everyone on it.
  `CoopPeer.HatId` rides in `Hello` and the roster (protocol
  `wavebreak-coop-2`), so the lobby line-up and the in-run puppets wear it.
  `UI/CoopLineup` stands every roster player side by side on the title beach
  (rebuilt per player only when the look changes, polled 5x a second) with a
  left-to-right label layer on the CO-OP page for names and READY. The
  page's left column reuses the PLAY page's panel text (`UI/CharacterInfo`).
- **XP is shared.** The mod mirrors every XP gain to every player, and so
  does this: every XP orb anyone collects is granted to every player
  (`ShareXp`), and each applies their own XP-gain stat. Everyone levels
  together, so nobody falls behind for playing support, and each still picks
  their own cards. Gold and med-packs are per player.
- **Down, not dead.** At 0 health in co-op, `PlayerHealth` goes down instead
  of ending the run. The downed machine counts its own revive: a living
  teammate within 2.6 m for 4 s (progress drains slowly when they step away)
  brings it back with 40% health and 2 s of immunity. The host ends the run
  when every connected player is down.
- **Enemies chase the nearest living player.** The host's `EnemyManager`
  picks the nearest target per enemy. Spawn bursts ring each living player in
  turn, and strays are culled by distance to the nearest player.
- **Run settings:** co-op forces the Classic route and no challenges, so the
  host's seed builds the same maps everywhere. The host's difficulty tier is
  used by everyone.
- **Starting together.** Every machine loads the run scene and holds the
  tree paused until the host has heard "loaded" from everyone (30 s cap).
  Then one `Go` starts the clocks.
- **Leaving.** A client leaving removes their avatar, and the run goes on.
  Their orbs go to the next nearest player. The host leaving sends every
  client to the title's CO-OP page with "The host left the game", unless
  their run was already over, in which case the results screen stays.
  Joining a run in progress is refused.

### Steam lobbies and transport

`scripts/Net/Steam/`, on Steamworks.NET (NuGet, MIT). It follows the mod's
flow: lobby for finding people, a direct Steam P2P link to the lobby owner for
the game itself.

- **When Steam starts** (`SteamApp.TryInit`): only when the CO-OP page opens
  or its RETRY is pressed, or when the game was launched with
  `+connect_lobby <id>`. Never in single-player, never in an automated run
  unless `--steam-test` is given. If `SteamAPI.InitEx` fails (Steam not
  running, not signed in, DLL missing) the page shows "Steam must be running
  to play co-op" with RETRY, and the error goes to the log. A `SteamPump` node
  then runs callbacks every frame and shuts Steam down on quit.
- **App ID**: `SteamApp.AppId = 480`, Valve's public test app *Spacewar*.
  Anyone can use it without a store page, so friends see "Spacewar" in Steam.
  Shipping under the game's own name needs a Steamworks App ID; change that
  constant (the export writes `steam_appid.txt` from it) and nothing else.
  The App ID also goes into the `SteamAppId`/`SteamGameId` environment
  variables before init, so editor and test runs need no file.
- **DLL**: `SteamApp` installs a `DllImportResolver` for the Steamworks.NET
  assembly that loads `steam_api64.dll` from beside the exe (exported) or from
  `thirdparty/steamworks/win64` (editor and tests).
- **Lobby** (`SteamLobby`): HOST (Steam) creates a friends-only lobby with 4
  slots, sets `game=wavebreak` and `protocol=<Coop.Protocol>`, and sets rich
  presence `connect=+connect_lobby <id>` so Steam's "Join Game" works. INVITE
  FRIENDS calls `ActivateGameOverlayInviteDialog`. Joins arrive from
  `GameLobbyJoinRequested` (an invite accepted in Steam),
  `GameRichPresenceJoinRequested`, the friends list on the page
  (`GetFriendGamePlayed` with our App ID and a lobby whose data says
  `wavebreak`), or the command line. A join that arrives mid single-player
  run waits for the title (`PendingJoin`). After entering, the client checks
  the lobby's game and protocol and connects to `GetLobbyOwner`. Each member
  publishes its Godot peer id as lobby member data `peer`, so every machine
  can put a Steam avatar (`GetSmallFriendAvatar`) on every roster row.
- **Transport** (`SteamPeer`, a `MultiplayerPeerExtension`): the star ENet
  had, over **ISteamNetworkingMessages** (`SteamMessagesLink`). That is
  connectionless P2P addressed by Steam id, through Valve's relay, so there is
  no NAT trouble and no port to open. Not ISteamNetworkingSockets: in
  Steamworks.NET its P2P calls take `SteamNetworkingConfigValue_t`, whose
  overlapped union CoreCLR refuses to load, so every such call throws under
  .NET. Frames (`SteamFrame`) start with a kind byte:
  HELLO (with a magic) → WELCOME (the client's random Godot peer id) or REFUSE
  (full); DATA carries Godot's channel, transfer mode and a sequence number;
  PING keeps idle links alive; BYE leaves. Reliable packets use Steam
  reliable sends, which are ordered. For unreliable-ordered packets the
  receiver drops anything older than the newest it has seen on that channel.
  The host accepts a HELLO only from a lobby member, and drops a peer silent
  for 12 s. The client gives up after 25 s without a WELCOME. Server relay
  is on, so client-to-client RPCs and peer announcements work as over ENet.
- **Seam for tests** (`ISteamLink`): the peer only sends, receives and hears
  about failed sessions. `FakeSteamHub` implements that in memory, with held
  and reversed unreliable delivery and cut links. `--steam-selftest` runs 43
  checks: frame round trips and malformed frames, sequence wrap-around, the
  raw handshake, stale-packet dropping, broadcast targets, wrong-magic HELLOs.
  It also runs a host and four clients, each on its own `SceneMultiplayer` in
  one process, covering relay RPCs, a 200 KB message, a non-member ignored, a
  fifth player refused, leave, timeout and host leaving.

### Testing

`tests/coop.ps1` tests the game's co-op sync over ENet (the developer path;
`SteamPeer` slots in under the same RPCs). It launches two game processes on
localhost, through the hidden-desktop wrapper:

```
host:   -- --coop-host=7777 --coop-players=2 --auto --invulnerable --phase-seconds=22 --open-portal-at=36 --coop-expect-phase=2 ...
client: -- --coop-join=127.0.0.1:7777 --auto --invulnerable --phase-seconds=22 --coop-expect-phase=2 ...
```

The host picks Hard in the lobby (`--coop-difficulty=hard`); the client
checks it saw that tier before READY and that its run started on it
(`--coop-expect-difficulty`). At 12 s the client opens a real level-up hand
(`--coop-levelup-at`), takes the autopilot off the wheel and holds the real
`move_forward` action for 1.6 s: the hand must be the compact one, no input
hold may be set, the player must move at least 1 m, and a real 1 key must
take a card (`levelup_moved=`, `coop-levelup-client.png`).
Both bots play about 60 s. The host spawns three ranged enemies at 10 s to
test the mirrored shots. The client goes down on purpose at 20 s, and the
host's bot walks over and revives it. The boss arrives and the portal opens,
the host takes the team into the rest room (the client arrives downed on
purpose and must be revived there, `--coop-down-before-rest`), both bots shop,
rest and stand in the CONTINUE portal, and the host must leave with
everyone in it (`rest_exit=all`, `--coop-expect-restroom`); then both reach
map 2. Both bots huddle for a screenshot, the client leaves,
and the host checks the run survived that. Each side prints
`COOP PASS|FAIL role=... players=... enemies_synced=... hits=... ...` with the
reason on failure. A second scenario has the host quit at 15 s
(`--coop-quit-at`); the client must land on the CO-OP page with the message
(`--coop-expect-host-leave`). Screenshots go to `tests/shots/coop-*.png`.
`--coop-lobby-only` with `--menu-shot=x.png --menu-page=coop` photographs a
filled lobby; `tests/coop-lobby.ps1 [-Lang ar] [-Res 1920x1080]` runs a host
and a guest (`--coop-hat`, `--character`) to the READY state and checks both
captures show two players on the beach and the host's tier.

`--steam-selftest` checks the Steam transport without Steam (above).
`--menu-shot=x.png --menu-page=coop` (add `--lang=ar`) photographs the
"Steam must be running" state. Automated runs never start Steam, so this
works on any machine.

**Live Steam check** (two Steam accounts on two PCs, or a PC and a VM; one
Windows session runs one Steam client):

1. Both: start Steam, sign in, launch Wavebreak, open CO-OP. The status line
   must not say "Steam must be running", and the log shows
   `[steam] ready: app 480, user <name> ...`.
2. A: HOST (Steam). The status reads "Lobby open..." and the log shows
   `[steam] lobby <id> created`.
3. A: INVITE FRIENDS → pick B. If no overlay appears, use the Steam friends
   list: right-click B → Invite to Lobby.
4. B, with Wavebreak open on CO-OP: accept the invite in Steam. Or wait about
   2 s for A to appear under FRIENDS IN A GAME and press JOIN. B's log:
   `[steam] in lobby ...`, then `[steam] connected to host ... as peer <id>`.
5. Both rosters show two rows with avatars. B: READY. A: START. Play,
   go down and revive, then B leaves: A's run continues. Repeat with A
   leaving: B lands on CO-OP with "The host left the game".
6. B joining from a cold start: close Wavebreak on B and use **Join Game** on
   A in Steam. Under App ID 480 this starts Spacewar, not Wavebreak (see
   limits). Instead, test the command line Steam would use:
   `Wavebreak.exe +connect_lobby <id from A's log>`.

The log is `%APPDATA%\Godot\app_userdata\Wavebreak\logs\godot.log`.

`Wavebreak.exe -- --menu-shot=x.png --menu-page=coop --steam-test` captures
the page with Steam really started, for a one-machine sanity check.

### Team awareness: minimap, name tags, pings, boss toast, scoreboard

`UI/AwarenessLayer` installs it for every run (`Main._Ready`), single-player
included. Its overlay is a CanvasLayer on the HUD's layer but just *before*
the `UI` layer in the tree (same-layer CanvasLayers stack by index), so the
HUD, level-up panel, pause menu and results all draw over it; the scoreboard
has its own layer 30 so it works over the pause menu and the results.

- **Minimap** (`UI/Minimap`): one Control, all `_Draw`, redrawn at 15 Hz.
  Round, top-right under kills/gold, and it **turns with the camera**
  (camera-forward is up; a small N on the rim shows north) because a
  north-up map has to be rotated in your head mid-fight. Shows you (arrow
  turned by body facing), teammates (slot-coloured dots with an initial),
  enemies (red; elites bigger; bosses big and pinned to the rim), unopened
  chests (`ChestSpawner.Chests`, a read-only partial), unused shrines, the
  open portal, pings, and downed players as a pulsing SOS cross (you too,
  while down). Settings: "Show minimap" / "Minimap size" under CAMERA & HUD,
  stored in `[hud]` of `settings.cfg` by `Meta/HudSettings` (never in
  automated runs).
- **Name tags** (`UI/TeamTags`): canvas-drawn, so always on top of walls and
  props, unlike the old `Label3D` (still on `RemotePlayer`, hidden). Pill in
  the player's colour, health bar under the name (revive progress while
  down), fading to 40% from 22 to 80 m. Off-screen teammates get an
  edge arrow with name and distance; edge arrows keep out of the top 150 px
  (clock, phase lines) and bottom 110 px (dash meter).
- **Downed flare** (`Net/DownFlare`, a child of each `RemotePlayer`): a
  46 m additive, fog-free beam and a ring pinging outward, only while down.
  `UI/AwarenessAlerts` polls the remote down flags (10 Hz) and shows
  "<name> is down! Go revive them" for 6 s, cleared at once on revive. No
  new traffic: the 20 Hz player state already carries the down flag.
- **Boss kill credit**: the machine that owns enemy health (host, or this
  machine in single-player) records `Enemy.LastHitter` on every damage
  (`Actors/Enemy.Awareness.cs`). `NetSession.Hits` sets `Enemy.NetHitter`
  to the sender around the client's batch, so a client's killing blow is
  credited to that client. A boss death on the host sends `BossSlain`
  (reliable, call-local) to everyone; single-player publishes `BossDefeated`
  directly. The toast sits low centre (the upper middle is the map title card).
- **Scoreboard** (`Game/RunScoreboard`, static, reset per run): kills, boss
  kills and damage dealt are counted where enemy health lives (host/SP, via
  `LastHitter`); downs, level, damage taken and gold earned by each player's
  own machine (`ScoreSelf`, client to host, 1 Hz); revives given by the
  downed machine, which knows who stood next to it (`RevivedBy`). The host
  merges and sends the table (`ScoreSync`, `long[]` stride 9, 1 Hz). Hold
  **Y** (`scoreboard` action) for `UI/ScoreboardPanel`; the results screen
  has a scoreboard card (`UI/ScoreboardRows`).
- **Pings** (`Net/Pings`, `NetSession.Pings`, `UI/PingMarkers`): the `ping`
  action (G, middle mouse) raycasts from the screen centre (world and enemy
  layers, the player excluded; the ground plane if nothing is hit) and names
  the ping from what is within 3.5 m: enemy, chest, shrine; pinging yourself
  or pinging while down is "Help!", anything else "Go here". 6 s life, one
  live ping per player, 3 per 5 s checked on the sender and again on the host
  (`PingAsk` → host → `PingShow` to all, call-local). Drawn on the canvas
  (pin, ring, name, distance, edge arrow) and on the minimap, with a
  synthesised two-note chime on the SFX bus. Single-player: a local marker.

The co-op test runs all of it with `--coop-check-hud` (both sides): the
client finishes the first boss with one big hit through the normal hit
path, then each side requires the toast to name the client, a scoreboard
with kills for both players, a down marker drawn (host: the client's SOS
and the alert; client: its own SOS), a revive credited, and pings shown
both ways; the client fires a 5-ping burst straight at the host, which must
pass on exactly 3. `--coop-board-shot`, `--coop-boss-shot` and
`--coop-ping-shot` photograph the scoreboard, the toast and a ping
(`tests/shots/coop-board-*.png`, `coop-boss-*.png`, `coop-ping-*.png`).
In single-player, `--show-scoreboard` holds the scoreboard open for a
capture.

### Known limits

- Steam only for players. Direct IP stays behind the developer panel switch
  (Profile tab: "Direct-IP co-op on the CO-OP page") and the test flags.
- Under the test App ID 480 Steam shows the game as "Spacewar". An invite
  accepted, or "Join Game" pressed, while Wavebreak is closed starts Spacewar.
  Have Wavebreak running on the CO-OP page first. A real App ID fixes both.
- The Steam overlay may not appear in a game Steam did not launch (Vulkan
  only gets Steam's overlay layer when Steam starts the game). INVITE FRIENDS
  then points to the friends list's "Invite to Lobby", which needs no overlay.
- The Steam path has not been played live yet (no Steam on the build
  machine). The transport passes its fake-Steam self-test, and the exported
  build loads the DLL and fails cleanly with Steam off.
- No late joining, and no reconnecting to a run after a disconnect.
- Enemy wind-up telegraphs, burrowing and auras are not mirrored, only their
  attacks are. Cute-enemy special hazards (bubbles, puddles, pinches) hit the
  host only. Thorns use the host's stat.
- Enemy count and health are not scaled up for more players, so co-op is
  easier than solo at the same tier.
- Quests, Pearls and unlocks are counted on each machine for its own player
  and profile. Kills are counted for everyone, since every machine sees every
  kill.

## Results screen

`UI/ResultsScreen` is all containers inside one centred panel sized to the
window (up to 1560x940, 20 px margin), so it fits 1280x720 and 1920x1080
without scrolling and mirrors for Arabic: a header (VICTORY / YOU SURVIVED /
YOU DIED, the tier and CO-OP as badges, a leaderboard badge when the run
placed, which replaces `LeaderboardBanner` while the results are up, and one
short line about how it ended), six icon tiles (time, level, kills, bosses,
gold earned, Pearls), three cards (the Pearls breakdown with the balance
apart; the build as weapon and item icons with names on hover, only the
item grid scrolls; the scoreboard), big RESTART / MAIN MENU buttons with
their keys, and the seed in small print. While it is up, `_Process` keeps
the mouse visible and `PlayerCamera.TryCaptureMouse` refuses: a level-up
or shrine panel closing after the run ended restored the capture it found
when it opened, which is how the cursor vanished on the victory screen.
`--results-shot=x.png [--results-win] [--results-after=N]` (with `--auto
--invulnerable --give=...`, `--lang=ar`, `--resolution 1920x1080`)
photographs it.

## Developer panel

`UI/DevPanel*.cs` (widgets in `UI/DevUi.cs`). Ctrl+Shift+D toggles it
on the title and in a run; nothing else opens it and no normal UI mentions
it. It is a CanvasLayer under the tree root, installed by `MainMenu._Ready`
and `Main._Ready`, so it survives scene changes; it keeps itself the root's
last child so its `_Input` sees the chord first. While open, the other
panels' `_Input` return early on `DevPanel.IsOpen` (so a digit typed in a
search box never picks a card), the mouse is free, and with "Pause game
while open" (default on) the run stays paused: the pause is re-asserted each
frame because the level-up panel unpauses on close.

Tabs: Profile (unlock all or single rows, tiers, shrines, Pearls, shop,
reset; saved at once and the title reloads to show it), Player, Build, Spawn,
Stage, Info. Game-side entry points live in `*.Dev.cs` partials next to the
code they drive (`StageDirector.Dev`, `Spawners.Dev`, `WeaponSystem.Dev`,
`Inventory.Dev`, `Hud.Dev`, `Profile.Dev`, `Meta.Dev`, `Meta/DevCheats`).

Any run-side action sets `DevPanel.UsedThisRun`; such a run skips the
unlock tracker, lifetime stats, the leaderboard and tier/challenge wins.
`-- --check-devpanel` drives every control (real chord, click and typing)
and prints `DEVPANEL PASS/FAIL`; `--devpanel-shot=<prefix>` saves one PNG
per tab; `--menu-shot=x.png --devpanel-menu[=unlock]` shows it on the title.

## Graphics settings

`Meta/GraphicsSettings` holds every graphics option (`GraphicsConfig`) and
pushes it into the engine: the root viewport (MSAA/FXAA/TAA, 3D scale and
FSR 1/2.2), the directional shadow atlas and soft-shadow quality, every
`WorldEnvironment` and `DirectionalLight3D` under a node (`ApplyScene`, which
`Arena` and `ThemeDresser` call when they build a sky), `Engine.MaxFps`, vsync,
window mode, UI scale (`ContentScaleFactor`), and the camera's FOV. The camera
reads shake and "speed & dash zoom" scales, `DamagePopup` the number cap, and
`Arena` the prop-density multiplier (next map). "Weapon effects opacity"
(default 75%) goes through `GraphicsSettings.FxFade`, which every projectile,
swipe, explosion, aura disc and status arc uses for its fade, so a late build
cannot wall off the screen. `Combat/Weapons/FxGuard` keeps effects off the
player and the screen, whoever fired them: drawn sizes are capped (player-
centred fields 4.5 m, blobs such as Maelstrom Seed's vortex and explosion
shells 2.8 m, swipe and breath sheets 5 m, blades 1.5 m; hit areas are not)
and big ones lie flat on the ground (swipes on the floor, vortex and
explosion shells squashed, Brine Wake at most four pools). Every weapon
material fades per pixel nearer the camera than the player
(`DistanceFadeMode.PixelAlpha`, following the zoom), and each pooled shot
that sits on the camera-to-player line turns see-through
(`Projectile.KeepOutOfSight`). The blue "dome" late in a run was Maelstrom
Seed: its whorl and core scaled with area at chest height.
Breaker Swell draws at most four low rings. Environments and suns keep their
authored values as metadata, so switching an effect back on restores the map's
look; Sea Fret marks its fog as gameplay fog, which "fog off" keeps at 60%.
The default is the High preset, which equals the pre-settings look; automated
launches never load or save it. `UI/GraphicsPanel` is the shared page (title
settings and pause menu); window mode, size and monitor wait for APPLY and
revert after 10 s unless kept. `--check-graphics` drives every option and
prints GRAPHICS PASS/FAIL; `--menu-page=graphics[:bottom]` captures the page.

## Gameplay settings and auto-pick

`Meta/GameplaySettings` (`[gameplay]` in `user://settings.cfg`, never read or
written by automated runs; `--compact-levelup` and `--autopick-rule=<id>` set
them in memory): Compact level-up, Auto-pick on level up and its rule.
`UI/GameplayPanel` is the title's GAMEPLAY page and rows in the pause menu's
settings. With auto-pick on, `LevelUpPanel.Show` resolves the hand at once
through `Autopilot.PickByRule` (rule first, then rarity, then the bot's own
score; supply payouts only when nothing else is offered) and shows a toast;
automated runs log `LEVELUP autopick rule=... picked=...`.

Panels that free the cursor (level-up, shrine, merchant/microwave, pause
menu) hold it through `PlayerCamera.HoldCursor/ReleaseCursor`: the capture
state is taken when the first hold starts and given back when the last one
ends, so queued or overlapping panels never leave mouse-look dead until a
click. `--check-input-guards` asserts the mouse is captured again right
after two queued picks, in the full and the compact hand.

## Language (English / Arabic)

`Meta/Loc` holds the language (Settings > Language; `[general] language` in
`user://settings.cfg`, never read or written by automated runs; `--lang=ar`
forces it for captures). English text is the key: `Meta/ArabicText/*.cs` are
plain `(English, Arabic)` arrays, found by reflection (fields named `Content*`
are content, the rest UI), loaded into a `Translation` for locale `ar` so
Labels/Buttons whose whole text is a key auto-translate. Code that builds text
calls `Loc.T(text)` or `Loc.F("template {0}", args)`; a key with `{n}` holes is
also a pattern, so `T("Seasoned: Rimeheart")` resolves through `"Seasoned: {0}"`.
Arabic sets the root window's layout direction to RTL (containers and anchored
controls mirror; the title backdrop hero moves to the other side) and adds
Noto Sans Arabic UI (OFL, `assets/fonts`) as a fallback on the default font.
Changing the language reloads the title on the settings page.
Numbers, signs, fractions, key names and units inside Arabic text are wrapped
in directional isolates (`Loc.Ltr`; `T`/`F` do it automatically) so "+30%",
"10/10" and "x1.32" keep their order. The developer panel is translated too
(`ArabicText.UiDev`). `--latin-report` prints any visible English left on screen.
`--dump-strings=<txt>` lists content strings with no Arabic; the content audit
fails on any missing/empty translation, mismatched `{n}` holes, conflicting
duplicates, a missing font, or a text server that cannot shape/bidi.

## Music and the Boombox

`tools/compose_music.py` renders every track procedurally (19 OGG loops and
stings, about -20 dBFS, seamless loops). `Audio/MusicTracks` is the catalogue:
which map, boss, swarm or menu each track belongs to, and how it unlocks (free,
first heard in a real run, or a quest). `Audio/MusicDirector` asks
`Audio/Boombox` for the loop to play: the map's own track, Shuffle, or a pinned
track; the mode and pin live in `user://settings.cfg [boombox]` and automated
runs never touch them. `UI/BoomboxPage` is the title page (play, pin, modes,
volume); the pause menu has a MODE/SKIP row. `ContentAudit.Music` checks every
map has its track and remix and every file loads with the right loop flag.

## Title screen and boot splash

`UI/MainMenu.Backdrop` builds the title scene from primitives: a sunset
procedural sky, a shader sea (summed sine waves, crest foam and a foam ring
around the island), a sand island with palms, rocks, shells and a lighthouse
whose beacon pulses, gulls circling, and a few real enemy rigs bobbing in the
shallows. The selected hero (and hat) stands on the beach, scaled up. The camera
sways slowly instead of orbiting, so the menu column never covers the scene.
Right-to-left layouts mirror the camera target, hero, lighthouse and monsters.

The boot splash is `assets/splash.png` (icon, name, tagline on the menu's night
colour), drawn by `installer/make_splash.py` and set under `[application]
boot_splash/*` in `project.godot`, so the Godot logo never shows. The version
(`config/version`, the exe's version resources and the installer) is 1.1.0.

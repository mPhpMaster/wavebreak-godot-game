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
| `FinalSwarm` consts | +15% hp, +12% dmg, +0.35 spawn rate per 30 s | the endless phase after the last boss; uncapped so it always ends |
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
  Core/        EventBus, GameEvents, Rng, Pool
  Stats/       Stat, StatSheet          <- the pipeline
  Combat/      DamageInfo, WeaponSystem, WeaponRuntime, Projectile
  Actors/      Player, PlayerHealth, PlayerCamera, Enemy, EnemyManager, Pickup
  Progression/ XpCurve, Inventory, OfferPool
  UI/          Hud, LevelUpPanel, ResultsScreen, DamagePopup
  Data/        Definitions, Catalog, Stages   <- all content (Stages = maps + phases)
  Game/        Main, Arena, StageDirector, Portal, RunState, Screenshot
  Meta/        Profile, Unlocks, Characters, MetaContent, SecretSpawner, UnlockTracker
  Visuals/     ModelSpec, ModelBuilder, CharacterRig, PlayerLooks, EnemyVisuals,
               PropTheme, PropFactory, ToonMaterials, ArtAssets, showcase/stress
assets/        imported CC0 models (see assets/CREDITS.md)
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
dies; that death still publishes `RunEnded(survived: true)`.
`--no-final-swarm` restores the instant win; `--difficulty=<id>` forces a tier.

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
## Developer panel

`UI/DevPanel*.cs` (widgets in `UI/DevUi.cs`). Ctrl+Shift+Alt+F12 toggles it
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

## Developer panel

`UI/DevPanel*.cs` (widgets in `UI/DevUi.cs`). Ctrl+Shift+Alt+F12 toggles it
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
`Arena` the prop-density multiplier (next map). Environments and suns keep their
authored values as metadata, so switching an effect back on restores the map's
look; Sea Fret marks its fog as gameplay fog, which "fog off" keeps at 60%.
The default is the High preset, which equals the pre-settings look; automated
launches never load or save it. `UI/GraphicsPanel` is the shared page (title
settings and pause menu); window mode, size and monitor wait for APPLY and
revert after 10 s unless kept. `--check-graphics` drives every option and
prints GRAPHICS PASS/FAIL; `--menu-page=graphics[:bottom]` captures the page.

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
`--dump-strings=<txt>` lists content strings with no Arabic; the content audit
fails on any missing/empty translation, mismatched `{n}` holes, conflicting
duplicates, a missing font, or a text server that cannot shape/bidi.

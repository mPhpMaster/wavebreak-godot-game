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
| `--check-perks [--check-perks-shot=x.png]`, `--perk-levelup-shot=x.png` | perks: every perk at max, each trigger made to happen, `PERKS PASS/FAIL`; a hand of perk cards (see "Perks") |
| `--perf` | every 5 s: FPS, CPU/GPU render ms, draw calls, nodes, live enemies, per-system ms (`Game/Prof`), and a node census every 30 s |
| `--levelup-shot=x.png`, `--menu-shot=x.png --menu-page=<page>`, `--panel-shot=<prefix>` | capture screens |
| `--validate-content`, `--check-pause=<dir>`, `--check-chests=<dir>`, `--check-boundary`, `--check-input-guards`, `--check-devpanel`, `--check-graphics`, `--check-walk` | self-checks printing PASS/FAIL (`--check-boundary` also runs the BLOCKERS checks on maps with blockers) |
| `--map-tour=<prefix>` | photograph the current map: the start facing N/E/S/W, each zone, the Tab big map, the minimap, an overhead view; use with `--route=beacon` (Stormwatch Harbour first) |
| `--check-restroom`, `--restroom-shot=x.png`, `--check-phases`, `--check-portal-timer`, `--portal-seconds=N`, `--no-rest-room` | rest room, run length and portal countdown (see "Rest room between maps") |
| `--check-totem`, `--totem-shot=<prefix>` | Tide Totem self-check (`TOTEM PASS`; no `--auto`, add `--invulnerable`) and its captures (see "Tide Totems") |
| `--check-trade`, `--swap-meet`, `--rest-exit-seconds=N` | swap meet: rules and escrow self-check (`TRADE PASS`), build the stall in single-player (captures), shorten the co-op rest room's countdown (see "Swap meet") |
| `--coop-host=PORT` / `--coop-join=IP:PORT` with `--auto` | automated co-op session over ENet (direct IP), printing `COOP PASS/FAIL` (see "Online co-op") |
| `--coop-expect-migration`, `--coop-no-migrate`, `--coop-late-join`, `--coop-expect-late-join`, `--coop-toast-shot=x.png`, `--coop-mark-file=P --coop-mark-at=S` | host migration and late-join checks (see "Host migration and late join") |
| `--check-browse`, `--fake-browse`, `--lobby=public\|invite` | public games list: filter/format self-check (`BROWSE PASS`), made-up rows for a `--menu-shot`, the lobby type for this run |
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
               Chest, Merchant, Microwave, Campfire, TideTotem, MapShrine, CuteShrine, Station
  Audio/       AudioDirector, SfxSynth, MusicDirector, MusicTracks, Boombox, AudioSettings
  Progression/ XpCurve, Inventory, OfferPool, TomeBook, LevelUpChoices
  UI/          Hud, LevelUpPanel, PauseMenu, StatsPanel (+ BigMap), ResultsScreen, BossBar, MainMenu
               pages (Codex, MetaShop, Boombox, Graphics, CoopPage), CoopHud, DevPanel, CuteStyle, Icons,
               AwarenessLayer (Minimap, FogOfWar, TeamTags, PingMarkers, AwarenessAlerts, ScoreboardPanel)
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

### Swap meet (co-op trading)

`Actors/Trader`, `Game/RestRoom.Trade.cs`, `Game/Trade/*` (`TradeRules`,
`TradeLedger`, `TradeView`, `SwapMeet`, `TradeCheck`), `Net/NetSession.Trade.cs`,
`Net/NetSession.Test.Trade.cs`, `UI/TradeUi`, `UI/TradePanel`, `UI/Hud.Trade.cs`,
`Progression/Inventory.Trade.cs`, `Progression/TomeBook.Trade.cs`,
`Audio/SfxSynth.Trade.cs`, `Meta/ArabicText/ArabicText.UiTrade.cs`.

- **The stall:** front left of the island, a beaver behind two crates under a
  sea-green scalloped awning, a crab on the counter, a rug of wares, a hanging
  "SWAP MEET" board and two arrows circling over the roof, so it never reads
  as the purple merchant. Interact (E) opens the teammate picker.
- **Single-player: no stall.** Nobody to trade with, so the room is exactly
  what it was (`--check-restroom` still sees 3 stations). `--swap-meet` builds
  it anyway for captures; E then pops "Trading needs another player", as it
  does for a co-op player left alone in the session.
- **Flow:** picker (1-3) -> `TradeAsk` to the host -> `TradeInvite` to the
  teammate (ACCEPT 1 / DECLINE 2 or Esc; expires after 20 s) -> the host opens
  a session and sends `TradeOpen` to both. Each side offers gold, item copies
  and whole tomes (with their level) and sends it with its holdings
  (`TradeSet`; holdings are re-sent whenever they change). READY, then
  CONFIRM, both sides. **Every offer change bumps the session version and
  clears both READY and both CONFIRM**, and READY/CONFIRM carry the version the
  player saw, so a click on a table that has since changed is ignored.
- **Rules** (`TradeRules.Validate`, the same code on the host, in both windows
  and in the self-check): a side gives only what it holds; the receiver must
  hold the result *after* the swap. Item stack caps count what the receiver
  gives away of the same item; a tome must be new to the receiver (tomes are
  never merged) unless the receiver gives theirs away in the same swap; the
  receiver's tome count after the swap must fit its tome slots. The window
  greys out bag tiles the partner could not take (reason in the tooltip) and
  lists every broken rule; the host refuses READY while one stands.
- **Weapons are not traded.** A weapon is more than an id and a level: each
  level carries a rolled upgrade of some rarity (`WeaponRuntime.Upgrades`),
  the character's starting weapon is part of its identity, the inventory's
  weapon ledger only ever goes up (it never forgets a weapon), and a player
  could trade away their only weapon. Moving the rolled upgrades faithfully
  would need a weapon serialisation format for this one feature; the window
  says "Weapons are not traded."
- **Executing without duplication.** Each machine owns its own inventory
  (see "Who owns what"), so the host cannot rewrite both. It runs an escrow:
  `TradeEscrow` asks each side to re-check against its live inventory, remove
  its offer and report exactly what it removed (`TradeEscrowed`, tome levels
  included); only when both escrows are in does the host send each side the
  other's (`TradeGrant`), which `TradeLedger.Settle` adds. A failed check or a
  10 s timeout rolls back (`TradeRollback`: escrows go home, READY cleared, the
  window stays open with the reason); a player leaving, the rest room ending,
  Cancel, or the session closing ends the trade (`TradeClosed`, or
  `TradeLocalEnd` when the host itself is gone): every escrow is refunded to
  its owner. The goods are always in one inventory or one escrow. The host's
  own player takes the same path through local calls (`ToParty`).
- **Room end:** the host closes every trade in `LeaveRestRoom`, before the
  Phase message (reliable and ordered, so every window is closed and every
  escrow is back before the next map loads). That covers the CONTINUE
  countdown running out with a trade open.
- **Applying:** gold moves with `Inventory.TradeGold` (neither earned nor
  spent: quests, the scoreboard, pickup sounds and gold procs do not see it;
  the HUD gets the total through a zero-amount `GoldGained`); items with
  `Inventory.TradeRemove` / `Add` (the stack is re-applied to the stat sheet,
  so modifiers follow the count exactly); tomes with `TomeBook.TradeRemove` /
  `TradeGrant` (every modifier is tagged with the def, so removal is one
  `RemoveAllFrom` and the grant writes the level's total once). The HUD's tome
  rows are rebuilt (`Hud.ResyncTomes`); the results screen drops items at 0.
- **UI:** the cream card style, two columns (mirrored in Arabic): YOU GIVE
  (offer well, gold row with -100/-10/-1/+1/+10/+100/ALL, your bag) and
  "NAME GIVES" (their offer and gold, then what you will hold afterwards).
  Tiles are the stats sheet's build-box tiles as buttons with tooltips: a
  click offers one copy, right-click or Shift-click the whole stack, a click
  on an offered tile takes one back. Keyboard: Tab/arrows and Enter, + / -
  for 10 gold (Shift: 100), 1 READY, 2 CONFIRM, Esc/E cancel. The cursor is
  freed through `ModalPanel` (`PlayerCamera.HoldCursor/ReleaseCursor`); in
  co-op that sets `LocalHold` like every other panel. Sounds
  (`SfxSynth.BuildTrade`): a "tok" when the partner's offer changes, a
  two-note ding for each READY or CONFIRM, an arpeggio on completion, a
  falling pair on cancel, a chime for an invite.
- **Checks:** `--check-trade` (one process: packing, every rule, escrow,
  refund restoring holdings and every stat exactly, settle; `TRADE PASS`).
  `tests/coop.ps1` scenarios `coop-trade` (English, 1280x720) and
  `coop-trade-ar` (Arabic, 1920x1080): the host offers 40 gold + a stat item
  for the guest's tome; both READY; the host raises the gold to 50, which must
  reset READY on both sides (`ready_resets=1`); both READY and CONFIRM; each
  side checks its gold, items, tomes and that its stat sheet moved; the
  script checks the team's gold total did not change. Screenshots
  `tests/shots/coop-trade[-ar]-host-window.png`, `-client-window.png`,
  `-invite-client.png`. `coop-trade-timeout`: the guest asks, both READY, the
  host stands in the portal with `--rest-exit-seconds=8`; the room ends by
  `timeout`, both windows close with "The rest room ended", and both
  holdings are unchanged.
- **Limits:** a tome received at max level counts towards "max a tome" feats
  on the receiver. If the host process dies between sending the two
  `TradeGrant`s of a guest-to-guest trade, one guest could end up with both
  sides (each guest refunds an unanswered escrow when the host goes away);
  both grants leave in the same network flush, so it takes a crash at that
  exact moment. Trading happens only in the rest room.

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

## Heroes trio and signature weapons

Three more characters (`Meta/Characters.Heroes.cs`): Sunny the Lookout,
Biscuit the Sea Dog and Flick the Kite Fox. Each has a passive
(`Meta/CharacterPassives.Heroes.cs`: Calm Lookout, Old Salt, Sea Breeze), a
stat trade, six skins and quests (`Meta/Quests.Heroes.cs`: a gate plus five
skin quests each).

Their bodies are their own, hand-built from `MeshKit` shapes in
`Visuals/HeroModels.cs` (look ids `hero-sunny`, `hero-biscuit`, `hero-flick`,
resolved by `PlayerLooks.Heroes.cs`); nothing else wears them. They used to
be Kenney cube pets, which read as repeats of the cute crew, and no free
rigged model fitted the brief (searched poly.pizza/Quaternius CC0: the
animated fox and dogs are realistic quadrupeds, the CC0 birds are not
animated). Chibi bipeds, 1.5 m, head about a third of the height:

* Sunny: red macaw, navy captain's coat with gold epaulettes, buttons and
  coat tails, a three-colour crest, long tail feathers, feather hands, a brass
  spyglass in the right hand (the weapon arm, so the windup raises it);
* Biscuit: broad tan sea dog, floppy ears and eye patch, striped jersey,
  sailor collar, red neckerchief, bell-bottoms, an iron anchor slung on his back;
* Flick: slim orange fox, tall dark-tipped ears, flight goggles on the
  forehead, a scarf with two streaming ends, a huge white-tipped tail, a kite
  on his back and a kite spool in his left paw.

How they plug in: `CustomBody` (partial in `HeroModels.cs`) gains jointed
limbs with the biped part roles, so `CharacterRig.PoseBiped` animates them
like every procedural hero (idle breathing, walk/run cycle, air tuck, dash,
slide, slam, flip, weapon-arm windup and strike, hit recoil and flash, death
fall and dissolve), and a hat anchor (`BuiltModel.HatAnchor/HatRadius`): the
hat sits on the crown under the hips node, so it leans, bobs and flips with
the head (`CharacterRig.Hats`). Skins stay `SkinRecolor`s, with a mask:
`MeshKit.Paint` writes vertex alpha, and the character shader mixes the
recolour by it, so fur and plumage take a skin's dye fully, clothes partly,
and eyes, beak, brass, lenses and iron not at all (every other model has
alpha 1, unchanged). `--art-showcase=lineup[:ids]` stands characters side by
side (default: the trio beside the five cube-pet crew) and
`--art-showcase=poses:<id>` shows one in each verb.

Each starts with a **signature weapon**, an ordinary `WeaponDef` in the
catalogue (`Data/Catalog.Heroes.cs`, appended from `Catalog.Expansion`'s
static constructor) delivered by `Combat/Weapons/Signatures.cs`:

| Weapon | Plays | What it does | Lv 4 | Max level |
|---|---|---|---|---|
| Spyglass Glint | precise | instant thin beam at the enemy nearest the centre of the camera's view (13° sight cone, elites first); a sighted shot is a guaranteed crit, +2.5%/m past 4 m (max +60%), +15% per body pierced; nothing in sight falls back to the nearest enemy | +1 pierce, 19° cone | a bullseye kill reflects into the farthest enemy within 12 m |
| Mooring Anchor | control | thrown at the nearest enemy; impact, then a rope ring for 3 s (x Duration): chafe ticks, chill-slow, an inward pull at and just past the edge; then hauled home through everything still inside | the haul stuns | the haul drags the ring together, then a second slam |
| Skimmer Kite | movement | a kite on a string trails low behind you (loops round you when idle); every tick the string and the kite (x2) cut what they touch, scaled 0.7-2.2x by the kite's speed, so turning and running is the aim | +1 kite | every 2 s each kite drops lightning on an enemy near it |

Exclusivity: `OfferPool.BuildCandidates` never offers a signature as a new
weapon (live system or ledger path), so only its owner holds one; held, it
levels through the normal rolled upgrade cards, and the card that reaches
Lv 4 or max level names the milestone (`Signatures.MilestoneLine`). The
Toggler never lists them. Upgrade lines that matter: damage, attack speed,
projectiles (more beams / anchors, folded into damage past 3 / kites, max 3),
area, range, pierce, projectile speed, crit.

Effects (`Combat/Weapons/SignatureFx.cs`) are stepped from
`Projectile.UpdateAll`, so they pause with the run and run inside the
audit's fire test: thin bars and small meshes, FxGuard-tracked materials,
`GraphicsSettings.FxFade`, drawn sizes capped (rope ring at
`FxGuard.MaxCentredRadius`, flashes at 0.9 m, lines 0.03-0.12 m), the ring
fading further over a player (`FxGuard.OverPlayer`). Sounds are their own
(`Audio/AudioDirector.Heroes.cs`, from `SignatureCue` on the bus): glass ting
and bullseye fifth, rope whoosh / clonk / chain rattle, kite flutter and
crackle, each rate-limited; a signature's shot swallows the generic shape
sound (`Signatures.ConsumeFired`). In co-op every hit goes through
`TakeDamage`/`ReportHit` and `Enemy.ApplyChill/ApplyStun`, so a client's
signature hits on puppets are forwarded like any other weapon's.

Checks: `ContentAudit.Heroes` (owners, exclusivity over 3000 rolls, held
upgrades when run with `--character=<hero>`, milestones, icons, skins as real
repaints, gate quests); `tests/coop.ps1 -Only coop-heroes` (host Flick, guest
Sunny, `--coop-expect-signature`); `--art-showcase=skins:<id>` lines up a
character's skins; `--art-showcase=hats:<hat>:3` shows the fourth row.
Testing a locked hero needs a profile that has it: `--profile=<cfg>` with
`[unlocks] qh_sunny=true` etc.

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
## Perks

Fifteen levelled perks (`Data/Tomes.Perks.cs`, rules in
`Combat/Weapons/PerkProcs*.cs`). A perk is a level-up pick whose levels buy a
*rule* (a proc, a conditional stat, a team effect) instead of one clean stat.

- **A perk is a tome.** `PerkDef : TomeDef`, appended to `Tomes.All` by one
  line in `Tomes.Expansion.cs`. Everything a perk must respect is already keyed
  by `TomeDef`: the slot limit (`TomeBook.Slots`, Bookshelf included), banish,
  reroll and skip, the Toggler (`OfferPool.DisabledTomes`), "Prefer tomes"
  auto-pick, the HUD tome list, the build box, the codex, the dev panel,
  mastery quests and `--give`. A separate offer type would have had to be
  threaded through each of those. Only three places say "perk": the card text
  (`Offer.ForTome`), the card's kind tag (`· PERK`) and the icon (own glyph and
  trigger badge, not a book). **Perks share the four tome slots**, so a build
  chooses between a stat and a rule; the new-perk card says "uses a tome slot".
- **Numbers live in the table.** Each perk has 3-4 `PerkParam`s, one value per
  level (5-8 levels), and a template sentence with a hole per param.
  `PerkProcs` reads every number through `PerkDef.V(param, level)`, so the
  card, the codex, the build box and the effect cannot disagree. A card shows
  what this level adds ("hits 11 → 10 · jumps 2 → 3") and the total after it;
  a new card shows level 1. `TomeBook.Add` writes no modifier for a perk.
- **Runtime** (`PerkProcs`, one node per run, attached by `RunMeta`), same
  habits as `ItemProcs`: listens on the bus, every stat change is one tagged
  modifier per (perk, stat) replaced each frame (level 0 writes nothing, so a
  cleared build leaves no residue), damage is tagged `perk:` with proc
  coefficient 0 and never triggers perks or items, and it has its own `Rng`.
  Kills are "mine" when a hit published on this machine killed (weapons,
  statuses, items, perks): `EnemyDamaged` is only ever published for local
  hits, the host applies forwarded hits without publishing.
- **The fifteen**: Stormcount (every Nth hit chains lightning), Bubble Net
  (elites drop a bubble worth temporary shield), Cold Wake (dashes leave a
  chilling, damaging wake), Crab Stomp (landing on an enemy stomps and
  bounces), Crimson Tide (crits heal, capped per second; megacrits heal a %),
  Flotsam Orbit (collected XP orbits you as damaging motes), Golden Ballast
  (gold pickups stack timed damage; full stacks burst), Last Light (refuse N
  lethal blows per map), Keeper's Beam (a beam marks the nearest closed chest;
  opening it pays gold and heals), Tidal Pulse (periodic knock-back pulse),
  Barnacle Hide (armour and thorns grow while standing still, shed slowly on
  the move), Following Sea (attack and shot speed per m/s moved), Cornered Eel
  (damage, lifesteal and evasion as health falls below half), and the two
  team perks below.
- **Team perks** (`PerkProcs.Coop.cs`, `Net/NetSession.Perks.cs`). Each
  machine owns its own player's perks, so a teammate's buff is applied on
  *your* machine: every player sends its Crew Beacon and Lifeline levels to
  all (`PerkLevels`, reliable, on a change and every 3 s, so late joiners and
  migrated sessions catch up), stored in `PerkNet` by peer id. Crew Beacon:
  each machine gives its own player the strongest beacon in range (its own or
  a teammate's puppet's; they do not stack). Lifeline: the downed machine
  already counts its own revive (`NetSession.StepRevive`); it reads the
  helper's level for the speed and the reach. Alone, the beacon buffs you and
  Lifeline's low-health surge heals you.
- **Also from chests**: `PerkProcs.RollChestPerk`, called first by
  `ChestSpawner.TryOpen`: 30% of gilded and 5% of standard chests pay a perk
  level instead of an item, when a perk can be taken (held below max or a free
  slot; banished and Toggler-off perks excluded). The merchant stays items only.
- **Visuals** (`PerkProcs.Fx.cs`): flat ground rings, small orbs, puddles and
  one thin beam; at most 24 transient effects plus capped motes (8), bubbles
  (5) and wake puddles (14); rings centred on the player are capped at
  `FxGuard.MaxCentredRadius`; everything fades through
  `GraphicsSettings.FxFade` and the near-camera fade. **Sounds**
  (`Audio/AudioDirector.Perks.cs`): eleven soft synthesised cues, each with its
  own minimum gap, and all perk sounds together at most 4 per second.
- **Checks**: the content audit (`ContentAudit.Perks.cs`, EN and AR) checks
  levels, tables, every level an upgrade, card text per level, icons, Arabic
  for every string, no stat written by the book, shared slots, offers, Toggler,
  banish, "Prefer tomes", and chest perks. `--check-perks` gives every perk at
  max (slots lifted for the check) and makes each trigger happen in the real
  game, printing `PERK <id> ok|FAIL` lines and `PERKS PASS/FAIL`
  (`--check-perks-shot=x.png` adds a frame). `--perk-levelup-shot=x.png`
  (with `--lang=ar`) photographs a hand of perk cards. `tests/coop.ps1` runs
  the main scenario with `--coop-check-perks` (host: Crew Beacon + Lifeline,
  client: Lifeline): levels arrive both ways, the client stands in the host's
  beacon, and the client's revive is sped up by the host's Lifeline.
- **Known limits**: on-kill perks count only kills landed on this machine.
  Flotsam motes come from shared XP, so teammates' orbs feed them too. Keeper's
  Beam is local (teammates do not see your beam). Last Light runs before the
  co-op down, so a refused blow never downs you. Perk numbers are a first
  balance pass.

## Stormwatch Harbour, blockers, fog of war, the Tab map

### The sixth map

`Data/Stages.Harbour.cs` (the def), `Game/Arena.Landforms.cs` (the layout),
`Visuals/PropTheme.Harbour.cs` (the `harbour` biome). The biggest map: ten
sides, in-radius about 181 m (Shallows 155, Rimeglass 143). It is the one map
built to be *explored*: from the start no line of sight leaves the town.

- **Layout** (`StageDef.Layout = "harbour"`, built before the random terrain,
  which then fills the space left): Harbour Square (r 24, kept clear for the
  portal) ringed by six houses at 34 m, a second row of six behind every
  street out of the square at 62 m, so each street ends at a facade; four
  20 m cliff spurs on the diagonals (84-150 m) cut the outer belt into four
  zones you only see from inside: **Lighthouse Point** (north: a walkable
  headland, 5 m high with 20 degree slopes all round, the lighthouse with a
  sweeping beam, the keeper's cottage, sea stacks), the **Sunken Quarter**
  (east: half-drowned houses, the map's ten flooded-street pools placed
  there through `Arena.NextHazardSpot`), **Coral Canyon** (south: two 70 m
  rock walls 20 m apart, coral spires) and **the Docks** (west: warehouses,
  two moored ships, crates). The lighthouse (31 m) is the one thing seen
  over the roofs: the landmark you steer by. Zones are registered
  (`MapBlockers.Zone`) for the big map's labels and the line under the
  minimap.
- **Cost**: one Jolt box or cylinder per blocker on the arena's single static
  body (37 shapes), and house bodies, roofs, windows, trims, round parts and
  cliff rocks each go through one MultiMesh, so the town is a handful of
  draw calls. Measured with `--perf` in one `--route=beacon --auto` run
  (same machine, same run, averages over each map): Stormwatch 63.5 FPS,
  485 draws, 1.46M primitives, physics 3.4 ms; Gravemoor next 67.9 FPS,
  601 draws; Cinder Reach 55.5 FPS, 820 draws. More primitives (the town's
  facades and windows), fewer draw calls than the classic maps.
- **Content**: its own enemy mix (`EnemyIds`: crabs, eels, jellies, gulls,
  shell-backs; no fire or frost kin, few shooters), boss Undertow Oracle, its
  own track and remix (`harbour`, `harbour_shanty` in `tools/compose_music.py`),
  11 map challenges (twists Night and High Tide), quests `qm_map_stormwatch`
  and `qm_map10_stormwatch`, routes `start:stormwatch` and **Beacon Road**
  (`--route=beacon`: Stormwatch, Gravemoor, Cinder Reach), all appended after
  the existing rows because the profile stores route and map-challenge
  choices as indexes. Classic, `Stages.Run` and `LongOrder` are unchanged.

### Blockers: steering and placement

`Game/MapBlockers` holds the big solids of the current map as oriented boxes
on the floor plane (empty on every other map). Enemies steer straight at the
player with no pathfinding, which is fine round a pillar and hopeless round a
30 m house, so `Actors/Enemy.Blockers.cs` adds one step: when the straight
line to the player crosses a blocker (grown by 1.4 m), walk to the nearer of
that box's two silhouette corners (shortest corner-then-target, with
hysteresis so a crowd does not flip sides), and from there the line clears or
the next box takes over. That is a visibility-graph walk one box deep, enough
for a map of separate convex blockers kept at least 8 m apart. The full test
runs every fourth tick per enemy (staggered); an enemy on a detour never
coasts through a wall (`CoastThisTick`). The autopilot uses the same corner
walk (`Game/Autopilot.Blockers.cs`). Placement: the spawn-ring search, chests,
pots and stations reject points inside a blocker or on its roof; shrines use
`Arena.ClearSpotNear`, which already sees the reserved footprints.

`--check-boundary` prints `BLOCKERS` lines on such maps (`StageDirector.Blockers.cs`):
the player runs, jumps and dashes into five blockers and must never get
inside one; 300 real spawn-point searches must land outside them; no chest
or shrine stands in one; and twelve runners dropped behind the second row of
houses must reach the player in the square within 30 s (12/12 with steering,
0/12 with `MapBlockers.SteeringOff`, the straight-line baseline).

### Fog of war

`UI/FogOfWar` (one node per run, from `AwarenessLayer`), `UI/Minimap.Fog.cs`,
`Net/NetSession.Fog.cs`. Every map starts unexplored. Two layers:

- **The picture**: the whole map at 1.25 m a pixel (floor, wall band, raised
  features from `Arena.Features`, blockers as outlined shapes), mixed per
  pixel with a dark fog by how well it was seen. Soft-edged circles of 32 m
  are stamped round every player this machine knows about (its own and the
  teammates' puppets) whenever one has moved 2 m; the texture is uploaded at
  most about 7 times a second, only when it changed. Unseen ground shows the
  wall outline at 30% and nothing that stands on it.
- **The record**: one bit per 5 m cell (761 bytes on Stormwatch). Co-op shares
  it: a client sends its bits to the host when they changed (at most every
  2 s, reliable), the host ORs them in and sends the team mask back when it
  changed, and every 10 s regardless so a late joiner or a migrated session
  catches up. A mask carries a token (phase and a stable hash of the map id;
  `string.GetHashCode` differs per process); one for another map is dropped.
  Worst case well under 1 KB/s per player.

Rules, the same on the minimap and the big map: enemies (bosses too), chests
and shrines in fog are not drawn; you, teammates, pings and SOS markers
always are; **the portal is always drawn once open**: it opens at the centre,
where every map is entered, so it is never in fog, and hiding the way onward
would only cost time. The rest room has no fog. `--coop-check-fog` (in
`tests/coop.ps1`) has each side reveal a far probe nobody stands in and
requires the other side to see it through the mask (`COOPFOG PASS`).

### The Tab sheet

`UI/StatsPanel` (+ `StatsPanel.Map.cs`, `UI/BigMap`): the big map in the
middle (the fog texture, zone labels once found, revealed chests and shrines,
the boss when it stands in revealed ground, the portal, pings, teammates with
names, you with your reveal ring, north up, never mirrored), the stats on one
side (two columns from 1600 px wide, one scrolling column below), the build
box (weapons, tomes, items as icons with hover details) on the other. The map
is as large as the rest leaves: about 470 px at 1280x720, 780 px (the cap) at
1920x1080. No settings or controls are on it: sound, mouse, gameplay,
graphics and key bindings live under Settings only. The pause menu is the
same sheet pinned, with its buttons under it, so it matches. While the sheet
is up the shrine markers (ShrineHud, a layer above the UI) hide.

## Tide Totems

`Actors/TideTotem`, `Game/Interactables.Totems.cs`, `Actors/Pickup.Totem.cs`,
`Game/TideWhoosh`, `Audio/SfxSynth.Totem.cs`, `UI/TotemIcon`,
`Net/NetSession.Totems.cs`. A map structure: walk up, press E ("E — call the
tide (pull all XP and gold)"), and every experience orb and coin lying
anywhere on the map flies to you.

- **The model**: three carved sea faces stacked on a driftwood plinth (a
  pufferfish, a clam holding a pearl, a conch with a pearl tip), each with
  glowing sea-glass eyes front and back, about 3.6 m tall (twice a hero).
  Idle: the station marker bobbing above it, an aqua light, pulsing eyes and a
  pulsing ring of water on the ground. Used: the colours grey, the eyes close
  to sleepy lines, cracks show, the light and marker go out. It is a
  `Station`, so `Interactables` handles reach, focus (chests keep the key) and
  the prompt like the merchant's and the microwave's.
- **How many and where**: by map size (`TotemCountFor`, in-radius under
  135 m one, under 165 m two, else three): Thornmeadow and Gravemoor 1;
  Rimeglass, Cinder Reach and the Shallows 2; Stormwatch Harbour 3. Placed after the
  pots with their own rng (the stations' and pots' rolls are unchanged), one
  per sector of the map at 30-76% of the radius: the arena's terrain
  footprints first (`Arena.ClearSpotNear`: mounds, platforms, pools), then the
  stations' rules (physics ground, flat, 3.4 m headroom, no prop boxes, no
  blocker, clear of chests and stations), at least 22 m from the start and the
  centre (the portal), and 4 m outside every shrine's ring (charge rings
  included) and friendly shrine. Never in the rest room (`UseRestRoom` places
  none). `--check-boundary` prints a `TOTEMS` line per map: the count, none in
  a blocker, none near a shrine, inside the walls, at least 20 m apart.
- **Cost and balance: free, once.** It adds nothing to the run's economy; it
  only fetches what is already on the floor, which a Magnet item (bigger
  pickup range, all the time) does a little of every second. Charging gold to
  fetch gold would make it arithmetic instead of a decision. Once per totem
  and one to three per map makes it a "when": after a big fight on the far
  side, or before the boss. **Med-packs are left where they are**, so a totem
  never stands in for finding a heal.
- **The pull** (`Pickup.CallTide`): latches every XP orb and coin to the
  caller with a faster chase (`TideMaxSpeed` 85 m/s, ramp 150 m/s²) than the
  magnet's 30 m/s, so the far side of Stormwatch (about 300 m) arrives in
  under five seconds; the self-check measures about 1.8 s on Thornmeadow.
- **The whoosh** (`TideWhoosh`, 0.8 s): two thin rings of water racing out
  along the ground to 7 m and a twisting translucent spout round the totem.
  It stays on the ground and round the totem, and fades through
  `GraphicsSettings.FxFade`, so the weapon-effects opacity option turns it
  down too. The sound (`SfxSynth.BuildTotem`, 1.35 s): a soft conch "hoo"
  rising a fifth, a wave of filtered noise rushing in, and four glassy shell
  chimes, played positionally at the totem, so teammates hear it from afar.
  Popup: "The tide answers!" (teammates: "<name> called the tide").
- **Maps**: an upright aqua totem marker (`UI/TotemIcon`) on the minimap and
  the Tab big map for awake totems on revealed ground, the chest and shrine
  rule (see "Fog of war").
- **Co-op** (`NetSession.Totems.cs`): shared, unlike the other stations. The
  host places the totems and sends `TotemPlaced` (id, spot, facing, asleep)
  to everyone, and the whole list again after a world snapshot (late join,
  host migration). E sends `TotemAsk`; the host grants the first asker,
  latches every networked orb to that peer (its collector loop flies and pays
  them as usual: XP shared, gold to the collector) and sends `TotemFired`
  (id, peer) to everyone, which puts the totem to sleep everywhere and plays
  the whoosh; the caller's machine also pulls its own pot drops and shows its
  copies of the orbs flying in. A later asker hears `TotemNo`.
- **Autopilot**: wakes a totem within 50 m when at least 25 orbs and coins lie
  out of its pickup range (`BotTotemOrbs`).
- **Developer panel**: Spawn tab, STATIONS & PICKUPS: "Tide Totem" (synced in
  co-op; the host's panel only).
- **Tests**: `--check-totem` drops 40 XP orbs, 20 coins and 3 med-packs 60-140
  m away, presses the real E at a placed totem and requires everything but the
  med-packs collected within 5 s, gold and XP paid, the totem asleep and
  refusing a second call, a minimap marker drawn while awake, and no totem in
  the rest room after (`TOTEM PASS`). `--totem-shot=<prefix>` saves
  `-idle`, `-prompt`, `-whoosh`, `-asleep`, `-minimap` and `-map` PNGs (add
  `--lang=ar` for the Arabic prompt). In `tests/coop.ps1` (`coop`,
  `--coop-check-totem`, `NetSession.Test.Totems.cs`) the host puts a totem by
  the guest and drops 24 orbs and 12 coins far away; the guest presses E. The
  host must latch them all to the guest, see every one collected by the guest
  and the totem asleep; the guest must see them arrive, be paid the coins and
  have the map's placed totems (`COOPTOTEM PASS` on both,
  `tests/shots/coop-totem-client.png`).

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
| A player's body, movement, health, weapons, level-ups, inventory, tomes and perks (teammates' Crew Beacon / Lifeline levels are synced, see "Perks") | that player's machine | `RemotePlayer` puppets from 20 Hz unreliable state: position, facing, velocity, pose, health, down flag, pickup range |
| Enemies (director, spawns, AI, health, death) | host | `Enemy` puppets: spawn and death are reliable events; position, facing and health come in 15 Hz unreliable snapshots, with smoothing and a short look-ahead |
| Damage *to* enemies | host applies it | Clients' weapons fire locally at puppets. A hit is predicted (number, flash, health bar), batched and sent to the host, which applies it to the real enemy. Crowd control (stun, chill/freeze) is forwarded the same way. Burn and poison tick on the client's puppet and forward each tick |
| Damage *to* players | the victim | The host's contact swings at a remote player become a `Hurt` RPC. The owner applies it through their own i-frames, dodge, armour and shield. Enemy shots, blasts, shockwaves and flashes are mirrored, and each machine tests them against its own player |
| Drops (XP, gold, med-packs) | host | The host rolls drops and decides who collects each orb: the first player whose magnet range it enters, or the nearest living player if that one goes down or leaves. Clients animate the magnet and wait for the host's word |
| Chests | host places; first to ask gets it | `E` sends a request, and the host grants it to the first asker. The winner pays from their own gold and price ladder and rolls from their own pool. Everyone else sees it open with the winner's name |
| Phase flow (waves → boss → portal → next map, win, Final Swarm) | host | Reliable `Phase`, `Boss` and `BossDown` messages replay the steps. A client's `StageDirector.Tick` only guards falls. The run clock is synced twice a second |
| Rest room (entering, leaving, the CONTINUE count, the boss portal's countdown) | host | Reliable `RestEnter` when the host takes the boss portal; leaving is the usual `Phase` message. `RestStatus` (in the portal / total / countdown) and `PortalTimer` stream at 4 and 2 Hz, unreliable. Each machine builds its own room, revives and heals its own player, and shops with its own gold |
| Trades at the swap meet | host decides; each side moves only its own goods | The host validates both offers and runs the escrow; each machine removes and adds only its own player's gold, items and tomes (see "Swap meet") |
| Tide Totems | host places; first to ask wakes it | `E` sends a request; the host latches every networked orb and coin to the asker and tells everyone the totem sleeps (see "Tide Totems") |
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
  Their orbs go to the next nearest player. The host leaving mid-run hands
  the run to another player (see "Host migration and late join"); only in
  the lobby does it send the others back to the CO-OP page with "The host
  left the game". A run that was already over keeps its results screen.
  Players can join a run in progress (same section).

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
- **Lobby** (`SteamLobby`): HOST (Steam) creates a lobby with 4 slots,
  public or friends-only (the CO-OP page's **Lobby: Public / Invite only**,
  see "Public and invite-only lobbies"), sets `game=wavebreak` and
  `protocol=<Coop.Protocol>` (now `wavebreak-coop-3`: Hello carries the
  previous slot for host migration), and sets rich
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
  fifth player refused, leave, timeout and host leaving, then a survivor
  re-hosting on its own Steam id with another reconnecting to it (46 checks).

### Host migration and late join

`Net/NetSession.Migration.cs`, `NetSession.Snapshot.cs` and small
`*.Migration.cs` partials (`Enemy`, `EnemyManager`, `Pickup`, `ChestSpawner`,
`StageDirector`, `Inventory.CatchUp`).

**What a client already has.** Everything the host simulates is mirrored
on every client as puppets: every enemy (id, def, transform, health), orb
and chest, plus the map, the boss, the portal and the rest room (the phase
flow is replayed everywhere). What only the host knows it streams once a
second in a reliable **run snapshot** (`RunSnap`, 17 floats plus, on ENet,
each peer's address): the next free enemy, orb and chest ids; the enemy
director's schedule (credits, next elite wave, wave end, spawn timer); the
phase machine's private timers (phase index and state, boss arrival time,
portal and rest-room countdowns, Final Swarm clock, endless loop flag and
next map); the endless loop count. The scoreboard already syncs at 1 Hz.

**Election.** Deterministic and silent: the surviving player with the
lowest lobby slot (slots are unique; every survivor holds the same roster,
so all pick the same one). Peer ids are not stable (the new host must be
peer 1, and reconnecting clients get new ids), so slots are the identity:
avatars, roster entries and scoreboard lines are re-keyed by slot
(`RemotePlayer.PeerId` is settable, `RunScoreboard.Rekey`). The old host's
line stays under a negative "departed" id (`Coop.Departed`, also sent with
the roster). On Steam the lobby member that owns the lobby after Steam's
own hand-over passes ownership to the elected host
(`SteamLobby.HandOwnershipTo`), so lobby data, joinability and invites stay
with whoever hosts.

**The new host** (on `ServerDisconnected` mid-run): closes its client peer,
opens a host peer (ENet: the session's port + 1, the pre-agreed fallback
port, so a second migration uses + 2; Steam: a `SteamPeer` host in the same
lobby), becomes peer 1, promotes every puppet to a real enemy
(`Enemy.PromoteFromPuppet`: contact damage and speed recomputed from the
director's current multipliers, health kept; a puppet the player had
predicted dead dies for real), makes pending orbs collectable, learns the
map's obstacles for chest placement, adopts the director and phase timers
from the last snapshot (the boss puppet stays the phase boss; if it is
gone it is sent again), and keeps each other survivor's slot open for
`RejoinWindow` (20 s) before dropping it like a leaver.

**The others** stop sending (their own player keeps moving; puppets stand),
wait a second, then connect to the new host (ENet: its address as the old
host reported it, on port + 1, retrying until the window closes; Steam: its
Steam id) and say Hello with their previous slot. The new host re-keys
them and sends a **world snapshot** (`WorldSnap`: phase index and state,
clock, boss id, portal and swarm timers, endless loop, the host's
position, every enemy, orb and chest with its open flag, and the run
snapshot). The client lines its puppets up against it: matching ids are
kept and corrected, the rest despawned, the missing spawned. Everyone sees
the toast "Host left — Guest is now the host" (`UI/CoopToast`). The other
survivor is back in sync a second or two after the host quit (the test
prints it as `resync_s`); no enemy, orb, chest, phase step or second of run
clock is lost.

**Fallback.** When migration is impossible (the elected host cannot open
its port, a survivor cannot reach the new host within the window, Steam
cannot name the new host, or the host leaves before the run's Go), that
machine ends its run (`RunState.Finish`, so the results screen shows and
Pearls, records and quests are saved as on any run end), shows the
message, and closes the session. Never a crash or a silent return to the
menu. `--coop-no-migrate` forces this path for the test.

**Late join.** The lobby stays joinable during the run (`SetJoinable(false)`
at run start is gone). A Hello mid-run from a new player takes a free slot
(4 max; refused during the victory beat or after the run ended) and gets
`JoinRun` (seed, difficulty). It loads the run scene, says Loaded, and the
host sends it the world snapshot and its own `Go`; nobody else waits. The
others get the roster and add its avatar (`SyncRemotes` now adds as well as
removes, matched by slot). The newcomer catches up with the host's map or
rest room (`StageDirector.CatchUpMap/CatchUpState`, the same replay calls a
client uses), is placed next to the host, and is levelled to the **team's
average level** (rounded down, from the scoreboard) with every card picked
by the player's own auto-pick rule (`Main.CatchUpPicks`,
`Autopilot.PickByRule`): a fresh level-1 character would be dead weight in
a late phase, and a minute of card menus mid-fight is worse.

### Public and invite-only lobbies

The CO-OP page's **Lobby** row (`UI/CoopPage.Browse.cs`) picks Public or
Invite only before HOST; the choice is kept in `settings.cfg` `[coop]
lobby_public` (`Net/LobbyVisibility`). Invite only is the old friends-only
lobby (friends you invite, or who see you in a game). Public is
`k_ELobbyTypePublic` with lobby data `vis=public`, `host`, `diff` and
`state` (`lobby`, `map:2/3` or `rest:2/3`), kept current by the host
(`SteamLobby.PublishState`, with the roster and once a second in a run).
**BROWSE PUBLIC GAMES** asks Steam for lobbies with game, protocol and
`vis` matching, worldwide (`SteamLobby.RequestPublicList`, on opening the
page, REFRESH, and every 15 s). `Net/PublicGames` filters (ours, this
protocol, public, not full, not empty, not our own, no duplicates), orders
(lobbies not started first, then fuller games) and formats each row: host,
players x/4, difficulty, "In the lobby" / "Map 2/3" / "Resting after map
1/4", JOIN. Both kinds of lobby accept players during the run.
`--check-browse` tests the filter and the format without Steam;
`--fake-browse` fills the list for captures. When the panel grows past the
beach line-up's room, line-up name tags hide rather than cross the panel.

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
reason on failure. Host migration and late join (`-Only <names>` runs a
subset):

- `coop-hostleft`: two players, the host quits at 15 s (`--coop-quit-at`,
  which disconnects its peers at once); the guest must take over as the
  host of its own session and play on (`--coop-expect-migration`:
  migrated=1, enemies moved after it, the clock ran 15 s more, the toast).
- `coop-fallback`: the same with `--coop-no-migrate`; the guest's run must
  end on the results screen with the session closed.
- `coop-migrate`: three processes. The host quits at 20 s; GuestA (lowest
  slot) becomes the host on port + 1 and GuestB reconnects. Both must report
  migrated=1 with the same new_host, enemy travel after the migration
  (real AI on A, puppets on B), more than 15 s of clock after it, and map 2
  reached through the new host's boss, portal and rest room
  (`--coop-expect-phase=2`); A must have the rejoin, B a resync and enemy
  states from A. Screenshots `coop-migrate-a/b.png` and the toast
  `coop-migrate-toast-a/b.png`.
- `coop-latejoin`: host and GuestA start (map 1's portal opens at 12 s);
  the host writes a mark file at 18 s of run clock (after the rest room) (`--coop-mark-file/--coop-mark-at`) and
  the script starts a third process then, which joins about 30 s into the
  run (`--coop-late-join`): it must apply the world snapshot, be on the
  host's phase, be at least level 2 (caught up), and play (hits, confirmed
  kills, enemy states); the host must count the late join
  (`--coop-expect-late-join`). Screenshot `coop-latejoin-late.png`.

Screenshots go to `tests/shots/coop-*.png`.
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
  Under the markers it draws the explored map, and it hides enemies, chests
  and shrines that stand in unexplored fog (see "Fog of war"); on maps with
  zones the zone's name sits under the disc.
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
- A player who drops (not the host) cannot reconnect into the same slot;
  they come back as a late joiner, at the team's level but with a fresh
  build. Only the host's departure is migrated.
- Host migration over Steam is untested live (the ENet path is tested with
  three processes; the Steam transport's re-host is in `--steam-selftest`).
  The ENet fallback port (+1 per migration) must be open on LAN hosts.
- After a migration, state that lived only on the old host's machine and
  was not in the snapshot restarts: enemy wind-ups and statuses in
  progress, a boss's enrage phase, the director's spawn grid. A late joiner's build is auto-picked, not chosen.
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
(`config/version`, the exe's version resources and the installer) is 1.3.2.

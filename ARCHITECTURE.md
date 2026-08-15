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
| `RunState.TargetSeconds` | 900 | run length; 15 min is long enough for a build to form, short enough to retry |

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

## Layout

```
scripts/
  Core/        EventBus, GameEvents, Rng, Pool
  Stats/       Stat, StatSheet          <- the pipeline
  Combat/      DamageInfo, WeaponSystem, WeaponRuntime, Projectile
  Actors/      Player, PlayerHealth, PlayerCamera, Enemy, EnemyManager, Pickup
  Progression/ XpCurve, Inventory, OfferPool
  UI/          Hud, LevelUpPanel, ResultsScreen, DamagePopup
  Data/        Definitions, Catalog     <- all content
  Game/        Main, Arena, RunState, Screenshot
```

`Game/Main.cs` constructs the systems and ticks them **in one place** rather than
letting each run its own `_Process`. That ordering is deliberate: with
independent callbacks, whether weapons fire before or after enemies move depends
on scene-tree order, so hit registration changes when you reparent a node.

# Wavebreak 1.6.0

## What changed and why

**Puzzles.** The two-player room (one player on every plate) is removed, so every
room can be solved alone or by a team. Room kinds are one-file plug-ins in
`scripts/Game/Puzzle/Kinds/` (see "How to add a puzzle" in ARCHITECTURE.md). Five
new kinds were added: Ice Hall, Aqueduct Room, Ember Hall, Counting Room and
Lantern Path. A seeded shuffle bag shows every kind before one repeats. PUZZLES
ONLY (Puzzle Gauntlet) runs six puzzle maps of rising difficulty and saves the
best medals; it is on the PLAY page and in the co-op lobby.

**Reef currents.** Lumenreef Basin spawns currents, level or vertical, from the
run seed. Each shows a 1.5 s telegraph, pushes, fades and dies. A swimmer inside
one is carried by an eased drift added to their own swim velocity
(`ReefCurrentField`, `Player.Currents`). Currents do not push enemies or pickups.

**Distinct weapons.** 19 look-alike groups (57 weapons) got their own delivery
pattern, shape, impact and firing sound, with damage per second kept in range
(`docs/weapon-audit.md`, `--check-distinct`). Descriptions were rewritten in
English and Arabic.

**Fixes.** The Reef Leviathan uses measured pitch, roll and yaw
(`ModelSpec.SceneBasis`) and its own albedo. Cave sharks face forward. Health bars
skip burrowed (invisible) enemies. Puzzle room outlines are clipped to the
minimap circle. A phase boss more than 25 m past the arena edge is returned to
the spawn ring (`EnemyManager.ReturnStrayBoss`). One run lost Thresher Prime
1.7 km away, and the cause was not reproduced; the log line
`boss was off the map` marks a recurrence.

Co-op protocol is `wavebreak-coop-17`.

## Verification

- C# build passed. Content audit passed: 24,952 checks in English and 25,036 in Arabic.
- Full regression suite (`suite16`) passed: guards, pause, graphics, chests,
  boundary, dev panel, rest room, trade, totem, updater, Steam self-test, phases,
  perks (3 seeds), puzzles (3 seeds), puzzle run, gauntlet, currents, Leviathan,
  guardian, distinct, smoke (3 seeds), co-op (default set) and expeditions.
- The first full-run attempt stalled on the stray boss. Two reruns with the fix
  (seeds 7 and 3) reached the final swarm with no exceptions.
- `tests/verify-release-package.ps1` passed for 193 files.

## Artifacts

Portable ZIP: 97,680,774 bytes; SHA-256 `11d4389e2a81de57fa43ca2afb70929ba82d84fe571ebb83f7e3965b43aace6b`.

Installer: 75,871,794 bytes; SHA-256 `7ef5f90e653588820f9fb3cba341f853b6805ef862228348cb0c4f2bece89dd4`.

Known limits: the puzzle bot always earns gold, so it does not measure
difficulty; the co-op guest's gauntlet results screen is untested; character
passive rings still look alike.

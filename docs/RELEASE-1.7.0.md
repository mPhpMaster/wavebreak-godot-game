# Wavebreak 1.7.0

## What changed and why

**Hard puzzles.** Players found every room too easy, even at high difficulty.
Each kind in `scripts/Game/Puzzle/Kinds/` was rebuilt with more rules:
chained braziers under a fuse, ordered plates between gusts, two lamps with
splitters and linked mirrors, a 7x7 aqueduct with leaks and bolted pipes,
numbered ice sockets, iron crates, padlocked levers, clue tablets, limited
scale moves and a fenced lantern path with a stall timer. The difficulty
curve is 0, 2, 3, 4, 5, 5 and puzzle maps hold three to five rooms.

**Puzzle boons.** Each solved room gives every player a boon (`PuzzleBoons`):
speed, an air jump, jump height, dash cooldown, health and a heal, or armour.
Boons stack until the puzzle map or the gauntlet ends.

Co-op protocol is `wavebreak-coop-18`: the same seed builds different rooms
than 1.6.0.

## Verification

- C# build passed. Content audit passed (English 25,164 checks; Arabic 25,272
  after the release notes).
- Suite `suite17` passed: puzzles (seeds 1, 7, 42), boons, gauntlet (seeds 7
  and 3), puzzle run, pause, guards, pad menu, currents, distinct, full run,
  smoke (3 seeds) and co-op (including the co-op puzzle with a late joiner).
- `tests/verify-release-package.ps1` passed for 193 files.

## Artifacts

Portable ZIP: 97,726,334 bytes; SHA-256 `5109814ca92c38b349d33481eab8c6136e75c9fa943ee4d63cc7742ebd937399`.

Installer: 75,914,908 bytes; SHA-256 `d45a2f37540c711269c0797fe8410236db0843d65688a7923454a4454186605e`.

Known limits: the puzzle bot always earns gold, so it does not measure
difficulty; crate generation can take about 280 ms at the top difficulty.

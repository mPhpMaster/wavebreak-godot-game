## Changes

- Five new puzzle rooms: Ice Hall (glide), Aqueduct Room (pipes), Ember Hall (braziers), Counting Room (scales) and Lantern Path (trail). A run shows every room kind before one repeats.
- The room that needed one player standing on every plate is gone. Every puzzle can be solved by one player or by the whole team.
- Puzzle room kinds are now one-file plug-ins under `scripts/Game/Puzzle/Kinds/`; see "How to add a puzzle" in ARCHITECTURE.md.
- New PUZZLES ONLY mode (Puzzle Gauntlet) on the PLAY page and in the CO-OP lobby: six puzzle maps in a row, harder each time, ending with medals. Best medals are saved.
- Lumenreef Basin has water currents that appear and vanish at random, level or vertical, and carry you along when you swim into them. They come from the run seed, so co-op players see the same currents.
- Weapons that looked or played alike now differ: 19 look-alike groups got their own pattern, projectile shape, impact and firing sound, with balance kept (see `docs/weapon-audit.md`). Descriptions were rewritten to match, in English and Arabic.
- The Reef Leviathan swims upright with its own colours, and cave sharks swim head first.
- No health bars float over hidden burrowing enemies, and puzzle rooms stay inside the minimap circle.
- A phase boss thrown far off the map is brought back near the players instead of walking back for minutes.
- Co-op protocol 17: update every player together.

## Downloads

- `Wavebreak-Setup-1.6.0.exe`: Windows installer.
- `Wavebreak-1.6.0-windows.zip`: portable Windows build.
- `SHA256SUMS.txt`: checksums.

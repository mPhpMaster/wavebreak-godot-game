# Wavebreak

A 3D survivors-like made in **Godot 4.7.1 (.NET / C#)**. You wash up on a
shore with one weapon. Then you run, jump, dash, slide and slam through
waves of cute monsters, build up weapons, items and tomes, beat a boss on
each of three maps, and hold the line through the Final Swarm.

![Gameplay](docs/screenshots/gameplay.png)

## Download and install

Get the latest build from [Releases](https://github.com/mPhpMaster/wavebreak-godot-game/releases).

- **Installer:** `Wavebreak-Setup-<version>.exe`
  - Installs for your user by default and adds a Start-menu shortcut, plus an optional desktop one.
  - Removable from *Add or remove programs*.
  - It is unsigned, so Windows SmartScreen asks once: *More info → Run anyway*.
- **Portable:** `Wavebreak-<version>-windows.zip`. Unzip anywhere and run `Wavebreak.exe`.

Saves, settings and unlocks live in `%APPDATA%\Godot\app_userdata\Wavebreak`.

## What's in it

| | |
|---|---|
| Maps | 5: Thornmeadow, Rimeglass Plateau, Cinder Reach, Gravemoor, Saltglass Shallows. Each has its own enemies, hazards, boss and music; routes pick three per run |
| Weapons / items / tomes | 30 / 98 / 26, with elements, status effects, megacrits and upgrade rarities |
| Enemies | 53, including phase bosses with move sets |
| Characters | 21 with passives, 133 skins, 22 hats |
| Quests | 303 (18 secret), which unlock characters, weapons, items, skins and Pearls |
| Meta | Pearls, the Pearl Shop, difficulty tiers, 12 global modifiers, 55 map challenges, the Toggler, leaderboard |
| Run events | Charge, challenge, statue, greed, balance and wishing-well shrines; merchant; microwave; pots; chests |
| Level-up | Three cards with icons; reroll (R), banish (B) and skip (X) |
| Music | 19 procedurally composed tracks; the Boombox lets you play, pin or shuffle them |
| Co-op | Online, 2-4 players, direct IP (see [Co-op](#co-op-2-4-players)) |
| Languages | English and Arabic (full right-to-left layout) |
| Settings | Full graphics page (presets, display, scaling/FSR, AA, shadows, effects, FOV, shake, UI scale, weapon-effect opacity, FPS counter), audio, key bindings |

## Co-op (2-4 players)

Title screen → **CO-OP**.

1. **Host:** pick your character and skin with the arrows, enter a port
   (default `7777`) and press **HOST**. Tell the others your address.
2. **Join:** enter the host's address as `IP:port` (for example
   `192.168.1.20:7777`) and press **JOIN**, then **READY**.
3. The lobby lists everyone with their character and skin. When all are
   ready, the host presses **START**.

**Where to connect.** It is a direct connection, with no account and no
server in between:

- **Same network (LAN):** join the host's local IP (`ipconfig` on the host
  shows it, usually `192.168.x.x`).
- **Over the internet:** the host forwards the port (UDP, default 7777) on
  their router to their PC and gives out their public IP, **or** everyone
  installs the same free virtual LAN, such as [ZeroTier](https://www.zerotier.com/)
  or [Tailscale](https://tailscale.com/), and joins the host's address on it.
  No port forwarding is needed then.
- Windows may ask the host to allow Wavebreak through the firewall the first
  time. Allow it on private networks.

**How it plays.**

- Everyone keeps their own character, weapons, level-ups, tomes and items.
- Experience is shared: every orb anyone picks up fills everyone's bar.
  Gold and med-packs go to whoever picks them up.
- Chests are shared. The first player to open one gets it, and pays with
  their own gold.
- Shrines, merchants, the microwave and pots are each player's own.
- A level-up or the pause menu does not stop the game for the others. Your
  character stands still while it is open, and you cannot be hurt for the
  first 8 seconds.
- At 0 health you go **down**. A teammate who stands next to you for 4
  seconds revives you with 40% health. The run ends when everyone is down.
- Enemies chase the nearest player who is still up. The camera follows you,
  and names show over the other players' heads.
- If the host leaves, everyone returns to the CO-OP page with a message. If
  someone else leaves, their character is removed and the run goes on.
- You cannot join a run that has already started. Co-op always uses the
  Classic route, with no challenges. The host's difficulty applies to everyone.

| | |
|---|---|
| ![Co-op lobby](docs/screenshots/coop-lobby.png) | ![Co-op run](docs/screenshots/coop-run.png) |
| Lobby: players, characters, skins, ready | Two players in a run, name tags and team panel |
| ![Revive](docs/screenshots/coop-revive.png) | ![Arabic co-op lobby](docs/screenshots/arabic-coop-lobby.png) |
| A downed teammate and their revive ring | Arabic: co-op lobby |

## Screenshots

| | |
|---|---|
| ![Title](docs/screenshots/menu.png) | ![Character select](docs/screenshots/character-select.png) |
| Title screen | Character select: skins, hats, route, difficulty |
| ![Level up](docs/screenshots/level-up.png) | ![Boss](docs/screenshots/boss.png) |
| Level-up cards with reroll / banish / skip | Phase boss |
| ![Cinder Reach](docs/screenshots/gameplay-cinder.png) | ![Pause sheet](docs/screenshots/pause-sheet.png) |
| Cinder Reach, a later map | Pause menu and character sheet (scrolls for big builds) |
| ![Pearl shop](docs/screenshots/pearl-shop.png) | ![Codex](docs/screenshots/codex.png) |
| Pearl Shop | Unlocks and secrets |
| ![Boombox](docs/screenshots/boombox.png) | ![Graphics](docs/screenshots/graphics.png) |
| Boombox | Graphics settings |
| ![Arabic title](docs/screenshots/arabic-menu.png) | ![Arabic character select](docs/screenshots/arabic-character-select.png) |
| Arabic: title | Arabic: character select |
| ![Arabic pause](docs/screenshots/arabic-pause-sheet.png) | |
| Arabic: pause sheet | |

## Controls

| Key | Action |
|---|---|
| Mouse | look |
| W A S D | move |
| Space | jump / double jump |
| Shift, right mouse | dash |
| Ctrl | slide; in the air, slam |
| E, left mouse | interact |
| Tab | hold for the character sheet |
| Esc / P | pause menu (Resume, Settings, End Run, Restart, Main menu, Quit; in co-op, Leave Game instead of End Run and Restart) |
| M, `[` `]`, `-` `=`, `9` `0` | mute, music volume, master volume, effects volume |

All keys can be rebound in Settings → Key bindings. The game pauses itself
when its window loses focus.

## Developer panel

**Ctrl+Shift+Alt+F12**, on the title screen or in a run, opens a hidden
panel. It can unlock everything, give Pearls, edit your build, spawn anything
and jump between stages. A run that uses it does not count toward quests,
stats or the leaderboard.

## Building from source

Requirements: Godot 4.7.1 .NET and the .NET 8 SDK.

```bash
dotnet build
Godot_v4.7.1-stable_mono_win64.exe --path .
```

Release and installer:

```bash
Godot_v4.7.1-stable_mono_win64_console.exe --headless --path . --export-release "Windows Desktop" build/windows/Wavebreak.exe
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/Wavebreak.iss
```

The test flags, self-checks and how the code is organised are in
[ARCHITECTURE.md](ARCHITECTURE.md). Third-party assets and their licences are
in [assets/CREDITS.md](assets/CREDITS.md).

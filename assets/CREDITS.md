# Third-party assets

All third-party art in `assets/` is public domain (CC0 1.0). Credit is not
required by the licence; it is given anyway. Each pack folder keeps its
original `License.txt` where one was supplied. Only the models the game uses
were copied in.

| Folder / asset | Pack | Author | Source | Licence |
|---|---|---|---|---|
| `kenney/mini-characters` | Mini Characters | Kenney | https://kenney.nl/assets/mini-characters | CC0 1.0 |
| `kenney/blocky-characters` | Blocky Characters | Kenney | https://kenney.nl/assets/blocky-characters | CC0 1.0 |
| `kenney/graveyard-kit` | Graveyard Kit | Kenney | https://kenney.nl/assets/graveyard-kit | CC0 1.0 |
| `kenney/cube-pets` | Cube Pets | Kenney | https://kenney.nl/assets/cube-pets | CC0 1.0 |
| `kenney/nature-kit` | Nature Kit | Kenney | https://kenney.nl/assets/nature-kit | CC0 1.0 |
| `kenney/mini-dungeon/chest.glb`, `kenney/mini-dungeon/Textures/colormap.png` | Mini Dungeon | Kenney | https://kenney.nl/assets/mini-dungeon | CC0 1.0 |
| `quaternius/{blob,flying,big}` | Ultimate Monsters | Quaternius | https://quaternius.com | CC0 1.0 |
| `quaternius/flying/{Pigeon,Glub,Glub_Evolved,Hywirl,Goleling,Goleling_Evolved,Tribal,Armabee_Evolved,Squidle,Dragon}.gltf`, `quaternius/big/{Ninja,Monkroose,Tribal}.gltf`, `quaternius/blob/Wizard.gltf` (enemy expansion) | Ultimate Monsters | Quaternius | https://quaternius.com | CC0 1.0 |
| `icons/{sword,shield,heart,heart_broken,clover,flame,skull,bow,crown,potion,hourglass,pouch,tower,burst,campfire,award,dice,lock}.png` | Board Game Icons | Kenney | https://kenney.nl/assets/board-game-icons | CC0 1.0 |
| `icons/{target,star,fastforward,gear,expand,question,wrench,up,next}.png` | Game Icons | Kenney | https://kenney.nl/assets/game-icons | CC0 1.0 |

The heroes trio (Sunny, Biscuit, Flick) wear no third-party art: their
bodies are built in code from primitives (`scripts/Visuals/HeroModels.cs`)
and are original to this project, under the project licence (CC BY-NC 4.0,
see `LICENSE`) like the music below.

The Kenney icons above were re-rendered (cropped, rescaled to 128 px, given a
dark outline) by `tools/make_icons.py`. Every other PNG in `assets/icons/` is an
original glyph drawn by that script and is under the project licence
(CC BY-NC 4.0) like the music below.

CC0 1.0 (the Kenney and Quaternius packs): https://creativecommons.org/publicdomain/zero/1.0/

## Music

Every file in `assets/music/` is original to this project: the notes were composed
for it and rendered with no samples by `tools/compose_music.py`, which uses
numpy/scipy synthesis and ffmpeg/libvorbis. It is © 2026 Mohammad Al-Safadi
(sensei / hlaCk) under the project licence, CC BY-NC 4.0 (see `LICENSE`). To change a track, edit the script and re-run
`python tools/compose_music.py [--only name]`.

| File | Use | Key / tempo | Length |
|---|---|---|---|
| `title.ogg` | title menu (original music-box waltz rewrite) | D harmonic minor, 3/4, 96 bpm | 60 s |
| `meadow.ogg` | thornmeadow (loop) | G major, 104 bpm | 74 s |
| `frost.ogg` | rimeglass (loop) | D dorian, 84 bpm | 69 s |
| `ashlands.ogg` | cinder (loop) | A minor, 96 bpm | 60 s |
| `graveyard.ogg` | graveyard map (loop) | E harmonic minor, 3/4, 96 bpm | 60 s |
| `tidal.ogg` | tidal map (loop) | F lydian, 90 bpm | 64 s |
| `boss.ogg` | phase boss (loop) | C minor, 124 bpm | 46 s |
| `swarm.ogg` | Final Swarm (loop) | D minor, 136 bpm | 56 s |
| `victory.ogg` | win sting | C major | 7.5 s |
| `defeat.ogg` | defeat sting | A minor | 10 s |
| `lounge.ogg` | menu / Boombox (loop) | F major, 84 bpm | 46 s |
| `meadow_dusk.ogg` | Thornmeadow remix (loop) | C major pentatonic, 112 bpm | 51 s |
| `frost_aurora.ogg` | Rimeglass remix (loop) | E lydian, 92 bpm | 42 s |
| `ashlands_forge.ogg` | Cinder Reach remix (loop) | D dorian, 108 bpm | 53 s |
| `graveyard_waltz.ogg` | Gravemoor remix (loop) | G harmonic minor, 3/4, 138 bpm | 42 s |
| `tidal_lagoon.ogg` | Saltglass Shallows remix (loop) | Bb major, 100 bpm | 38 s |
| `boss_final.ogg` | final boss (loop) | E harmonic minor, 132 bpm | 44 s |
| `shop.ogg` | merchant jingle | G major | 5.6 s |
| `fanfare.ogg` | run won sting | F major | 10 s |

Which track plays where, and how each unlocks in the Boombox, is
`scripts/Audio/MusicTracks.cs`.

The 2026-10-28 title rewrite uses the project's sample-free synthesizer and an
original authored melody. Reproduce it with `tools/compose_title_waltz.py` after
rendering the base soundtrack. Its source and output follow the original music
licence above. Other map tracks were not replaced. Technical audio measurements
are recorded in `docs/TITLE-WALTZ-AUDIO.json`; human listening is not claimed.

## Fonts

| File | Font | Author | Source | Licence |
|---|---|---|---|---|
| `fonts/NotoSansArabicUI-SemiBold.ttf` | Noto Sans Arabic UI SemiBold (Arabic fallback for the default font) | The Noto Project Authors | https://github.com/notofonts/arabic | SIL Open Font License 1.1 (`fonts/OFL.txt`) |

## Reef pickups: Kenney Platformer Kit 4.1

- Author: Kenney (https://kenney.nl). Source: https://kenney.nl/assets/platformer-kit
- License: CC0 1.0, bundled in `kenney/reef-pickups/License.txt`.
- Files: `jewel.glb` (XP), `coin-gold.glb` (gold), original `Textures/colormap.png`.
- Retrieved 2026-10-27 from the official free download; no payment or bundled software.

## Steam

| File | What | Owner | Licence |
|---|---|---|---|
| `thirdparty/steamworks/win64/steam_api64.dll` | Steamworks API runtime (co-op lobbies) | Valve Corporation | Steamworks SDK Access Agreement; redistributed with the game, not covered by this repository's licence |

### Animated cave guardian

`models/quaternius/animated-fish/Shark.fbx`: Animated Fish Pack by Quaternius,
CC0-1.0. Author upload: https://opengameart.org/content/animated-fish ;
official pack: https://quaternius.com/packs/animatedfish.html .
Original License.txt and download provenance accompany the model.

### Animated Reef Leviathan

Sea Levithan, Rigged, Animated, Free by wErT (@wErTT), CC BY 4.0.
Source: https://sketchfab.com/3d-models/sea-levithan-rigged-animated-free-d8bdc555c8b2472d9a2058dd55036c3d
License: https://creativecommons.org/licenses/by/4.0/ .
Modifications: normalized/oriented source geometry with an original looping
swimming morph animation; incompatible source rig omitted and palette lifted
for underwater readability. Full provenance/modifications are in ATTRIBUTION.txt.


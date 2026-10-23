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

The Kenney icons above were re-rendered (cropped, rescaled to 128 px, given a
dark outline) by `tools/make_icons.py`. Every other PNG in `assets/icons/` is an
original glyph drawn by that script and is CC0 1.0 like the music below.

CC0 1.0: https://creativecommons.org/publicdomain/zero/1.0/

## Music

Every file in `assets/music/` is original to this project: the notes were composed
for it and rendered with no samples by `tools/compose_music.py`, which uses
numpy/scipy synthesis and ffmpeg/libvorbis. It is dedicated to the public domain
under CC0 1.0, like the art above. To change a track, edit the script and re-run
`python tools/compose_music.py [--only name]`.

| File | Use | Key / tempo | Length |
|---|---|---|---|
| `title.ogg` | title menu (loop) | D major, 80 bpm | 48 s |
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

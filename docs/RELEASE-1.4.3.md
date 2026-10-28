# Wavebreak 1.4.3 release verification

This release carries the authorized expedition polish. Version 1.4.2 remains the
published baseline until the final 1.4.3 assets have been uploaded and checked.

Implemented: staged loading and model prewarming; temporary F2-skip/F1-replay
movement practice; objectives; rooftop/giant/wall hooks with traveling tips,
original launch audio, toggle/hold settings and Jump/Dash boosting; original
medieval city facades and sloped roofs; very slow, solid giants and visible wounds;
twin swords; six-pellet shotgun with recoil/audio/bubbles; steerable decaying swim
dash; detailed wreck and cave route markers; licensed shark and Leviathan visuals;
global imported XP/gold models and deterministic collectible resources; rest
combat suppression; ramp pursuit; living-player targeting and averaged safe
spawns; per-render enemy-health projection; expanded Saltglass bounds; replicated
DPS/healing/down/death scoreboard with Tab; corrected Classic/Reef route order.

## Evidence already obtained

- Rooftops, independent hooks, moving body/boundary attachments, hold/toggle and
  real ENet city-hook replication passed targeted real-engine checks.
- Full and compact co-op upgrade layouts passed actual two-process movement,
  non-pausing, selection and rest-message overlap checks.
- Three real ENet processes passed living-player/3D targeting, averaged spawn
  center, downed-peer exclusion and damage/DPS/healing/down replication checks:
  `%TEMP%/wavebreak-coop-polish-20261028-091351`.
- Resource checks passed collection/no-duplicate/deterministic rebuild/imported
  shared geometry across every map. Rest suppression check is being strengthened
  to require active combat projectiles before entering rest.
- Steering check exposed an exact-opposite-direction normalization trap. Replaced
  with bounded angular steering; real left/right burst steering and decay now pass.
- Swim-pose fixture was isolated from live weapon recoil and enemy interference;
  the existing forward/up/down reinforcement and pose acceptance now passes.
- A ground enemy physically routed around a platform, climbed its ramp and reached
  the player target in PlatformPursuitCheck.
- Staged loading completed 54 real steps, removed its overlay, resumed gameplay
  and started Thornmeadow in StartupCheck. Model rendering prewarming is included
  in the final rebuild.
- Content audit passed 21,769 assertions after the expedition weapon changes.

## Final acceptance limits

Human new-player sessions, manual listening, live Steam and installation over a
real player's existing copy remain unverified. The playtest protocols are prepared.
No third-party outreach or changes to the user's live game session were performed.

Final combined regression, EN/AR rendering, package hashes and publication checks
are recorded below when complete; the initial 1.4.3 build predates the final dash
fix and must not be uploaded.

## Final combined results

- Content audit: 21,925 checks, `%TEMP%/wavebreak-expeditions-20261028-092722/content-audit.log`.
- All-map resources, active-projectile rest cleanup, weapons, steering, platforms,
  hook controls/targets, guardian, Leviathan, city, rooftops, dual hooks, classic
  routing, notes, menus, swimming/pose, cave UI and Reef systems passed in that run.
- The legacy marine-prefix assertion was updated to verify the actual imported
  animated Leviathan instead of requiring a procedural-only model ID. Full Reef
  expedition/collision/terrain-cache acceptance then passed at
  `%TEMP%/wavebreak-expeditions-20261028-094102/reef-expedition.log`.
- Receiver interpolation could make a moving target occlude its own verified
  body-local hook anchor. Validation now checks the target surface and range,
  excludes that target from its own LOS ray, and continues blocking all other
  geometry. Both real peers pass at `%TEMP%/wavebreak-expeditions-20261028-094710`.
- EN/AR destination/character menus and city screenshots passed/reviewed:
  `%TEMP%/wavebreak-expeditions-20261028-094503`.
- Scoreboard EN/AR images: `%TEMP%/wavebreak-scoreboard-1.4.3-en.png` and `-ar.png`.
- Retained-quality tour: `%TEMP%/wavebreak-performance-20261028-092934`, scale=1,
  density=1, 1920px viewport; sampled city/Reef mean 6–9 ms. This is a local tour,
  not a hardware-independent or full-crowd performance guarantee.
- Real temporary updater helper: 28 checks passed (apply, rollback, cancellation,
  timeout, invalid hash, preservation). `%TEMP%/wavebreak-updater-1.4.3-fixed.log`.
  The helper uses managed SHA-256 to avoid inherited PowerShell module-path issues.
- Isolated export: `build/release-1.4.3`. Final managed library is rebuilt using
  ExportRelease/win-x64, checked against unchanged runtime dependencies, copied
  into that export and repackaged after the final network/test correction.
- Installer compression changed from ultra64 to max: same payload, lower build
  memory/time. Both versions' own source paths remain supported by build.ps1.
- Exported game binary passed bundled notes, Classic-after-Reef start, steering
  and all-map resources/rest checks using the isolated exported executable.
- Windows export preset metadata is now synchronized by build.ps1 from the
  project version, so ProductVersion/FileVersion also report 1.4.3.0.


## Published and verified

Release: https://github.com/mPhpMaster/wavebreak-godot-game/releases/tag/v1.4.3

Code/tag commit: `4417ab63dc6c228359ab636bbaf0312b49f74dce`.
Both `main` and `codex/expedition-polish` were pushed. A documentation-only commit
may follow the release tag to record this verification.

| Asset | Bytes | SHA-256 |
|---|---:|---|
| Wavebreak-1.4.3-windows.zip | 97164930 | 06f01239ed252de3dd601bebe8e236d3b82c60eef961c1bfd8df1c093a54d578 |
| Wavebreak-Setup-1.4.3.exe | 75658864 | 9bd43906c878efe62aa78111e0f6a400af2f75718363a62ffa79a497b1ed0f02 |
| SHA256SUMS.txt | 188 | f40ad7d2083bdbc88f5f47cd04c17aa1dcb84af7e462cc25a5f1af13272421d2 |

All three GitHub-uploaded asset sizes and server SHA-256 digests match local files.
The public installer and portable download URLs return HTTP 200. GitHub's latest
release endpoint reports v1.4.3, draft=false and prerelease=false. The portable
manifest validates every one of 191 files and Windows ProductVersion 1.4.3.0;
embedded helper contents/BOM/parser checks also pass.

The final staged export was rebuilt after synchronizing Windows metadata; it
includes the final dynamic-target validation fix. GPU startup/prewarming passes
54 real stages with no ERROR/SCRIPT ERROR, releases its overlay and resumes play.
Source evidence: `%TEMP%/wavebreak-loading-prewarm-final.log`.

The user's running game was not closed/restarted or replaced. Everyone joining
co-op must use the published 1.4.3 package (protocol wavebreak-coop-10).
Human listening/playtest, live Steam and installation over an actual player copy
remain the explicit acceptance limits recorded above.

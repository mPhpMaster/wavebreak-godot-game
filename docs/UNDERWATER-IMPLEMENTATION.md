# Lumenreef Basin implementation and validation

Updated 2026-10-27 after the explicitly authorized resume. Implementation is complete for the requested reef scope; automated verification and rendered review are recorded below. User listening/manual acceptance and live Steam testing are not claimed.

Worktree: C:/Users/Administrator/.codex/worktrees/6205/wavebreak-godot-game
Changes are uncommitted. Preserve new/untracked partial classes and assets.

## Implemented scope

- [x] Optional reef-dive route / lumenreef side map, preserving saved route ordering, the original six-map standard draw pool and ordinary map controls.
- [x] Deep continuous terrain collision, valleys/ridges, natural enclosing mountains, roughly 70 m central water depth, shared moving-wave surface and camera-directed 3D swimming.
- [x] Full-body swim pitch and strokes shared by local/remote players; fixed 12% speed reinforcement for upward-look plus rise / downward-look plus dive. Idle/opposed/both-held inputs do not stack boosts or change aim. No ordinary jump/slide/slam underwater.
- [x] Momentum breach, gravity/apex/fall, splash and reentry. Initial reef arrival starts 24 m above mean water with four seconds of damage protection; late join/respawn do not replay arrival. Above-water falling uses air animation.
- [x] BOTH short straight caves removed. One 261 m curved enclosed passage connects a perimeter cliff entrance to an interior seabed chimney. Two wider chambers, rock portal rims, actual clipped terrain openings, continuous tube collision, lighting/coral, two marine guards, one-time gold and a Gilded chest. Movement/fall recovery understands underground space.
- [x] Wreck landmark/chest/guardian retained. Added supported cliff ledges and their chests. Map/Cute/secret shrines now use real terrain foundations; statue/greed shrines occupy elevated ledges; chests/stations/totems retain physical support handling.
- [x] 52 useful resource nodes: crystals and reef herbs across seabed, elevated ledges, actual cliff faces and cave floor. Wall growth follows collision normals. Prompts show the bound interaction key and reward in English/Arabic. Crystals grant 6 XP or 4 gold; herbs release a 5% health pickup. Depleted growth shrinks and cannot pay twice.
- [x] Host validates resource ID, stage, living peer and 3D proximity. Reliable depletion and cave claims replicate; late joins receive state. Early depletion packets queue before physics initializes nodes. Pickup authority/XP sharing uses existing networking. Protocol is now wavebreak-coop-6; mixed older clients are rejected.
- [x] Every reef enemy model lookup, including ordinary/elite/guard/summon/event/boss paths, receives a map-scoped original marine silhouette (fish/eel/crab/jelly/ray). Other maps retain their models. Off-ground marine rigs paddle/undulate with vertical orientation; crawling only occurs on actual ground contact. Enemy pursuit stays submerged below the dynamic surface.
- [x] Repetitive pitched glug replaced with filtered-noise swish/bubble envelopes of varied cadence; splash has a soft noise attack/decay. Existing Sfx/master/mute routing, depth crossfade/filter, stronger movement/dash and quiet rest retained.
- [x] Ambient and wake bubbles clip to the moving waterline. Above-water splash droplets remain appropriate. Sky/sun, air/water transition and depth-dependent fog/ambient/sun lighting added. Fish/currents/wake/caustics/waves retained.
- [x] Official free Kenney Platformer Kit 4.1 jewel and gold-coin GLBs replace reef pickup placeholders. Gold falls and rests on actual terrain/ledge/cave collision; magnet, XP sharing and pooling retained. Models, texture and original CC0 license are in assets/kenney/reef-pickups; provenance is in assets/CREDITS.md.
- [x] Touched oversized classes split into partials, including Pickup/PickupField/PickupKind and ShrineSpawner placement/trials. Changed C# files remain below 500 lines.

## Verification

Latest dotnet build --no-restore succeeded. Two pre-existing warnings remain: CoopLineup.cs CS0108 (Ready hides Node.Ready) and RunScoreboard.cs CS8602.

Evidence directory: C:/Users/Administrator/AppData/Local/Temp

| Evidence | Result and scope |
| --- | --- |
| wavebreak-verified-swim.log | SWIM PASS: swimming, real terrain collision/relief, valley recovery, ordinary controller/jump restoration and reentry |
| wavebreak-verified-swim-pose.log | SWIMPOSE PASS: actual input-driven full-body level/up/down pose, bounded 12% reinforcement, idle/opposed cases, camera/capsule separation and no ordinary jump |
| wavebreak-verified-breach.log | BREACH PASS: directed look, slow surface treading, momentum breach/apex/gravity/reentry, air pose flags and resumed strokes |
| wavebreak-resume-reef-systems.log | REEF SYSTEMS PASS: new winding passage segments, one-time cave rewards/guards, wreck, currents, fish reactions, wake and objective audio routing/mixes |
| wavebreak-verified-reef-expedition.log | EXPEDITION PASS: body-clear cave collision along 261 m, separate entrance/exit, underground controller stability, actual upward exit traversal, supported ledges, harvest validation/depletion/wall distribution, safe gravity skyfall and gold settling |
| same expedition log | CONTENT AUDIT PASS: 21,328 checks, including route/content/localization and ordinary mechanics |
| wavebreak-verified-coop-host.log | COOP REEF PASS: real ENet host commits depletion |
| wavebreak-verified-coop-client.log | COOP REEF PASS: real initial client receives depletion, despite local classic route argument |
| wavebreak-verified-coop-late.log | COOP REEF PASS, late=True, initial_snapshot=True: third peer receives spent state after joining an active run |
| wavebreak-verified-render.log / wavebreak-final-reef-render.log | GPU-rendered hidden-desktop tours completed; reviewed cave entrance/interior/exit, grounded cliff shrine, marine models/pickups, swimming, above-water sky and visible sun; final render emitted no shader/runtime errors |

The first shortened late-join attempt failed because the test host quit before the third peer finished loading. The rerun used a longer host window and passed. An early capture build had a static-member reference error; corrected before successful builds. Earlier headless keyboard lookup diagnostics were fixed with a headless-safe display-name fallback; the final checks do not emit those errors.

Objective audio exports: wavebreak-reef-water-strokes.wav (12 s, peak 0.266, RMS 0.050) and wavebreak-reef-water-entry.wav (1.30 s, peak 0.275, RMS 0.033). These establish valid unclipped rendered audio, not subjective listening acceptance.

## Evidence limits / intentionally deferred work

- [x] Normal game-window behavior was observed in the direct follow-up below.
- [ ] Subjective audio listening and exhaustive user acceptance remain unverified; backend audio input is unavailable.
- [ ] Live Steam, full general co-op revive/migration and broad multiplayer acceptance remain deferred. The previous shortened general co-op client revive failure is not superseded by the narrower resource tests.
- [ ] Web canceled; Android/Roblox postponed. No release/export/deploy/commit/publish performed.

English UI remains the preference for future manual launches. Do not restart a running manual session merely to change language, and never terminate unrelated apps or the original project checkout.

## Commands and code map

Engine directory: D:/tools/godot/Godot_v4.7.1-stable_mono_win64

Manual executable: Godot_v4.7.1-stable_mono_win64.exe
Console executable: Godot_v4.7.1-stable_mono_win64_console.exe
Manual arguments: --path <this-worktree> --windowed --resolution 1280x720 -- --route=reef-dive --lang=en
Do not add automation/check/invulnerability flags to a manual acceptance session.

Focused flags: --check-swim, --check-swim-pose, --check-breach, --check-reef-systems, --check-reef-expedition; combine with --route=reef-dive --lang=en. --validate-content runs the audit. --coop-check-reef layers the resource check onto the existing ENet harness. GPU capture: --auto --invulnerable --reef-tour=<absolute-prefix>, using tests/run-offscreen.ps1 to avoid focus stealing.

- Movement: Player.Swimming, SwimmingNavigation, SwimmingPresentation, CharacterRig.Swimming, ReefArrival.
- Geometry: UnderwaterTerrain, ReefCavePath, ReefCaveGeometry, ReefLandmarks, ReefPlacement/ReefActorSupport/ReefFoundations.
- Harvest: ReefHarvest, ReefHarvestDef, ReefHarvestVisual, NetSession.ReefHarvest and snapshot integration.
- Marine models/animation: ReefMarineModels, CharacterRig.Marine, Enemy.Swimming and EnemyVisuals map override.
- Water/audio: ReefAtmosphere, UnderwaterDressing/Materials, SwimmerWake, ReefAudioSynth, ReefWaterAudio.
- Pickups: Pickup.Reef plus split Pickup/PickupField/PickupKind; assets/CREDITS.md and bundled license.

No memory files were changed. User restart pause was explicitly revoked before implementation resumed.

## End-of-run process state

All automated checks and capture processes started for this resume completed. Final process inventory found no Godot process with the verified/final capture markers. No manual game session was launched or terminated.

## Direct game-window follow-up

After the user requested completion of the remaining verification, launched the normal English reef session without autopilot, invulnerability or test flags. The live window showed swimming/strokes/wake, active marine combat, a chest reward, gold collection and a level increase. The log confirms the Gilded cave chest opened and granted its reward. User input was detected during inspection; no further control was injected, and the user's active game remains open. This establishes observed normal gameplay, not an exhaustive manual traversal of every map interaction.

Audio review limitation is now established rather than assumed: passing the rendered water WAV to the available audio surface returned that this backend does not support audio input. No claim of listening verification is made. The local audio companion did not return an approved session; no computer/microphone audio was captured and no permissions were changed. Objective audio checks and exported review files remain available.

## Selected UX follow-up: cave portals and nearby enemy health

Implemented only the authorized portal markers/elevation/discovery and nearby enemy health bars. Interaction targeting/key behavior was not changed.

- Cave entrance and exit are hidden initially and discovered independently within 18 m in full 3D, requiring revealed fog and an unobstructed world-collision ray. XZ fog revealed from above does not reveal an underground portal. Discovery lasts for the current map and is shared with teammates and late-join snapshots.
- Minimap uses distinct arch (entrance) and outward-door (exit) icons. Discovered portals remain pinned to the rim when distant. Matching compact rows under the map show Entrance/Exit plus signed relative elevation: up/+ above the local player's feet, down/- below, = 0 m level. Full Tab map uses matching icons and full labels. English/Arabic render correctly; no undiscovered portal labels appear.
- Ordinary/elite enemy bars use current/max health within 14 m of the local player in full 3D. Fog, camera bounds and world occlusion gate visibility. Bosses use their existing dedicated UI. One overlay refreshes at 20 Hz, caps at 16 bars and 32 occlusion rays per refresh, and skips overlapping bars and the current minimap area. Pool/death references are checked rather than adding nodes to every enemy. Existing UI scaling applies. No enemy-health authority or interaction behavior was changed.
- Build passed with only the two existing warnings. Final content audit: 21,343 checks. Final CAVEUI checks passed headless and in English/Arabic GPU-rendered runs, covering independent discovery, hidden/wall/depth states, signs, real Tab map, ordinary/elite/current health, 3D distance, boss exclusion, fog/occlusion, despawn/pool reuse and bounded work.
- Real ENet host/client checks passed shared entrance discovery while exit remained hidden, plus host-owned damaged enemy health on the nearby overlay. New cave discovery was not separately exercised in a live Steam or late-join run; snapshot wiring is implemented. Protocol 6 prevents mixed incompatible builds.
- Initial headless projection assertions failed because the dummy viewport is 64x64 and an extra fixed pixel inset excluded visible points; removed redundant inset, retaining actual bar bounds checks. A standalone Arabic test-map fixture positioned off-screen; replaced with the real Tab sheet and reviewed its English/Arabic renders.

Evidence in Windows Temp: wavebreak-cave-ui-final-check.log, wavebreak-cave-ui-final-en.log, wavebreak-cave-ui-final-ar.log, wavebreak-cave-ui-coop-host.log, wavebreak-cave-ui-coop-client.log. Captures use wavebreak-cave-ui-final-en/ar prefixes, suffixes hidden, entrance, both, fullmap and health.

At the user's explicit request, launched one persistent manual English single-player reef-dive window from the latest build, without auto/check/capture/invulnerability/Steam or Dummy audio. Verified a responding Wavebreak window and live reef startup log. Existing sound settings were preserved. The user game was left open.

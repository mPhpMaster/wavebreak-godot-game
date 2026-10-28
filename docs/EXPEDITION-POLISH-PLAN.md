# Expedition polish — authorized work

Latest published baseline: 1.4.2. Current branch: codex/expedition-polish.
The broader batch is not published. Draft changes from 161dae7 were archived
before release; this branch now incorporates the shipped UI/Flick fixes.
Only checks recorded below establish current implementation.

## Verified current work
- Rooftop acquisition is connected to both player inputs and registered city
  buildings. Low/sky aim, side selection, taller nearby preference, obstruction,
  range, held-anchor stability, 12-query budget and teardown passed RooftopCheck.
- Existing DualHookCheck passed after the targeting change, including independent
  controls, boost, bindings and death cleanup.
- Classic Thornmeadow and map-to-character selection shipped in 1.4.2.

## Still required
1. Honest staged initial loading and prewarming; preserve graphics/content; compare cold/steady measurements.
2. Optional practical tutorial for independent hooks, boost and core dash; skip and replay, current bindings. Replace permanent reef/city bottom help with temporary dismissible prompts.
3. Core-hit sound/flash/giant reaction and visible wounds/blood-style damage; no duplicate damage or co-op false confirmation.
4. Clear reef cave and city warden objectives; progress and appropriate one-time authority/rewards.
5. Prepare new-player test route/questions/metrics. Actual participant testing remains unavailable without people; no outreach authorized.
6. New original MAIN MENU music inspired by existing Gravemoor Lullaby and Lantern Waltz (musicbox melodies, triple-meter waltzes). User has not heard new map music: do not replace reef/city tracks yet. Those maps currently use tidal and harbour respectively.
7. Jump AND Dash boost while tethered. Dash near an eligible core keeps core-strike priority. Existing mapping/rebindings/controllers must work.
8. Gameplay settings, title and in-game: independently usable hold vs click-toggle hook behavior. Toggle retains attachment until second click or invalid/out-of-range target. Rooftop preference overrides low-wall manual aim, as requested.
9. Implemented and engine-verified: automatic reachable rooftop selection for left/right, camera-forward and side preference, taller nearby roof bias, range/LOS, stable held anchors and bounded queries.
10. Grapple moving giants as well as city boundary walls. Moving hook anchors follow body-local attachment, expire on death/despawn, replicate safely.
11. Hook launch must visibly travel from character reel to target and attach on arrival, with an original compressed-gas/reel launch sound. No instant cable from nowhere. Independent simultaneous launches/releases.
12. City architecture inspired by the anime's stone/plaster/timber/gabled tiled roofs; retain expanded city/navigation/quality and exact visible/collision hook geometry.
13. Monsters and giants cannot overlap; queue at narrow paths. Player cannot push giant. Fix physical/crowd authority without widespread physics performance collapse.
14. Choosing either expedition map opens CHARACTER CHOICE, then player starts. Map recommends Nori/Vesper but permits every unlocked character; back navigation restores intended route/preview.
15. Vesper starts with real dual swords, reflected on rig and in actual melee attacks/damage/upgrades.
16. Nori starts with shotgun: roughly2s base interval, upgradeable rate, actual shooter recoil, original shot sound and water bubbles. Preserve 3D swim targeting/co-op/character portability.
17. Repair underwater dash into a perceptible directional impulse with controlled water-drag decay, rather than the current unsatisfying speed behavior.

## Validation
- Real engine checks for all new mechanics, co-op moving targets/projectiles/hits, controls, both level-up layouts and rest overlap.
- English/Arabic renders, optional tutorial skip/replay and map→character flow, no saved-profile writes in automated tests.
- Compare startup/cold spikes and long gameplay at retained user graphics. Do not inspect user's screen or close/restart a manual game.
- Complete work already authorized; do not stop at repeating this plan. No extra confirmation needed for implementation.

18. Underwater dash must remain steerable left/right during the impulse. Its direction was previously locked; new pure steering/decay helper and .55s swim burst implemented, awaiting checks.
19. Replace the reef wreck with a recognizable broken ship hull, ribs, deck/cabin/mast/debris and clear swim passages; preserve chest, guardian, map marker, terrain grounding and honest collision.
20. Replace underwater boss with a chosen free Leviathan-like sea monster from a legitimate asset store; verify source license, rig/format and performance before import. Research not complete: Meshy gallery has CC0 static candidates requiring account; Sketchfab flying sea-monster rig is270k triangles; Reaper/Ghost models are Subnautica-derived and are not selected. No model imported yet.
21. Replace XP and gold visuals globally in every map, using selected free asset-store models. Candidate Quaternius RPG Essentials is CC0; selection/import not yet done. Preserve pickups/pooling, pickup ranges and network identity.
22. Rest room disables combat abilities and their particles, clears lingering combat effects. In gameplay attacks fire only with a living hittable target in appropriate range; preserve non-combat passives.
23. Mushrooms/plants in Saltglass Shallows and crystals/coral/plants across ALL maps become collectible with clear interaction and host-owned rewards. Avoid thousands of new per-frame nodes; account for per-user prop density differing in co-op.
24. Enemies navigate around platforms and use reachable ramps/terrain instead of pushing into an inaccessible platform edge. Do not allow roof camping to break AI.
25. Co-op each enemy picks nearest living player; spawn center is the mean of all active living players, with terrain/LOS/safe-distance rules. Both ordinary spawns and bosses.
26. Enemy health bars must track smoothly under rapid player/camera motion: retain throttled target selection but update projection every rendered frame after camera movement, with correct interpolation/visibility.
27. Saltglass Shallows boundary must sit at the actual playable terrain edge rather than an apparently interior line; keep collision, visual edge, bounds, spawn/navigation and minimap consistent.
28. Team health panel shows each teammate's DPS. Tab opens an all-player scoreboard with kills, deaths/downs distinguished, damage, healing and DPS, plus leaders/sorting. Preserve configurable bindings and give the big map an unambiguous alternate binding; verify real co-op stat replication.
29. User questions why Reef→Saltglass Shallows→Thornmeadow (normal starting map). Inspect optional route expansion under longer1.3.4 tiers: prevent inappropriate returning to the tutorial/first map, while preserving phase counts, bosses and difficulty. Explain verified current cause before changing route semantics.
30. Normal single-player PLAY must start Thornmeadow (Classic), not a persisted Reef route. Main→play now resets route0; special expedition→play preserves selected map. Expedition Start now opens character selection instead of immediate gameplay; tests need updating for new flow.
31. Reef phase3 must be Stormwatch Harbour, explicitly requested. Reef extension now appends Cinder/Gravemoor/Rimeglass after the three-map prefix instead of inserting Thornmeadow before phase3. Need route-count and phase/boss scaling checks.
32. Replace the cave guardian's appearance with an appropriate free internet asset independently of Leviathan boss. Identify actual cave enemy and asset before claiming import.
33. Cave interior needs readable traversal: improved geometry, lighting and visible route/landmarks to entrance/exit so swimmers do not get lost. Preserve discovery, rewards and co-op.
34. Enemy masks now include EnemyLayer with giant collision priority10; slow walk has explicit .5/.65 caps. Needs collision/performance/nav regression tests. No claim all overlap/pushing is solved yet.
35. Health bars now QueueRedraw every frame with ProcessPriority100 while selection remains20Hz. Need render smoothness tests and interpolated anchor review.
36. Coop spawn anchor now averages valid living player positions; bosses use same anchor; nearest target ignores dead/invalid and uses3D distance underwater. Need three-peer placement/target tests.

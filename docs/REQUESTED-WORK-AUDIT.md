# Requested-work audit — 28 October 2026

## Verified published baseline

Latest release: 1.4.2. Evidence: docs/RELEASE-1.4.2.md and published GitHub release.

- Reef map, continuous swimming, surface waves/breach, terrain/mountain borders, long cave, marine enemies and resource collection: existing implementation; previous runtime verification is recorded in UNDERWATER-IMPLEMENTATION.md and EXPEDITION-FIX-VERIFICATION.md. Subjective audio/feel are not automatically verified.
- Expanded city, large original giants, independent LMB/RMB hooks, attached Space boost and Dash core strike: shipped baseline. This does not include rooftop auto-selection.
- Map selection, map-default Nori/Vesper with manual character choice, corrected Classic start: shipped.
- Flick replacement Stormfang Blades: shipped 1.4.2, including saved weapon identity and stationary/3D/outward/return tests.
- Minigore credits and GitHub update notification/download/Windows handoff: shipped. Mock/temporary-install tests passed; installation over a real player copy is not established.
- Windows installer/portable release: published and artifact checks recorded in RELEASE-1.4.2.md.

## Explicit unfinished request from this chat

- [x] Hook clicks automatically acquire the nearest suitable high rooftop, at the top of the building. Player.Grapple now calls CityRooftops.Find for each side; Arena registers/clears its roofs. RooftopCheck and DualHookCheck pass in the actual engine, covering low/sky aim, nearest/higher preference, occlusion, reach, side independence, query budget, stable held anchors and teardown. Not yet released.

## Broader authorized polish

All 36 entries in EXPEDITION-POLISH-PLAN.md remain the detailed acceptance list. The plan includes unverified draft code; neither the plan nor a successful release proves those entries complete. They must be reconciled against 1.4.2 before continuing.

- [ ] Loading/prewarming and retained-quality performance checks.
- [ ] Optional/replayable tutorial, temporary prompts and clear cave/warden objectives.
- [ ] Hit feedback, target reactions and original main-menu music.
- [ ] Jump/Dash hook boost, hold/toggle settings, side-aware rooftop targeting, moving giant/boundary anchors, visible traveling hook launch and sound.
- [ ] Anime-inspired city architecture, giant crowd/collision behavior and navigable platforms.
- [ ] Vesper dual swords and Nori shotgun with actual attacks, upgrades, sound and effects.
- [ ] Steerable underwater dash and smooth enemy-health-bar projection.
- [ ] Recognizable wreck, licensed sea boss/cave guardian assets and clearer cave route/interior.
- [ ] Global XP/gold visual replacements and collectible vegetation/crystals across maps.
- [ ] Safe rest-room combat suppression; target-dependent attacks.
- [ ] Co-op nearest-living-player targeting, averaged spawn anchor, teammate DPS and scoreboard replication.
- [ ] Saltglass playable-edge consistency, Reef phase-three Harbour and route-count/scaling verification.
- [ ] Full/compact non-pausing co-op upgrade layouts and rest-panel overlap tests.
- [ ] Real-engine, EN/AR visual and multiplayer regression checks for the final combined build.

Android/Roblox remain postponed; web cancelled; live Steam/manual listening require real acceptance and are not claimed complete. No third-party outreach is authorized.

## Execution status

Implementation chat was idle at audit start. A resume request with this acceptance scope and rooftop priority was dispatched. Confirm actual active status separately; dispatch alone is not proof of implementation.

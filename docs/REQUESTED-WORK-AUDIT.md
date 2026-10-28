# Requested-work audit — Wavebreak 1.4.3

The 36-item acceptance list is implemented in the current release candidate.
Version 1.4.2 is the previous published baseline. Final publication evidence is
recorded in RELEASE-1.4.3.md once the release assets are verified.

| Scope | Implementation / evidence |
|---|---|
| Loading and performance | Real staged progress and model rendering prewarm. StartupCheck completes 54 steps, removes the overlay, resumes play and starts Thornmeadow. Full-density 1920px performance tour retains scale 1, glow/fog and existing user settings; measured mean frame times roughly 6–9 ms in the sampled scenes. |
| Help and objectives | Timed F2-skip/F1-replay practice observes actual hooks, boost, core strike and swimming actions. Map/phase objectives and cave/resource progress are visible. Existing one-time rewards remain authoritative. |
| Hooks and boost | Side-aware reachable roofs, hold/toggle, tether-preserving Jump/Dash boost, traveling tips and original launch audio. Moving giant local anchors and boundary walls. Rooftop, dual-hook, controls and target checks pass. Two actual ENet peers pass core damage, tells, moving anchors, dual hooks and boost. |
| City and giants | Original plaster/timber facades and colliding gabled roofs; physical crowd collision, giant priority, no knockback, .5/.65 walking speed and health-driven wounds. Actual full-scale navigation and rooftop collision checks pass. |
| Weapons | Vesper twin swords and Nori six-pellet shotgun, actual 3D hits, recoil/audio/bubbles, two-second base interval and speed upgrades. ExpeditionWeaponsCheck and 21,925 content-audit checks pass. |
| Swimming | Steerable decaying burst, including exact opposite-direction reversal. SwimmingDashCheck and isolated live swimming/pose/breach checks pass. |
| Reef art | Licensed Quaternius shark and wErT Leviathan geometry with original portable swim morph loop; full provenance/license notices included. Detailed wreck/ribs/cabin; brighter cave with directional markers and entrance/exit landmarks. Runtime imports/animation and render reviews completed. |
| Pickups/resources | Shared normalized Quaternius XP/gold geometry on every map. Deterministic collectible crystals/plants/mushrooms, duplicate prevention and host-owned spent state. All-map rebuild/gather checks pass. Decorative collectible-look scatter is replaced by resource instances. |
| Rest | Both co-op card layouts preserve movement and do not pause/dim. Real two-peer layout tests cover upgrade selection and message overlap. Active projectiles and signature effects are retired on entering rest; targetless combat activation is gated. |
| Pursuit/spawning | Ground enemies route around platforms and physically climb ramps. Nearest valid living player uses underwater 3D distance; all director and phase-boss spawn paths use the living team's center, safe terrain/distance and occupied-space rejection. Three real peers verify target selection, down exclusion and center math. |
| Health bars | Selection remains bounded/throttled; projection redraws every rendered frame after camera movement, using interpolated enemy transform when available. |
| Bounds/routes | Saltglass playable radius extends to 220 with its visible skirt removed. Classic starts Thornmeadow. Reef prefix remains Lumenreef, Saltglass, Stormwatch; longer routes append later maps. Actual classic-after-expedition and route audits pass. |
| Co-op stats | Team DPS and Tab scoreboard with kills, bosses, downs, terminal deaths, damage, ten-second DPS, restored health, damage taken, level and gold. Three actual peers verify field replication. EN/AR screenshots reviewed. Custom keybindings are preserved; only untouched old Tab/Y pairs migrate. |
| Music | Original sample-free title waltz replaces only title.ogg; map tracks remain available for the user's review. Technical audio measurements recorded in TITLE-WALTZ-AUDIO.json. |
| Distribution | Versioned What's New with Arabic; final Windows portable/installer, manifest/embedded-helper/hash verification, Git push and GitHub publication. SHA-256 helper no longer depends on PowerShell's module autoload. |

Human new-player sessions and manual listening remain unverified; protocols are
prepared. Live Steam and installing over a real existing player copy are also
unverified. These limits must remain explicit and are not replaced by automated
checks. No third-party outreach or interruption of the user's live game occurred.
Android/Roblox remain postponed; web remains cancelled. Ancient Portal was only
evaluated and is not included in this release.

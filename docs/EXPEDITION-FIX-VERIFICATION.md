# Expedition verification — 28 October 2026

Feature implementation and functional checks are complete. Performance acceptance remains open: the final controlled city run reproduced severe GPU/presentation stalls. Manual listening, player feel and live Steam validation are also unverified. The user's open game was not restarted, closed or inspected; new compiled changes require a later launch.

## Changes and evidence

- Independent left/right hooks, separate releases, attached Space boost, controller inputs and rebinding; expanded 350-metre city, articulated giants, Nori/Vesper selection and retained manual overrides.
- Cave entrance stones have terrain-supported footings, with matching collision.
- Tower walls previously spanned below ground to half their intended height. Walls now span 0 to height minus 0.25 metres; the roof slab spans the final 0.25 metres and meets the full-height collider. Upper walls, rooftop views and structural hook anchors were checked in the actual renderer.
- Existing Dash attempts a core strike only while airborne, near a visible glowing core, moving at least 7 m/s and off cooldown. One press produces one strike. A failed eligibility check preserves normal dash. There is no separate F action. RMB remains the city right hook; Shift/controller dash works while that hook is held.
- Co-op core strikes use a dedicated host request: sender state, airborne pose, speed, range, line of sight and cooldown are checked before host damage. The two-process fixture generated a real hook boost and dash: client strike=True, host accepted=1.
- At most twelve peripheral 2D speed streaks redraw at 30 Hz while tethered and moving above 18 m/s. Intensity follows actual velocity and the screen-effects setting. No particles or lights are added. Inactive effects stop redrawing.
- Minigore remains in the IDEA credits.

## Functional verification

`dotnet build --no-restore` passed with the two existing CS0108/CS8602 warnings.

`tests/expeditions.ps1 -SkipBuild` passed city, dual hooks/rebinding/controller configuration, fresh/existing/manual-override/direct EN/AR selection, content audit, swimming/pose, cave UI, reef systems and terrain checks. The initial co-op fixture supplied a velocity that ordinary movement immediately replaced; it was corrected to generate real speed with the hook boost. `tests/expeditions.ps1 -SkipBuild -CoopOnly` then passed both real ENet peers, including host-accepted core damage.

Evidence: temporary folders `wavebreak-expeditions-20261028-044836` and `wavebreak-expeditions-20261028-045639`. Rendered city log `wavebreak-city-roof-final.log` reports CITY PASS; rooftop, giant and tether captures were reviewed. Render checks were capped to 5 FPS on a hidden desktop and are not performance measurements.

The updater's mocked engine suite passed 33 checks: semantic version order, no downgrade, official assets and HTTPS redirects, path/ZIP rejection, cached metadata, rate limits, streamed size/SHA256, truncation, complete package preparation, explicit installation, failed/retried handoff without quitting, cancellation and Windows PowerShell 5.1 Arabic/BOM handling. The hidden Arabic form was constructed without being shown.

`tests/updater-portable.ps1` passed real Windows PowerShell helper tests against locally compiled harmless fixture executables and temporary fake installations: apply, backups, locked-file rollback, cancellation, wait timeout without killing the fixture, bad checksum, preservation of extra files, and cleanup of actual `*.wavebreak-new-*` partial filenames. A process-exit inspection race found by this test was fixed. No downloaded installer or real game installation was executed or replaced.

## Controlled performance results

Four separate 120-second gameplay runs used the same binary, graphics settings, per-map seed and scripted simulation-time control. Startup is separate; steady metrics exclude the first ten gameplay seconds. City seed: 632668492. Reef seed: 42. Actual simulation time and enemy outcomes diverged during stalls, so these are bounded observations, not universal performance guarantees.

| Map / algorithm | Mean ms | P95 ms | P99 ms | Max ms | Frames >100 ms | Render CPU / GPU ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| City reference | 4.73 | 6.14 | 6.86 | 28.75 | 0 | 1.28 / 4.57 |
| City tuned | 15.44 | 16.09 | 338.29 | 442.90 | 159 | 1.25 / 15.02 |
| Reef reference | 7.60 | 9.53 | 11.08 | 510.88 | 26 | 1.56 / 7.10 |
| Reef tuned | 7.42 | 11.06 | 12.92 | 37.55 | 0 | 1.76 / 6.92 |

City tuned simulation advanced only 92.82 seconds during the 120-second wall-time run. Low render CPU cost alongside large GPU/presentation stalls does not establish their cause. Background load or desktop scheduling remains a hypothesis, not a diagnosis. No GPU comparisons were repeated while the user played. These measurements precede the final roof/dash/wind changes.

Isolated findings remain useful: cached terrain generation preserved all 75,294 face vertices and reduced its measured build from about 1462 to 164 ms. Splitting placement reduced a combined 142 ms placement frame to a measured peak around 30 ms. These improvements do not establish that all lag is fixed.

Raw controlled metrics and historical exploratory results are in `EXPEDITION-SOAK-PERFORMANCE.json`. Older short tours ignored the requested seed, and older long runs used diverging autopilot; their favorable results are historical, not acceptance evidence.

## Remaining acceptance

- Reproduce and diagnose the city GPU/presentation stalls during an isolated session after the user finishes playing; benchmark the final roof/wind build then.
- Manual listening and player-feel acceptance; live Steam transport/player join.
- Production Windows installer wizard/update and release smoke test. Current tests deliberately use mocks and temporary fixtures; no release was published and no version was bumped.

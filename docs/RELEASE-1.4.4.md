# Wavebreak 1.4.4

## Scope

This patch removes scoreboard view selection, gives scoreboard input priority over overlapping pause/character-sheet bindings, keeps equipment hover targets stable, and restricts underwater-only enemy definitions across spawn paths. Opening pause with Escape suppresses a held scoreboard request until the key is released.

Equipment descriptions use current weapon/tome ranks and actual accumulated item modifiers. Custom tooltips process while paused and close with the character sheet. Enemy model replacement detaches the previous pooled rig immediately before scheduling its deletion, preventing an obsolete land model from remaining attached during a map change.

Each release includes matching English and Arabic What's New text. Classic Play remains rooted in Thornmeadow.

## Verification

- C# build passed; one existing `CoopLineup.Ready` hiding warning remains.
- `OverlayRegressionCheck`: real Tab/Escape press and release, Escape while Tab is held, duplicated saved action bindings, persistent equipment tiles, live descriptions, tooltip cleanup, and scripted/boss/multiplayer rejection of marine definitions on Thornmeadow.
- Native GPU hover and screenshots passed in English and Arabic on an isolated Windows desktop, including tooltip appearance while paused.
- Two actual ENet peers joined and loaded the run; overlay regression passed on both peers.
- Guardian and Leviathan checks passed after pooled model replacement was corrected. City, rooftop, dual-hook, classic-start and release-notes checks passed.
- Exported Windows executable passed overlay, release-notes and classic-start checks.
- The portable-package verifier compares every payload file against the export, validates Windows executable version, and checks embedded updater helpers.
- Final content audit passed 21,949 checks. Swimming, swim pose, cave UI, Reef systems, expedition progression and both ENet city peers passed.

Evidence logs are stored in the local temporary directory under `wavebreak-144-*` and `wavebreak-expeditions-20261028-*`.

## Limits

This patch does not implement additional shotgun grip or recoil changes. Human listening, Steam integration and installing over an existing player's installation remain unverified. Those are not represented as completed by the automated checks.

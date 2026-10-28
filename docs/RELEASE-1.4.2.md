# Wavebreak 1.4.2

This release improves the expedition menu, adds versioned What's New notes,
and ensures ordinary Classic Play starts in Thornmeadow after a prior expedition
selection. Expedition start buttons now lead through character selection.

The broader gameplay polish remains on `codex/expedition-polish` at `161dae7`.
Draft physics, combat, co-op, dash and rooftop scaffolding from that branch are
excluded from this release. `EXPEDITION-POLISH-PLAN.md` describes that pending
work and is not a list of shipped features.

The release retains the 1.4.1 license, attribution and sponsor changes.
No live player installation is modified by packaging checks.

Flick also receives Stormfang Blades, retaining the existing saved weapon ID.
The returning blades target in three dimensions and work while stationary.

## Publication and verification

- Published stable release: https://github.com/mPhpMaster/wavebreak-godot-game/releases/tag/v1.4.2
- Source: `9fcf7e0d65217d1d5a3f5dc405f41741980be00f`.
- Full expedition checks passed, including both classic entry paths, fresh and
  existing profiles, menu selection, swimming, cave systems and both ENet peers.
- Flick content audit passed 21,723 checks, including stationary, elevated,
  outward/return hits and cleanup. Updater checks passed 34 checks.
- Exported executable passed the versioned notes and Classic Thornmeadow checks.
  Arabic release notes were rendered and visually reviewed from the export.
- ZIP extraction verified all 191 exported files and embedded updater helpers.
- ZIP SHA256: `d4e3570c854a5e1bb6a4d1dba1a36c9c270172459f860a9450942395b98b1a23`.
- Installer SHA256: `43eaad80a314627a8d58e2347c2176e1cb6ee50e74edefc8d482ddb44feb641d`.
- All three uploaded asset digests and sizes match local files.

Installer execution and human listening/play acceptance were not performed.

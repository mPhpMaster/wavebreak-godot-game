## Changes

- Nori uses a separate shotgun with two-hand grip poses. The muzzle and body face the shot; swimming animation no longer swings the weapon sideways from the hip.
- Swimming recoil is limited to 0.8 m/s, refreshed rather than accumulated, and decays rapidly. Land recoil is limited to 0.35 m/s.
- Vertical swimming control is independent of weapon kick and currents. Dive works through a surface breach; coastal floor recovery cannot launch swimmers onto a cliff above the sea.
- Co-op mirrors shotgun aim and firing poses. All players must update together (protocol 11).
- Fixed installed-update readiness failing when the helper tried to create a lock in Program Files before elevation. The installed path leaves installation writes to the elevated setup wizard; portable updates retain their transactional lock and rollback.
- Update failures display the helper's actual error details.

## Installing from an affected older version

If 1.4.3 or 1.4.4 reports `Update error: Handoff`, download and run `Wavebreak-Setup-1.4.5.exe` manually once. Those older executables contain the old updater helper; downloading a newer release cannot replace that running helper. Future updates from 1.4.5 use the repaired path.

The real installed 1.4.3 path was checked through readiness without closing the user's game or replacing its files. Installation over that live copy and human listening remain unverified.

## Downloads

- `Wavebreak-Setup-1.4.5.exe`: Windows installer.
- `Wavebreak-1.4.5-windows.zip`: portable Windows build.
- `SHA256SUMS.txt`: checksums.

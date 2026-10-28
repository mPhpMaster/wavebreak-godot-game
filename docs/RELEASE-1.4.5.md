# Wavebreak 1.4.5

## Actual causes and corrections

The failed installed update request targeted `C:\Program Files\Wavebreak` from version 1.4.3. The helper's `status.json` reported access denied creating `.wavebreak-update.lock`. That happened before readiness and before the installer could request elevation. Installed updates now leave writes in the installation directory to the setup wizard. Portable updates retain their exclusive root lock and transactional rollback. Program Files installation requests explicitly launch the all-users installer with elevation. The UI now shows helper failure details instead of only the `Handoff` category.

Nori's shotgun was baked into one lower-arm mesh while the swimming animator continued to swing both arms. The replacement is a separate weapon node with two-bone arm poses placing both palms on defined grips. World-space aiming is converted through the swimming rig transform; the actual muzzle becomes the pellet origin. Local body yaw follows the shot briefly, and co-op peers receive an aim/firing-pose RPC. This changes the co-op protocol to 11.

The previous swimming recoil added 5 m/s per shot up to 10 m/s. Swimming also used the previous final `Velocity.Y` as the next input to its vertical controller, feeding weapon kick and current back into subsequent frames. Looking down could therefore produce a sustained upward force and trigger surface breach. Shotgun recoil now refreshes a bounded 0.8 m/s kick (0.35 on land), decays at 8 m/s², and is separate from vertical swimming control. Dive input also applies during a breach. Coastal wall recovery no longer treats a cliff above sea level as a floor onto which the player should be raised.

## Verification evidence

- C# compilation passed.
- Actual installed 1.4.3 readiness preflight passed against the existing Program Files root and the real downloaded 1.4.4 installer. `-PrepareOnly` exits immediately after validation/readiness; it cannot replace files or launch the installer. The user's game remained open and its executable version remained 1.4.3.0.
- Updater tests passed 34 checks; real Windows PowerShell portable-helper apply, rollback, cancellation, timeout, hash rejection and preservation checks passed.
- Baseline and 20× attack-speed shotgun tests maintained forward, upward and downward swimming. Over 150 physics frames, progress was approximately 16 m at baseline and 14.93 m at 20× fire rate. The test checks actual weapon cooldown shots, finite movement, a recoil bound of 0.8 m/s, palm placement and muzzle alignment.
- Surface dive passed with no firing and with downward feedback hammered every physics frame, reaching more than 12 m depth without rebreaching. The coastal wall test stayed below sea level.
- Two actual ENet peers passed shotgun-pose replication and descent checks; both observed the other's grip/firing pose and reached over 21 m depth.
- GPU screenshot review on an isolated desktop verified the two-hand swimming pose. The same rig passed the land grip/recoil check.
- Final exported Windows executable passed the dive, shotgun handling and 34-check updater fixtures. Package verification compared all 191 files and embedded updater scripts against source. Content audit passed 21,973 checks.

Portable ZIP: 97,183,913 bytes, SHA-256 `009a3b6837766200c6061f3ed4b95002fdd846581db8c7fac77ec84865c6464f`.

Installer: 75,670,012 bytes, SHA-256 `2ed658baaf023f1b0e12e92818c15ec296890c8e7740c790bd6ee72d5c71bb60`.

Local evidence uses `wavebreak-145-*`, `wavebreak-shotgun-*`, `wavebreak-dive-control.log`, `wavebreak-installed-preflight.log` and `wavebreak-updater-regression-next.log` in the temporary directory.

## Real installation acceptance

After explicit user approval to close the game and install, the released installer completed with exit code 0 over the existing administrative installation in `C:\Program Files\Wavebreak`. Its pre-install executable version was 1.4.4.0; the resulting version is 1.4.5.0. All 191 exported payload files matched their installed SHA-256 hashes. `profile.cfg`, `settings.cfg`, `keybinds.cfg` and `leaderboard.cfg` were backed up locally and remained byte-identical through installation.

The installed game was launched normally afterward (PID 1900), with a responding `Wavebreak` window and the correct executable path. Evidence: `wavebreak-145-real-install.log`; local save backup: `build/install-verification-145-20261028-125453`.

Human listening remains unverified. The old helper embedded in affected 1.4.3/1.4.4 executables cannot repair itself by downloading another asset: use the standalone 1.4.5 installer once. Subsequent updates run the repaired helper.

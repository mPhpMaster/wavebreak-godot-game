# Wavebreak 1.4.6

## Corrected causes

The shipped shotgun pose was not visually acceptable. Its palm-position check only proved that the hands reached numerical grip points; it did not prove the receiver was upright or clear of the face. The swimming rig's local fallback up vector flipped the weapon relative to world up. The new pose converts both aim and world up through the hips transform, places the receiver below/forward of the face, separates the trigger grip from the foregrip, and uses reachable two-arm poses with visible mitten/thumb geometry. Standing, swimming and side views were rendered separately.

The old Leviathan fixture called `SpawnAt` directly. The real first Reef phase instead checked the cloned boss's inherited 470-second unlock time before considering the map's authored boss, although the first encounter begins after 210 seconds. That allowed a generic older boss to appear. The Reef-specific authored encounter now bypasses that generic gate, and the Leviathan definition is available from the first Reef phase. The new fixture uses `StageDirector.DevBossNow`, exercising actual phase selection, pooled spawn and boss announcement.

The actual licensed `assets/models/wertt/leviathan/leviathan-normalized.glb` is mandatory for this encounter; missing it raises an explicit failure instead of substituting a procedural marine model. It is scaled fourfold and pitched into a horizontal swimming pose, approximately 31 × 24 × 32 metres. The collision envelope is 32 metres high with a 15-metre radius. Boss depth pursuit targets the body centre. Spawn placement checks the full surrounding seabed/surface volume and distance to all living players. Body-surface targeting and hit queries use the enlarged radius, including the spatial-grid path; regular target geometry is unchanged.

Cave Shark remains its separate cave guardian. Recoil and diving changes from 1.4.5 are retained. Co-op protocol 12 prevents old smaller collision/model definitions mixing with the new boss.

## Verification

- C# compilation passed.
- Shotgun fixture passed baseline and 20× attack-speed swimming in all three directions, grip reach, muzzle alignment, recoil bounds, steering and land pose. GPU screenshots reviewed standing, swimming and side views.
- The actual first Reef phase summoned `reef_leviathan` before the inherited generic unlock time. Its imported model, animation loop, fourfold scale, submerged spawn, enlarged collision/reach, actual shotgun damage at the body surface and submerged pursuit passed.
- Ranged attacks acquire targets beyond the enlarged collider and fire from body height in 3D. A real enemy volley hit a test swimmer outside the collision volume. Boss actions were moved to a separate partial file so both behavior files stay below 500 lines.
- Two real ENet peers observed the same imported horizontal model, 32-metre collision envelope, 15-metre radius and synchronized damage/health.
- Exported Windows executable passed actual-phase Leviathan, shotgun handling and What's New checks. Package verification compared all 191 exported files and embedded helpers.
- Content audit passed 21,991 checks. Full expedition regression evidence is in `wavebreak-expeditions-20261028-132321`; focused evidence uses `wavebreak-146-*` in the local temporary directory.
- Final remaining expedition regressions passed in `wavebreak-expeditions-20261028-134241`; persistent city/Leviathan ENet cases passed in `wavebreak-expeditions-20261028-134424`. Idle swimming checks isolate autopilot/weapon kick, and health-bar checks compare against actual prior health rather than assuming a full-health target.

## Artifacts

Portable ZIP: 97,189,733 bytes; SHA-256 `af8506ae8c2b3b0eb3f6a6bd9b8a5380979fa42843b028e86135bd03ac119d55`.

Installer: 75,678,902 bytes; SHA-256 `7cb1570f789fd0b014c4f6674fa52fdeb5970ce2aeb4b6d600dff914f18429bd`.

The running installed game was not closed or replaced for this patch. Screenshot review and automated checks do not establish the user's visual acceptance.

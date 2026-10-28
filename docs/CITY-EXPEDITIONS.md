# Special expeditions

Open **SPECIAL MAPS: REEF & CITY** from the title screen. Selecting Lumenreef
Basin immediately selects Nori; selecting Bellspire Ward selects Vesper. The
selected profile identity, animated preview, stats and co-op identity update
together. Choose another unlocked hero afterward to override the default.
Refreshing the page or starting the same selected expedition retains that
choice; explicitly choosing a map again applies its default. Starting directly
without selecting a card applies that map's default. Classic Play restores the
previous classic hero and route, including after returning from an expedition.
Difficulty remains unchanged.

## Bellspire Ward

The optional `skyline-run` starts in Bellspire, continues to Stormwatch Harbour,
and finishes in Cinder Reach. Longer tiers retain the city start and Cinder
finale. Existing route indices and the six-map standard pool are unchanged.

The city radius is 350 metres, up from 190: approximately 3.4 times the playable
area. More than 240 solid towers form dense avenues and four outer districts:
North Bellspire, Foundry Quarter, Westgate Heights and Southward Terraces.
Arrival roofs rise from 13 to 33 metres; outer towers reach 55 metres. Visible
roofs and collision match. Sixteen chests, 36 breakables and four charge shrines
use the expanded space. The giant navigation grid scales with the boundary.

| Default input | Action |
| --- | --- |
| Hold LMB / Q / left bumper | Attach and hold the LEFT hook |
| Hold RMB / V / right bumper | Attach and hold the RIGHT hook |
| Release either | Release only that hook, preserving the other |
| Space / controller A while attached | Boost while remaining attached |
| Space without a hook | Normal jump; no airborne hook thrust |
| Dash (Shift / controller X, rebindable) | Strike a nearby glowing core while airborne at speed; otherwise normal dash |

Settings → Key Bindings supports rebinding both hook actions separately.
English and Arabic hints show the current bindings, separate attachment states,
and boost cooldown. Each cable originates from its own reel and terminates at
its own anchor. The aim ray excludes the player's capsule.

Anchors require a real structural surface, clear sight and a 92-metre range.
Surface checks work from either wall face. Each tether validates independently;
a blocked anchor requires releasing that input before reacquisition. Reeling
and tangential steering share a bounded pull budget. Gravity always applies.
Boost adds forward and upward momentum, caps speed at 60 m/s, and has a
2.2-second cooldown; ordinary tether speed caps at 46 m/s. Holding two hooks
never doubles gravity or pulling force. Death, teleport and map exit clear both
hooks. Camera pitch/collision settings restore on exit. RMB retains dash on
ordinary maps and acts as the right hook in the city.

## Stone wardens

| Enemy | Height | Base health | Role |
| --- | ---: | ---: | --- |
| Bell Warden | 26 m | 650 | Ordinary giant |
| Gate Warden | 32 m | 1,000 | Elite giant |
| Bellspire Archon | 39 m | 2,100 | City portal boss |

Original articulated models use tapered ribcages, pectorals, segmented armour,
ribs, sculpted faces, fingers, thighs, knees, calves and toes. The Gate Warden
has heavier arms and layered shoulders; the Archon has a crown and mantle.
Ceramic, terracotta, slate, brass and recessed luminous cores distinguish them.
Geometry is merged per body/limb, with distant LOD. Forward facing and stride
length match actual travel. Existing collision, attack timings, health and
rewards are retained. Crowd size remains capped at four.

Chest/back cores remain at 65% standing height. Get within eight metres above
street level, moving at least seven metres per second, to strike. Damage scales
with speed and ordinary damage stats, with a 0.65-second cooldown. Existing
hit/item/reward/boss/portal paths handle the result. Feet do not count as cores.
Ground discs and aerial warning spheres commit to their marked position for a
1.7-second wind-up; buildings block attack sight lines.

## Reef entrance and rendering

The floating element was the lower half of the fifteen-rock entrance rim.
Matching visible/colliding stone footings now join its lower arch to the actual
seabed. The swim opening and separate interior chimney are retained. Capsule
sweeps check the open-water approach and the complete winding passage.

Static reef props and city geometry use spatial MultiMesh chunks with local
origins, preserving transforms, tint and animation custom data. Frustum culling
works per district rather than submitting an entire map-wide batch. City
windows use two-triangle facade quads rather than twelve-triangle boxes.
Underwater bubble streams retain their count, motion and surface clipping with
billboard discs; water wave shading remains on a denser-than-pixel-needed grid.
Saved quality, resolution, density, shadows and effects are not lowered.

## Heroes and co-op

Nori is an original fish hero with gills, dorsal fin, split tail, swimming limbs
and diver belt; +8% movement speed and Momentum. Vesper has a cropped jacket,
crossed harness, boots, scarf and twin reels; +6% movement speed and Tailwind.
Both are free on fresh/existing profiles, have portraits and animated previews,
and retain the Splinter Rail starter. Any unlocked hero can use either map.

Protocol `wavebreak-coop-8` replicates both independent anchors and boost count,
as well as existing movement, hero identity, giant health and attack tells.
Every peer needs the same build. Remote anchors validate structural contact,
range and sight independently; cables expire after stale packets. The host owns
giant health/attacks, while clients send core hits through the damage channel.

## Verification

```powershell
.\tests\expeditions.ps1 -Render
```

The suite builds and checks city collision/combat/navigation/rewards, independent
hooks and boosts across the expanded districts, fresh/existing profiles,
English/Arabic selection and manual overrides, localisation/content, Nori swim
movement/poses, cave UI/systems/clearance and two actual ENet peers. Render mode
captures both menu languages and city traversal on an invisible desktop.
Temporary or in-memory profiles and dummy audio protect player saves and windows.

See `EXPEDITION-FIX-VERIFICATION.md` for current measured results and evidence.
Human movement feel, listening acceptance and a live Steam session require
separate manual acceptance; the offscreen tests do not claim those activities.

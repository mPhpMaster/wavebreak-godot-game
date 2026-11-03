# Weapon and ability audit: look-alikes made distinct

Complaint: "some weapons or abilities have the same effect and the same look".
Cause: the first 18 weapons had no delivery of their own. Five `AttackShape`s
drew five things: every straight shot and every seeker was the same capsule,
every shell the same sphere and dome flash, every field the same disc, every
swing the same flat wedge, all in the weapon's tint, with one firing sound per
shape. Evolutions that borrow an expansion delivery reused that weapon's mesh,
and evolution riders shared one look per rider kind.

Code: `Combat/Weapons/WeaponLooks.cs` (the table), `LookMeshes.cs`,
`Projectile.Style.cs`, `DistinctFx.cs`, `LighthouseSweep.cs`,
`Combat/WeaponRuntime.Looks.cs`, `Audio/SfxSynth.Voices.cs`. Check:
`--check-distinct`. Pictures: `--distinct-shot=<dir>`. Numbers: `--distinct-dps`.

## Groups (before: same mechanic and same look) and what each member is now

Mechanic / targeting / look / voice. "ref" = the group's plain reference, unchanged.

| Group | Member | Now does | Now looks | Voice |
|---|---|---|---|---|
| straight shot, nearest | Splinter Rail | breaks into 2 splinters (15%) after its pierce | faceted glass shard, glass star on hit | glass ting |
| | Fan of Sparks | +15% point blank to 55% at max reach | spinning star that jitters, spark ring | zap |
| | Brinespike | drags bodies along the line, +12% per body already pierced | barbed spike rolling on its axis, spore puff | low thunk |
| | Glasswire Needles | a stream (0.05 s apart), each re-aimed | thin needles, glint on crit | high ting |
| | Searing Longshot | aims at the toughest body in reach (elite/boss first) | slender tracer + smoke line | crack |
| | Reefbreaker Shotgun (ref) | unchanged (cone, recoil, bubbles) | round pellets | shape |
| seeker, nearest | Wisp Seeker | drifts 0.3 s, then curves in slowly | bobbing comet, frost star | chime |
| | Judgement Lance | after its first hit barely steers and widens (holds a line) | long spear, light pillar | low crack |
| | Blightwasp Swarm | each wasp takes a different (next-nearest) body | striped winged wasps, erratic | buzz |
| | Tempest Spindle | corkscrews; snaps to an unhit body after each pierce (75%) | spinning double-cone + jagged thread | low zap |
| shell, contact blast | Mortar Shell | high lob onto the target; flies over bodies; landing ring telegraph | iron shell with fuse, rubble ring | low thunk |
| | Bloomcap Spores | pod burst 80% + splits into 2 hopping seedlings | spotted pod, seedlings, spore puffs | pop |
| | Hailstone Cluster | low carpet across the nearest body, skips once, bursts on landing (80%) | ice chunk with spikes, frost stars | clang |
| field on player | Ember Halo | ramps 60% to 110% while a body stays in it | ring of flame tongues | hum |
| | Static Field | big outward shove pulse, arcs to 3 bodies | zigzag ring that only shows on a pulse | low zap |
| | Rimeheart | pulls bodies in (knockback toward you) | rotating snowflake | low chime |
| melee wedge | Cleaver Arc | wide cone (ref rule) | thick crescent sweeping across | whoosh |
| | Riptide Blades | narrow lunge line, alternating sides | crossed thrust | high whoosh |
| | Forgefall Maul | overhead slam: circle ahead, short stun in the middle | cracked crater, ring, dust | deep thunk |
| | Skyline Twin Swords | two strikes (ref rule) | two thin arcs sweeping in opposite directions | clang |
| | Sleetbreath Horn | long narrow breath (ref rule) | sleet puffs drifting out | hiss |
| out-and-back | Gullwing Chakram (ref) | straight out, hang, back | spinning disc | whoosh |
| | Stormfang Blades (Flick) | zigzag tacking lane out, straight back | twin fang blade, rolling | own (signature) |
| | Glass Gale (evo) | loops (one to the nearest body, one round the reach) | needle fans | glass |
| orbit | Circling Shoal (ref) | fixed ring | glass fins | hum |
| | Eye of the Cyclone (evo) | ring breathes in/out (+-30%) | spindles | zap |
| line / beam | Lighthouse Beam | sweeps a 28 degree arc, each body once (85%) | warm sweeping bar | hum |
| | Truthline Rail (evo) | instant line (ref rule) | hair-thin white line + glints | glass |
| | Spyglass Glint (ref) | sight-cone crit beam | thin beam + lens glint | own |
| | Forked Squall | chain (ref rule) | jagged kinked bolts | zap |
| sky strike | Thunderhead Censer (ref) | random bodies | falling hailstones | thunk |
| | Broadside Barrage (evo) | a walking row through the nearest body | cannonballs in at a slant | deep thunk |
| | Sunken Sun (evo rider) | meteors on random bodies | burning chunks at a slant, flame ring | hum |
| | Hundred Forks (evo rider) | mark, then bolt straight down | ground mark + jagged vertical bolt | zap |
| | Worldforge Maul (evo rider) | line of bursts along the aim | magma spikes rising from the ground | thunk |
| expanding ring | Breaker Swell (ref) | full ring | crest blocks | hum |
| | Winter's Keep (evo) | marches (advance, stand, advance) | ring of ice spikes | chime |
| | Breakwater Cleaver (evo rider) | forward third only (x1.6 per cut) | forward crest arc | whoosh |
| vortex | Maelstrom Seed (ref) | holds its spot | whirlpool | hum |
| | Frozen Gyre (evo) | drifts after the nearest body | wheel of shards, frost star | clang |
| ricochet | Skimstone Sling | skips (ref rule) | pebble hopping | twang |
| | Deepwater Harpoon (evo) | each hit reels the body toward you, rope drawn | harpoon | low twang |
| sentry | Driftwood Sentry (ref) | straight bolts | crossbow post | twang |
| | Plague Hive (evo) | homing wasps, each its own target (85%) | paper hive + wasps | buzz |
| ground pool | Brine Wake (ref) | pools under you | brine pool | hiss |
| | Bloomtide (evo rider) | patch grows 60% to 120% | mushroom spore patch | pop |
| nova rider | Stormcage | instant forks to the nearest bodies (85%, 2 each when under 4 bodies) | jagged forks from you | zap |
| | Aurora Choir | slow motes spiralling out | comets | chime |
| kill-shard rider | Stormwing | fan along the killing blow | splinters | whoosh |
| | Knife Reef | ring of glass that homes | small shards | hum |
| | Reef of Teeth | shell bits lobbed round the corpse, burst on landing (75%) | seedling bits, rubble | pop |
| hit-blast rider | Sunlance | round burst | flame ring, no dome | crack |
| | Breaching Gun | cone through and behind the body | puffs blown on | shape |
| arc rider | Living Stormfan | forks to neighbours | jagged forks | zap |
| | Final Verdict | sentence on neighbours | pillars from above | crack |
| echo rider | Undertow Blades | echo lunge from the other side, drags toward you | dark thrust | whoosh |
| | Twin Horizons | echo reaches 15% further, spares bodies at your feet | horizon line | clang |

Not look-alikes (already their own delivery, mesh and twist): Barnacle Mines,
Mooring Anchor, Reefbreaker's recoil. Perk and item procs (Stormcount and
chain items: thin straight lines; Tidal Pulse: flat disc ring) stay as they
were; the weapons they resembled (Forked Squall, Static Field) moved away from
them. Character passive rings (Bloom, Frost, Bubble, Meow) only ever appear
one character at a time and carry their own word popup and colour.

## Balance (30 s, max level, `--distinct-dps`, same crit rolls)

Pack = 8 bodies round you, lone = 1 body at 1.6 m; rows that moved, before -> after.
Every other weapon and evolution measured identical. All within 0.8x to 1.25x;
Judgement Lance is the low one on purpose: it is a line weapon again, and the
probe is a ring, not a queue.

| Weapon / evolution | Pack | Lone |
|---|---|---|
| splinter_rail | 112 -> 128 (x1.14) | 56 -> 56 (x1.00) |
| sparkfan | 47 -> 51 (x1.08) | 52 -> 58 (x1.10) |
| ember_halo | 184 -> 197 (x1.07) | 46 -> 49 (x1.07) |
| mortar_shell | 162 -> 195 (x1.21) | 55 -> 63 (x1.14) |
| bloomcap_spores | 48 -> 48 (x1.01) | 48 -> 38 (x0.80) |
| judgement_lance | 375 -> 304 (x0.81) | 99 -> 99 (x1.00) |
| brinespike | 117 -> 124 (x1.06) | 58 -> 58 (x1.00) |
| glasswire_needles | 52 -> 57 (x1.09) | 57 -> 57 (x1.00) |
| hailstone_cluster | 35 -> 37 (x1.05) | 36 -> 34 (x0.94) |
| tempest_spindle | 160 -> 174 (x1.09) | 72 -> 72 (x1.00) |
| lighthouse_beam | 232 -> 236 (x1.02) | 120 -> 102 (x0.85) |
| skimmer_kite | 508 -> 588 (x1.16) | 331 -> 331 (x1.00) |
| evo_breakwater_cleaver | 131 -> 126 (x0.96) | 96 -> 99 (x1.04) |
| evo_sunken_sun | 355 -> 366 (x1.03) | 137 -> 138 (x1.01) |
| evo_stormcage | 586 -> 680 (x1.16) | 129 -> 128 (x1.00) |
| evo_broadside_barrage | 304 -> 370 (x1.22) | 158 -> 129 (x0.81) |
| evo_bloomtide | 103 -> 91 (x0.88) | 82 -> 82 (x1.00) |
| evo_aurora_choir | 109 -> 115 (x1.06) | 84 -> 86 (x1.03) |
| evo_glass_gale | 113 -> 100 (x0.88) | 75 -> 88 (x1.17) |
| evo_winters_keep | 313 -> 317 (x1.01) | 81 -> 81 (x1.00) |
| evo_frozen_gyre | 71 -> 73 (x1.02) | 44 -> 47 (x1.06) |
| evo_plague_hive | 103 -> 107 (x1.04) | 126 -> 107 (x0.85) |
| evo_cyclone_eye | 284 -> 297 (x1.04) | 116 -> 116 (x1.00) |
| evo_stormwing | 622 -> 628 (x1.01) | 241 -> 241 (x1.00) |
| evo_knife_reef | 713 -> 816 (x1.14) | 255 -> 255 (x1.00) |
| evo_hundred_forks | 218 -> 214 (x0.98) | 104 -> 104 (x1.00) |
| evo_reef_of_teeth | 403 -> 366 (x0.91) | 203 -> 203 (x1.00) |
| evo_breaching_gun | 149 -> 157 (x1.05) | 138 -> 138 (x1.00) |

Evolution ratios (evolved / max weapon, pack) stay inside the check's band;
the full table is the `--check-evolutions` output. Glass Gale's DamageScale
went 0.38 -> 0.9 because a loop passes each body once where out-and-back
passed twice. `--check-evolutions` after: band x1.47 to x2.54, mean x2.01 (PASS).

## Co-op

Nothing new goes over the wire and the protocol is unchanged. Weapons run on
their owner's machine; teammates never saw each other's shots, and still see
the evolution flash RPC as before. New knockback directions (Rimeheart pull,
harpoon reel, Undertow drag) and the Maul's stun travel in the existing hit
batch and crowd-control forwarding.

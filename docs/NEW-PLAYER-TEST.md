# New-player expedition test

This is a prepared test procedure, not evidence that a participant has tested
the game. No participants have been contacted and no sessions are recorded.
Use a temporary fresh profile, retain the player's chosen graphics settings,
and record the exact build. Do not overwrite an existing save.

## Ten-minute session

1. **First minute — choose a map.** From the main menu, find the special maps,
   choose Reef, identify the recommended character, optionally change it and
   start. Observe without coaching. Record wrong clicks and time to begin.
2. **Two minutes — water movement.** Ask the player to reach a high landmark,
   dive to a low resource, steer a dash, collect something and breach the
   surface. Do not announce the keys before they consult the tutorial/prompts.
   Record control mistakes, whether the resource reward is understood and
   whether the dash feels directional.
3. **Two minutes — cave navigation.** Ask them to find the entrance, discover
   its map marker, enter, obtain a reward and leave by the other exit. Record
   time to find the entrance, wrong turns and any interval over 20 seconds
   without a confident route. A marker must remain hidden before discovery.
4. **Three minutes — city traversal/combat.** Choose City through the menu and
   character choice. Ask them to reach a rooftop using each hook, try both
   hold and toggle modes, boost, release independently and hit a giant's core
   with dash. Record time to first rooftop, wrong-side or low-wall attachments,
   accidental release, falls, core-hit attempts and whether feedback explains
   success or failure. The player must distinguish hooks from free flight.
5. **Two minutes — co-op clarity.** With willing test participants and a real
   second client, open an upgrade hand during combat, inspect teammate DPS and
   the scoreboard, collect a shared resource and revive a teammate. Record
   obscured controls, duplicate rewards, input conflicts and inconsistent
   remote state. Skip this segment if no real second player is available and
   explicitly label it untested; automated ENet results are separate evidence.

## Ask after the tasks

- What were you trying to do when the controls behaved differently?
- Which visible object looked usable but had no useful interaction?
- Where did you lose track of the next objective or the cave exit?
- Could you tell when a core strike landed, and what caused it?
- Which sound was unclear, repetitive, too quiet or uncomfortable?
- Rate swimming, hooks, visual clarity and sound separately from 1 to 5.

## Record and act

| Metric | Record | Review trigger |
| --- | --- | --- |
| Time to start first map | Seconds; wrong clicks | Cannot start unaided in 60 s |
| Time to first rooftop | Seconds; failed hook attempts | No success after tutorial in 90 s |
| Cave route clarity | Wrong turns; longest uncertain interval | Repeated uncertainty over 20 s |
| Core feedback | Attempts; successful hits; explanation | Player cannot explain one verified hit |
| Collection clarity | Resource/reward understood; duplicates | Reward unclear or paid twice |
| Rendering | Mean/P95/P99 frame time; cold load separately | Reproducible stalls at unchanged settings |
| Co-op consistency | Both clients' observations and logs | Divergent target, rewards or statistics |

Record observations in the participant's words, distinguish them from developer
interpretation, and fix the observed problem before adding unrelated features.
Re-run only the affected task after a change. Listening, comfort and usability
ratings require a person; do not replace them with a green automated test.

# In-game What's New

For every release, add `release-notes/<application version>.json` with `Version`,
`Title` and a nonempty `Changes` array. Describe only shipped player-facing changes.
Add Arabic translations for the title and change strings in `ArabicText`.

`build.ps1` validates the matching notes before compiling or packaging; the export
preset includes the JSON files. An unknown version without notes does not show
an empty window.

The title offers the installed version's notes once per profile. Closing with
the button or Escape acknowledges that version. The main-menu WHAT'S NEW button
reopens the notes. Joining a lobby and automated gameplay checks do not interrupt
their flow with the automatic popup.

Verification: launch with `--headless -- --menu-shot=unused.png --check-release-notes`
for isolated behavior checks, or `-- --menu-shot=<png> --menu-page=whats-new --lang=ar`
for a rendered Arabic review. Neither command uses the player's profile.

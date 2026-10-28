# Windows updates

Open **GAME UPDATES** from the title screen. A newer stable release produces a
dismissible notification with its version and a **VIEW / DOWNLOAD UPDATE**
action. It appears once per release per process; checking never installs.

**DOWNLOAD & INSTALL** is explicit authorization to download, verify and launch
the updater. The game closes only after the helper acknowledges readiness.
Cancellation and failed handoff leave the game open. A verified package can be
retried without downloading again. Back/Escape cancels a busy operation before
leaving the page. English and Arabic use the same controls and state.

Installed Windows x64 copies use the matching official setup executable and its
normal wizard. Portable copies use the matching ZIP and retain unlisted files.
Development trees, editor/debug builds, Steam-managed copies, unknown installs,
linked locations and unsupported platforms direct users to Releases.

The first version containing this updater is **1.4.0**. Earlier versions need a
manual upgrade once. Installer and portable asset names must remain:

- `Wavebreak-Setup-<semantic-version>.exe`
- `Wavebreak-<semantic-version>-windows.zip`

Only stable versions newer than the installed version are offered; semantic
numeric comparison prevents `1.10.0` being treated as older than `1.9.9`.
Drafts/prereleases are rejected. Automatic startup checks use a one-hour cache,
ETags and an eight-second request bound. Manual checks bypass cache freshness.
Offline, rate-limited and missing-release checks are retryable.

The source is the public canonical repository's GitHub Releases API. Assets must
have the exact versioned filename, HTTPS official release URL, supported MIME
type, bounded size and GitHub-provided SHA-256 digest. Redirects are restricted
to GitHub's release asset hosts. Downloads are streamed and verified before
preparation; ZIP entries reject traversal, duplicate names, links, unsafe Windows
names and profile/source-tree paths. Windows executable and Godot pack headers
and the required exported layout are checked.

Portable installation uses fixed embedded Windows PowerShell 5.1 scripts written
with UTF-8 BOM so Arabic survives decoding. The helper waits for the known game
PID/start time/executable, never kills it, and times out without replacing files
if it remains open. It rejects unsafe installation and staging paths, checks
payload hashes and space, holds a root lock, copies to per-file temporary paths,
backs up originals and rolls back on failure/cancellation. Recoverable backups
remain under `.wavebreak-update-backup-<stage-id>` in the portable folder.
The game's ordinary AppData saves are outside the update transaction.

Verification commands:

```powershell
dotnet build --no-restore
& $Godot --headless --path . --audio-driver Dummy -- --check-updater
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/updater-portable.ps1
```

These tests use mocked network responses, hidden form construction and harmless
locally compiled fixture executables in temporary fake installations. They do
not execute a downloaded installer or replace an actual installation.

Metadata contract: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases)
and [API versioning](https://docs.github.com/en/rest/about-the-rest-api/api-versions).

param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][a-zA-Z0-9.-]+)?$') { throw 'Invalid release notes version' }
$notesPath = Join-Path $Root "release-notes/$Version.json"
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) { throw "Add player-facing release notes before packaging: $notesPath" }
$notes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($notes.Version -ne $Version -or [string]::IsNullOrWhiteSpace($notes.Title) -or @($notes.Changes).Count -eq 0) {
    throw "Release notes require matching Version, Title and Changes: $notesPath"
}
foreach ($change in $notes.Changes) {
    if ($change -isnot [string] -or [string]::IsNullOrWhiteSpace($change)) { throw 'Release notes contain an empty or invalid change' }
}
Write-Output "RELEASE NOTES PASS $Version"

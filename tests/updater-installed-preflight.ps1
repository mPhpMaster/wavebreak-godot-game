param([Parameter(Mandatory)][string]$ExistingStage)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $ExistingStage 'request.json') -Raw | ConvertFrom-Json
if ($source.mode -ne 'installed') { throw 'Expected a real installed update request.' }
$versionBefore = (Get-Item -LiteralPath $source.gameExe).VersionInfo.ProductVersion
$stageId = [Guid]::NewGuid().ToString('N')
$stage = Join-Path ([IO.Path]::GetTempPath()) ('WavebreakUpdates\' + $stageId)
New-Item -ItemType Directory -Path $stage | Out-Null
[IO.File]::WriteAllText((Join-Path $stage '.wavebreak-stage'), 'Wavebreak:' + $stageId)
foreach ($helper in @('ApplyUpdate.ps1','UpdatePortable.ps1','UpdateProgress.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root ('updater\' + $helper)) -Destination (Join-Path $stage $helper)
}
Copy-Item -LiteralPath (Join-Path $ExistingStage $source.assetName) -Destination (Join-Path $stage $source.assetName)
$requestPath = Join-Path $stage 'request.json'
[IO.File]::WriteAllText($requestPath, ($source | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -STA -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $stage 'ApplyUpdate.ps1') -RequestPath $requestPath -NoUi -PrepareOnly
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $stage 'status.json'); throw 'Installed preflight failed.' }
$status = Get-Content -LiteralPath (Join-Path $stage 'status.json') -Raw | ConvertFrom-Json
if ($status.phase -ne 'prepared' -or -not (Test-Path -LiteralPath (Join-Path $stage 'ready.json'))) { throw 'Missing readiness acknowledgement.' }
if ((Get-Item -LiteralPath $source.gameExe).VersionInfo.ProductVersion -ne $versionBefore) { throw 'Preflight unexpectedly changed installed version.' }
Write-Output ('INSTALLED PREFLIGHT PASS installed=' + $versionBefore + ' root=' + $source.installRoot + ' stage=' + $stage)

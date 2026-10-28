param([string]$Godot = 'D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $env:TEMP ('wavebreak-levelup-layouts-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
foreach ($layout in @('full', 'compact')) {
    $port = Get-Random -Minimum 39000 -Maximum 49000
    $hostLog = Join-Path $evidence "$layout-host.log"
    $clientLog = Join-Path $evidence "$layout-client.log"
    $common = '--headless --path "' + $taskRoot + '" --audio-driver Dummy --quit-after 9000 --log-file '
    $hostArgs = $common + '"' + $hostLog + '" -- --coop-host=' + $port + ' --coop-players=2 --coop-seconds=30 --coop-down-at=9999 --seed=42 --auto --invulnerable'
    $clientArgs = $common + '"' + $clientLog + '" -- --coop-join=127.0.0.1:' + $port + ' --coop-seconds=30 --coop-down-at=9999 --coop-levelup-at=3 --auto --invulnerable'
    if ($layout -eq 'compact') { $clientArgs += ' --compact-levelup' }
    $hostProcess = Start-Process -FilePath $Godot -ArgumentList $hostArgs -WindowStyle Hidden -PassThru
    $clientProcess = $null
    try {
        Start-Sleep -Milliseconds 800
        $clientProcess = Start-Process -FilePath $Godot -ArgumentList $clientArgs -WindowStyle Hidden -PassThru
        if (-not $clientProcess.WaitForExit(60000)) { throw "$layout client timed out" }
        if (-not $hostProcess.WaitForExit(15000)) { throw "$layout host timed out" }
        foreach ($log in @($hostLog, $clientLog)) {
            $text = Get-Content -LiteralPath $log -Raw
            if ($text -notmatch 'COOP PASS' -or $text -match 'COOP FAIL|SCRIPT ERROR|ERROR:') { throw "Layout check failed: $log" }
        }
        Write-Output "LEVELUP LAYOUT PASS $layout"
    } finally {
        foreach ($process in @($hostProcess, $clientProcess)) {
            if ($null -ne $process) {
                if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
                $process.Dispose()
            }
        }
    }
}
Write-Output "LEVELUP LAYOUTS PASS evidence=$evidence"

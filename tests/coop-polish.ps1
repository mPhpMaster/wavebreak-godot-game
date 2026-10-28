param([string]$Godot='D:\tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$evidence=Join-Path $env:TEMP ('wavebreak-coop-polish-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$port=Get-Random -Minimum 40000 -Maximum 49000
$processes=@()
try {
    foreach($role in @('host','a','b')) {
        $connection=if($role -eq 'host'){'--coop-host='+$port+' --coop-players=3'}else{'--coop-join=127.0.0.1:'+$port}
        $log=Join-Path $evidence ($role+'.log')
        $arguments='--headless --path "'+$taskRoot+'" --audio-driver Dummy --quit-after 6000 --log-file "'+$log+'" -- '+$connection+' --check-coop-polish --route=reef-dive --coop-seconds=999 --coop-down-at=9999 --character=nori --invulnerable'
        $processes+=Start-Process -FilePath $Godot -ArgumentList $arguments -WindowStyle Hidden -PassThru
        Start-Sleep -Milliseconds 500
    }
    foreach($process in $processes) { if(-not $process.WaitForExit(60000)){throw 'Three-peer polish test timed out'} }
    foreach($role in @('host','a','b')) {
        $log=Join-Path $evidence ($role+'.log')
        $body=Get-Content -LiteralPath $log -Raw
        if($body -notmatch 'COOP POLISH PASS' -or $body -match 'COOP POLISH FAIL|SCRIPT ERROR|ERROR:'){throw "Polish check failed: $log"}
    }
    Write-Output "COOP POLISH PASS evidence=$evidence"
} finally {
    foreach($process in $processes){if(-not $process.HasExited){Stop-Process -Id $process.Id -Force};$process.Dispose()}
}

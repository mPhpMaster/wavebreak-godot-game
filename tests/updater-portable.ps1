param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$evidence = Join-Path ([IO.Path]::GetTempPath()) ('wavebreak-updater-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$source = Join-Path $evidence 'Fixture.cs'
[IO.File]::WriteAllText($source, 'class Fixture { static int Main(string[] args) { System.Threading.Thread.Sleep(int.Parse(args[0])); return 0; } }')
$fixture = Join-Path $evidence 'Fixture.exe'
& $compiler /nologo /target:winexe "/out:$fixture" $source
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
function Assert-Check([bool]$Ok, [string]$Label) { if (-not $Ok) { throw $Label }; Write-Output ('UPDATER HELPER OK ' + $Label) }

foreach ($scenario in @('apply','rollback','cancel','timeout','bad-hash')) {
    $caseRoot = Join-Path $evidence $scenario
    $root = Join-Path $caseRoot 'portable'
    New-Item -ItemType Directory -Path (Join-Path $root 'data_Wavebreak_windows_x86_64') -Force | Out-Null
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $root 'Wavebreak.exe')
    [IO.File]::WriteAllText((Join-Path $root 'Wavebreak.pck'), 'old-pack')
    [IO.File]::WriteAllText((Join-Path $root 'data_Wavebreak_windows_x86_64\Wavebreak.dll'), 'old-library')
    [IO.File]::WriteAllText((Join-Path $root 'user-extra.txt'), 'preserve-extra')
    $stageId = [Guid]::NewGuid().ToString('N')
    $stage = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'WavebreakUpdates') $stageId
    New-Item -ItemType Directory -Path (Join-Path $stage 'payload\data_Wavebreak_windows_x86_64') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage '.wavebreak-stage'), ('Wavebreak:' + $stageId))
    foreach ($name in @('ApplyUpdate.ps1','UpdatePortable.ps1','UpdateProgress.ps1')) {
        [IO.File]::WriteAllText((Join-Path $stage $name), [IO.File]::ReadAllText((Join-Path $ProjectRoot ('updater\' + $name)), [Text.Encoding]::UTF8), [Text.UTF8Encoding]::new($true))
    }
    $files = @()
    foreach ($relative in @('Wavebreak.exe','Wavebreak.pck','data_Wavebreak_windows_x86_64/Wavebreak.dll','steam_api64.dll','steam_appid.txt')) {
        $target = Join-Path (Join-Path $stage 'payload') $relative
        if ($relative -eq 'Wavebreak.exe') { Copy-Item -LiteralPath $fixture -Destination $target }
        else { [IO.File]::WriteAllText($target, ('new-' + $relative)) }
        $files += @{ path=$relative; size=(Get-Item -LiteralPath $target).Length; sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash }
    }
    $assetName = 'Wavebreak-1.3.4-windows.zip'
    $asset = Join-Path $stage $assetName
    [IO.File]::WriteAllText($asset, 'fixture-download-hash-bound-to-validated-payload')
    $lifetime = if ($scenario -eq 'timeout') { '12000' } else { '2400' }
    $game = Start-Process -FilePath (Join-Path $root 'Wavebreak.exe') -ArgumentList $lifetime -WindowStyle Hidden -PassThru
    $request = @{ schema=1; product='Wavebreak'; mode='portable'; version='1.3.4'; installRoot=$root;
        gameExe=(Join-Path $root 'Wavebreak.exe'); gamePid=$game.Id; gameStartedUtcTicks=$game.StartTime.ToUniversalTime().Ticks.ToString();
        assetName=$assetName; assetSha256=(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash; files=$files; restart=$false; language='ar' }
    if ($scenario -eq 'bad-hash') { $request.assetSha256 = 'a' * 64 }
    $requestPath = Join-Path $stage 'request.json'
    [IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $held = $null
    if ($scenario -eq 'rollback') { $held = [IO.File]::Open((Join-Path $root 'Wavebreak.pck'), 'Open', 'ReadWrite', 'None') }
    if ($scenario -eq 'cancel') { [IO.File]::WriteAllText((Join-Path $stage 'cancel.request'), 'cancel') }
    $wait = if ($scenario -eq 'timeout') { 1 } else { 10 }
    $arguments = '-STA -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "' + (Join-Path $stage 'ApplyUpdate.ps1') + '" -RequestPath "' + $requestPath + '" -NoUi -WaitSeconds ' + $wait
    $helper = Start-Process -FilePath $powershell -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $caseRoot 'stderr.txt')
    $null = $helper.Handle
    try {
        if (-not $helper.WaitForExit(20000)) { throw 'Helper fixture timed out.' }
        $helper.Refresh()
        $status = [IO.File]::ReadAllText((Join-Path $stage 'status.json')) | ConvertFrom-Json
        if ($scenario -eq 'apply') {
            Assert-Check ($helper.ExitCode -eq 0 -and $status.phase -eq 'applied') 'actual helper applies to fake portable installation'
            Assert-Check ([IO.File]::ReadAllText((Join-Path $root 'Wavebreak.pck')) -eq 'new-Wavebreak.pck') 'new payload installed'
            Assert-Check ([IO.File]::ReadAllText((Join-Path $status.backup 'Wavebreak.pck')) -eq 'old-pack') 'original preserved in version backup'
        } else {
            Assert-Check ($helper.ExitCode -ne 0) ($scenario + ' exits without success')
            if ($held) { $held.Dispose(); $held = $null }
            Assert-Check ([IO.File]::ReadAllText((Join-Path $root 'Wavebreak.pck')) -eq 'old-pack') ($scenario + ' preserves original pack')
            Assert-Check ([IO.File]::ReadAllText((Join-Path $root 'data_Wavebreak_windows_x86_64\Wavebreak.dll')) -eq 'old-library') ($scenario + ' restores earlier library replacement')
            if ($scenario -eq 'rollback') { Assert-Check ($status.phase -eq 'rolled_back') 'locked target rolls back earlier replacements' }
            if ($scenario -eq 'bad-hash') { Assert-Check (-not (Test-Path -LiteralPath (Join-Path $stage 'ready.json'))) 'hash failure occurs before ready acknowledgement' }
            if ($scenario -eq 'timeout') { Assert-Check (-not $game.HasExited) 'timeout never kills the known game process' }
        }
        Assert-Check ([IO.File]::ReadAllText((Join-Path $root 'user-extra.txt')) -eq 'preserve-extra') ($scenario + ' preserves unlisted user files')
        Assert-Check (@(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.wavebreak-new-*').Count -eq 0) ($scenario + ' leaves no partial copy files')
    } finally {
        if ($held) { $held.Dispose() }
        $null = $game.WaitForExit(15000)
        $game.Dispose(); $helper.Dispose()
    }
}
Write-Output ('UPDATER HELPER PASS evidence=' + $evidence)

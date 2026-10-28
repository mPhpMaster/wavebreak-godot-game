param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$ExportRoot,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Run this verification with PowerShell 7 (.NET), not Windows PowerShell 5.1.' }
$assemblyPath = Join-Path $ExportRoot 'data_Wavebreak_windows_x86_64\Wavebreak.dll'
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($assemblyPath))
$paths = $assembly.GetType('Wavebreak.Updates.UpdatePaths', $true)
$package = $assembly.GetType('Wavebreak.Updates.PortableUpdatePackage', $true)
[string]$stage = $paths.GetMethod('CreateStage').Invoke($null, @())
try {
    [string]$payload = Join-Path $stage 'payload'
    $task = $package.GetMethod('ExtractAsync').Invoke($null, @([IO.Path]::GetFullPath($Archive), $payload, [Threading.CancellationToken]::None))
    $manifest = $task.GetAwaiter().GetResult()
    if ($manifest.Count -lt 5) { throw 'Required exported payload is incomplete.' }
    foreach ($file in $manifest) {
        $exported = Join-Path $ExportRoot $file.Path
        if (-not (Test-Path -LiteralPath $exported -PathType Leaf)) { throw ('ZIP differs from exported directory: ' + $file.Path) }
        if ((Get-FileHash -LiteralPath $exported -Algorithm SHA256).Hash -ne $file.Sha256) { throw ('ZIP checksum mismatch: ' + $file.Path) }
    }
    $exportFiles = @(Get-ChildItem -LiteralPath $ExportRoot -Recurse -File)
    if ($exportFiles.Count -ne $manifest.Count) { throw 'ZIP omits files from the exported directory.' }
    $exeVersion = (Get-Item -LiteralPath (Join-Path $ExportRoot 'Wavebreak.exe')).VersionInfo.ProductVersion
    if ($exeVersion -ne ($Version + '.0')) { throw ('Wrong Windows executable version: ' + $exeVersion) }
    $handoff = $assembly.GetType('Wavebreak.Updates.WindowsUpdateHandoff', $true)
    $handoff.GetMethod('WriteHelpers', [Reflection.BindingFlags]'Static,NonPublic').Invoke($null, @($stage))
    foreach ($helper in @('ApplyUpdate.ps1','UpdatePortable.ps1','UpdateProgress.ps1')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $stage $helper))
        if ($bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) { throw ('Embedded updater lacks UTF8 BOM: ' + $helper) }
    }
    $sourceHelper = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'updater\ApplyUpdate.ps1'), [Text.Encoding]::UTF8)
    if ([IO.File]::ReadAllText((Join-Path $stage 'ApplyUpdate.ps1'), [Text.Encoding]::UTF8) -ne $sourceHelper) { throw 'Exported updater helper differs from current source.' }
    Write-Output ('RELEASE PACKAGE PASS version=' + $Version + ' files=' + $manifest.Count + ' sha256=' + (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash)
} finally {
    $paths.GetMethod('Cleanup').Invoke($null, @($stage))
}

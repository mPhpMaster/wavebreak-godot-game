# Run a command on a separate, invisible Windows desktop.
#
# Godot test runs open a real window (they need the GPU to render screenshots),
# and that window takes focus on top of whatever the user is doing. Godot ignores
# minimised/hidden start-up styles and pulls off-screen windows back on screen.
# A second desktop in the same window station is the one thing it cannot escape:
# the process gets a real, GPU-backed window, but on a desktop the user never
# sees, so nothing pops up and focus is never stolen.
#
#   powershell -File D:\tools\megabonk\run-offscreen.ps1 -Command '<cmdline>' [-Out <file>] [-TimeoutSec 900]
#
# The command runs through cmd.exe so its stdout/stderr can be redirected to
# -Out (child processes inherit the desktop). Exit code is the command's.

param(
    [Parameter(Mandatory)] [string]$Command,
    [string]$Out,
    [int]$TimeoutSec = 1800,
    [string]$DesktopName = 'WavebreakTests',
    [string]$WorkDir = 'D:\tools\megabonk'
)

$ErrorActionPreference = 'Stop'

if (-not ('OffscreenDesktop2' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class OffscreenDesktop2 {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcess(string app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
        uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
"@
}

# GENERIC_ALL. CreateDesktop opens the desktop if it already exists.
$desk = [OffscreenDesktop2]::CreateDesktop($DesktopName, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
if ($desk -eq [IntPtr]::Zero) { throw "CreateDesktop failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }

try {
    $line = if ($Out) { "cmd.exe /d /s /c `"$Command > `"$Out`" 2>&1`"" } else { "cmd.exe /d /s /c `"$Command`"" }

    $si = New-Object OffscreenDesktop2+STARTUPINFO
    $si.cb = [Runtime.InteropServices.Marshal]::SizeOf($si)
    $si.lpDesktop = "WinSta0\$DesktopName"
    $pi = New-Object OffscreenDesktop2+PROCESS_INFORMATION

    # CREATE_NO_WINDOW (0x08000000): no console window for cmd.exe either.
    # CreateProcessW may write into the command-line buffer, so it must be a
    # mutable StringBuilder; the application path is explicit so no PATH search.
    $cmdExe = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $buffer = New-Object System.Text.StringBuilder $line, 32768
    if (-not [OffscreenDesktop2]::CreateProcess($cmdExe, $buffer, [IntPtr]::Zero, [IntPtr]::Zero, $false,
            0x08000000, [IntPtr]::Zero, $WorkDir, [ref]$si, [ref]$pi)) {
        throw "CreateProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }

    $waited = [OffscreenDesktop2]::WaitForSingleObject($pi.hProcess, [uint32]($TimeoutSec * 1000))
    if ($waited -ne 0) {
        # Kill the whole tree: terminating cmd.exe alone orphans the game under
        # it, which keeps running on the hidden desktop and writing to -Out.
        & taskkill.exe /PID $pi.dwProcessId /T /F 2>&1 | Out-Null
        [void][OffscreenDesktop2]::TerminateProcess($pi.hProcess, 124)
        Write-Warning "timed out after ${TimeoutSec}s"
    }
    $code = 0
    [void][OffscreenDesktop2]::GetExitCodeProcess($pi.hProcess, [ref]$code)
    [void][OffscreenDesktop2]::CloseHandle($pi.hThread)
    [void][OffscreenDesktop2]::CloseHandle($pi.hProcess)
    exit [int]$code
}
finally {
    [void][OffscreenDesktop2]::CloseDesktop($desk)
}

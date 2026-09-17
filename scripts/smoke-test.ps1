<#
.SYNOPSIS
  Launches a published CrashScope.Desktop.exe, verifies a real top-level window appears,
  checks for crash logs, then shuts it down. Exit code 0 = pass.
.PARAMETER ExePath
  Path to CrashScope.Desktop.exe (default: dist\toolbox-win-x64\Desktop\CrashScope.Desktop.exe).
.PARAMETER TimeoutSeconds
  How long to wait for the main window (default 20).
.PARAMETER KeepRunning
  Leave the app open after the check (for manual testing).
#>
param(
    [string]$ExePath,
    [int]$TimeoutSeconds = 20,
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $repo 'dist\toolbox-win-x64\Desktop\CrashScope.Desktop.exe' }
$ExePath = (Resolve-Path $ExePath).Path
$exeDir = Split-Path -Parent $ExePath
$startupLog = Join-Path $env:LOCALAPPDATA 'CrashScope\logs\startup.log'
$crashLogs = @(
    (Join-Path $env:LOCALAPPDATA 'CrashScope\crash.log'),
    (Join-Path $exeDir 'crash.log'),
    (Join-Path $exeDir 'startup.log')
)

function Fail($msg) {
    Write-Host "SMOKE FAIL: $msg" -ForegroundColor Red
    if (Test-Path $startupLog) {
        Write-Host "--- tail of $startupLog ---" -ForegroundColor Yellow
        Get-Content $startupLog -Tail 40
    }
    foreach ($c in $crashLogs) {
        if (Test-Path $c) { Write-Host "--- $c ---" -ForegroundColor Yellow; Get-Content $c -Tail 40 }
    }
    exit 1
}

Write-Host "Smoke test: $ExePath"

# Make sure a hidden-to-tray instance from a previous run does not make this pass for free.
$stale = Get-Process -Name 'CrashScope.Desktop' -ErrorAction SilentlyContinue
if ($stale) {
    Write-Host "Stopping $($stale.Count) stale CrashScope.Desktop process(es)" -ForegroundColor Yellow
    $stale | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# Mark-of-the-Web blocks are the #1 cause of "spinner then nothing" on a fresh machine.
$blocked = Get-ChildItem $exeDir -Recurse -File | Where-Object { Get-Item -Path $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue }
if ($blocked) { Write-Host "WARN: $($blocked.Count) files carry Zone.Identifier (MotW). Run: Get-ChildItem -Recurse '$exeDir' | Unblock-File" -ForegroundColor Yellow }

$crashBefore = @{}
foreach ($c in $crashLogs) { $crashBefore[$c] = if (Test-Path $c) { (Get-Item $c).Length } else { 0 } }
$startupBefore = if (Test-Path $startupLog) { (Get-Item $startupLog).Length } else { 0 }

$p = Start-Process -FilePath $ExePath -WorkingDirectory $exeDir -PassThru
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$hwnd = [IntPtr]::Zero
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ($p.HasExited) { Fail "process exited early with code $($p.ExitCode)" }
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and $p.MainWindowTitle) { $hwnd = $p.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { Fail "no main window after $TimeoutSeconds s (pid $($p.Id), responding=$($p.Responding))" }

Write-Host ("PASS: window '{0}' hwnd={1} pid={2} ws={3}MB responding={4}" -f $p.MainWindowTitle, $hwnd, $p.Id, [int]($p.WorkingSet64/1MB), $p.Responding) -ForegroundColor Green

# Let the delayed startup work (previous-session check, retention) run before judging crash logs.
Start-Sleep -Seconds 4
if ($p.HasExited) { Fail "process exited after showing window, code $($p.ExitCode)" }

foreach ($c in $crashLogs) {
    $after = if (Test-Path $c) { (Get-Item $c).Length } else { 0 }
    if ($after -gt $crashBefore[$c]) { Fail "crash log grew: $c" }
}
if (Test-Path $startupLog) {
    $new = Get-Content $startupLog | Select-Object -Skip 0 | Where-Object { $_ -match '\[ERROR\]' }
    $errCount = @($new).Count
    $newBytes = (Get-Item $startupLog).Length - $startupBefore
    Write-Host "startup.log: +$newBytes bytes this run; total [ERROR] lines in file: $errCount"
    Get-Content $startupLog -Tail 6
} else {
    Write-Host "WARN: startup.log not found at $startupLog (old build?)" -ForegroundColor Yellow
}

# Second-instance check: launching again must NOT spawn a second process.
$p2 = Start-Process -FilePath $ExePath -WorkingDirectory $exeDir -PassThru
Start-Sleep -Seconds 3
$count = @(Get-Process -Name 'CrashScope.Desktop' -ErrorAction SilentlyContinue).Count
if ($count -ne 1) { Fail "expected exactly 1 CrashScope.Desktop process after second launch, found $count" }
Write-Host "PASS: single-instance guard (second launch exited, 1 process remains)" -ForegroundColor Green

if ($KeepRunning) { Write-Host "Leaving app running (pid $($p.Id))."; exit 0 }

Stop-Process -Id $p.Id -Force
Write-Host "SMOKE PASS" -ForegroundColor Green
exit 0

param([switch]$SkipSmoke)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repo 'dist'
$toolboxRoot = Join-Path $distRoot 'toolbox-win-x64'
$agentOut = Join-Path $distRoot 'agent-win-x64'
$serviceOut = Join-Path $distRoot 'service-win-x64'
$desktopOut = Join-Path $distRoot 'desktop-win-x64'

function Resolve-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $userDotnet) { return $userDotnet }
    throw "dotnet SDK not found. Install .NET 8 SDK."
}

$dotnet = Resolve-Dotnet
Write-Host "Using dotnet: $dotnet"
& $dotnet --version
if ($LASTEXITCODE -ne 0) { throw "dotnet --version failed" }

# A running CrashScope from a previous test locks files under dist\ and would leave us with a
# half-old, half-new tree (that is exactly how the "double-click, spinner, no window" package
# got produced). Stop our own processes, then fail hard if the directory still cannot be removed.
$running = Get-Process -Name 'CrashScope.Desktop','CrashScope.Agent','CrashScope.Service' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Write-Host "Stopping $($running.Count) CrashScope process(es) running from dist\" -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}
if (Test-Path $distRoot) {
    Write-Host "Cleaning $distRoot"
    try {
        Remove-Item -Recurse -Force $distRoot -ErrorAction Stop
    } catch {
        throw "Cannot clean $distRoot ($_). Close Explorer windows / editors / the app using it and re-run. Refusing to publish into a dirty directory."
    }
}
if (Test-Path $distRoot) { throw "$distRoot still exists after cleanup; refusing to publish." }
New-Item -ItemType Directory -Path $toolboxRoot -Force | Out-Null

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$OutPath
    )
    Write-Host ""
    Write-Host "Publishing $ProjectPath -> $OutPath" -ForegroundColor Cyan
    if (Test-Path $OutPath) { Remove-Item -Recurse -Force $OutPath }
    & $dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true -o $OutPath /p:DebugType=None /p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $ProjectPath (exit $LASTEXITCODE)" }
    if (-not (Test-Path $OutPath)) { throw "Publish output missing: $OutPath" }
    Write-Host "OK: $OutPath" -ForegroundColor Green
}

$agentProj = Join-Path $repo 'src\CrashScope.Agent\CrashScope.Agent.csproj'
$serviceProj = Join-Path $repo 'src\CrashScope.Service\CrashScope.Service.csproj'
$desktopProj = Join-Path $repo 'src\CrashScope.Desktop\CrashScope.Desktop.csproj'

Publish-Project -ProjectPath $agentProj -OutPath $agentOut
Publish-Project -ProjectPath $serviceProj -OutPath $serviceOut
Publish-Project -ProjectPath $desktopProj -OutPath $desktopOut

$tbAgent = Join-Path $toolboxRoot 'Agent'
$tbService = Join-Path $toolboxRoot 'Service'
$tbDesktop = Join-Path $toolboxRoot 'Desktop'

New-Item -ItemType Directory -Path $tbAgent -Force | Out-Null
New-Item -ItemType Directory -Path $tbService -Force | Out-Null
New-Item -ItemType Directory -Path $tbDesktop -Force | Out-Null

Copy-Item -Recurse -Force -Path "$agentOut\*" -Destination $tbAgent
Copy-Item -Recurse -Force -Path "$serviceOut\*" -Destination $tbService
Copy-Item -Recurse -Force -Path "$desktopOut\*" -Destination $tbDesktop

$readme = @"
CrashScope Toolbox - Windows x64 self-contained
Build: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
Runtime: .NET 8 self-contained, no install required
Contents:
  Agent/   - CLI recorder (CrashScope.Agent.exe)
  Service/ - Windows Service host (CrashScope.Service.exe)
  Desktop/ - WPF toolbox (CrashScope.Desktop.exe) - default entry
Usage:
  1. Run Desktop/CrashScope.Desktop.exe
  2. Desktop is passive collection only. If you run Agent/CrashScope.Agent.exe, pass --no-actions
     to disable system-mutation verbs (see Agent/start-agent-passive.cmd)
  3. Data root: %LOCALAPPDATA%\CrashScope and %PROGRAMDATA%\CrashScope
  4. Only one recorder per data root (state\recorder.lock)
  5. Stop old hosts before upgrade
Troubleshooting:
  - Window does not appear: check %LOCALAPPDATA%\CrashScope\logs\startup.log
  - Extracted from a downloaded ZIP: right-click ZIP -> Properties -> Unblock BEFORE extracting,
    or run in PowerShell: Get-ChildItem -Recurse <folder> | Unblock-File
  - Only one instance runs; a second double-click brings the existing window to front
Notes:
  - Includes reliability convergence (docs/mcp-reliability-convergence.md)
  - No driver/BIOS/power changes by default
  - Export redaction on by default
"@
Set-Content -Path (Join-Path $toolboxRoot 'README.txt') -Value $readme -Encoding UTF8
Set-Content -Path (Join-Path $tbAgent 'start-agent-passive.cmd') -Value "@echo off`r`n`"%~dp0CrashScope.Agent.exe`" --no-actions %*`r`n" -Encoding ASCII

$license = Join-Path $repo 'LICENSE'
if (Test-Path $license) { Copy-Item $license (Join-Path $toolboxRoot 'LICENSE.txt') -Force }

$zipToolbox = Join-Path $distRoot 'CrashScope.Toolbox.win-x64.zip'
$zipAgent = Join-Path $distRoot 'CrashScope.Agent.win-x64.zip'
$zipDesktop = Join-Path $distRoot 'CrashScope.Desktop.win-x64.zip'

if (Test-Path $zipToolbox) { Remove-Item $zipToolbox -Force }
if (Test-Path $zipAgent) { Remove-Item $zipAgent -Force }
if (Test-Path $zipDesktop) { Remove-Item $zipDesktop -Force }

Write-Host ""
Write-Host "Compressing $toolboxRoot -> $zipToolbox"
Compress-Archive -Path "$toolboxRoot\*" -DestinationPath $zipToolbox -Force

Write-Host "Compressing $agentOut -> $zipAgent"
Compress-Archive -Path "$agentOut\*" -DestinationPath $zipAgent -Force

Write-Host "Compressing $desktopOut -> $zipDesktop"
Compress-Archive -Path "$desktopOut\*" -DestinationPath $zipDesktop -Force

Write-Host ""
Write-Host "Published:" -ForegroundColor Green
Write-Host "  $zipToolbox"
Write-Host "  $zipAgent"
Write-Host "  $zipDesktop"
Write-Host "  $toolboxRoot (unpacked)"

if (-not $SkipSmoke) {
    Write-Host ""
    Write-Host "Running smoke test on published Desktop..." -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'smoke-test.ps1') -ExePath (Join-Path $tbDesktop 'CrashScope.Desktop.exe')
    if ($LASTEXITCODE -ne 0) { throw "Smoke test FAILED (exit $LASTEXITCODE). Do not ship this build." }
}

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$agentProject = Join-Path $repo 'src\CrashScope.Agent\CrashScope.Agent.csproj'
$appProject = Join-Path $repo 'src\CrashScope.App\CrashScope.App.csproj'

$agent = Get-Process -Name 'CrashScope.Agent' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $agent) {
    Write-Host 'Starting CrashScope.Agent as Administrator...'
    Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project',"`"$agentProject`"") -Verb RunAs
    Start-Sleep -Seconds 2
}
else {
    Write-Host "CrashScope.Agent already running (PID $($agent.Id))."
}

Write-Host 'Starting CrashScope dashboard...'
dotnet run --project $appProject
